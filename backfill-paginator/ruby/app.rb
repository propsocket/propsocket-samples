# frozen_string_literal: true

# PropSocket backfill paginator — Ruby.
#
# Walks every page of all four top-level entities (parents before children) and
# "upserts" each record into a local JSON key/value store keyed by `id`. Keying
# on `id` makes re-runs idempotent (no-ops). Uses limit=100 and a stable sort
# (created_at:asc) so pagination doesn't skip/duplicate rows mid-walk. Honors
# 429 Retry-After.
#
# See the shared contract for the full API surface.

require "faraday"
require "json"

DEFAULT_BASE_URL = "https://api.propsocket.io/v1"

# Parents before children so referenced records exist first in the sink.
ENTITIES = %w[properties units residents leases].freeze

# Backfill uses the max page size and a stable ascending sort so offset paging
# stays consistent across the whole walk.
PAGE_LIMIT = 100
ORDER_BY = "created_at:asc"

# Reference upsert sink: a JSON file keyed by record id. NOT a real DB — it's a
# stand-in so re-running is a no-op (we overwrite by id, never duplicate).
STORE_PATH = File.join(__dir__, "backfill-store.json")

def load_store
  return {} unless File.exist?(STORE_PATH)

  JSON.parse(File.read(STORE_PATH))
rescue JSON::ParserError
  {}
end

def save_store(store)
  File.write(STORE_PATH, JSON.pretty_generate(store))
end

# Print an RFC 7807 problem+json error and exit non-zero, surfacing request_id.
def die_on_error(response)
  problem = begin
    JSON.parse(response.body.to_s)
  rescue JSON::ParserError
    {}
  end

  request_id = problem["request_id"] || response.headers["x-request-id"] || "(none)"
  detail = problem["detail"] || (response.body.to_s.empty? ? "(no detail)" : response.body.to_s)
  title = problem["title"] || "Request failed"

  warn "Error #{response.status} #{title}: #{detail} [request_id=#{request_id}]"
  exit 1
end

# GET one page, retrying transparently on 429 by honoring Retry-After. We sleep
# exactly the server-specified number of seconds — we do NOT invent a backoff.
def get_page(conn, entity, offset)
  loop do
    response = conn.get(entity, { limit: PAGE_LIMIT, offset: offset, "order-by" => ORDER_BY })

    if response.status == 429
      retry_after = (response.headers["retry-after"] || "1").to_i
      retry_after = 1 if retry_after < 1
      warn "Rate limited on #{entity} (offset #{offset}); sleeping #{retry_after}s per Retry-After"
      sleep(retry_after)
      next
    end

    die_on_error(response) unless response.success?
    return JSON.parse(response.body.to_s)
  end
end

def backfill_entity(conn, store, entity)
  offset = 0
  count = 0

  loop do
    body = get_page(conn, entity, offset)
    results = body["results"] || []
    meta = body["meta"] || {}

    results.each do |record|
      # Upsert keyed by id => idempotent. A re-run overwrites the same key.
      store[record["id"]] = record
      count += 1
    end

    break unless meta["hasMore"]

    offset += PAGE_LIMIT
  end

  count
end

def main
  api_key = ENV["PROPSOCKET_API_KEY"]
  if api_key.nil? || api_key.empty?
    warn "PROPSOCKET_API_KEY is not set. Copy .env.example to .env."
    exit 1
  end

  base_url = (ENV["PROPSOCKET_BASE_URL"] || DEFAULT_BASE_URL).sub(%r{/+\z}, "")

  conn = Faraday.new(url: base_url) do |f|
    f.headers["Authorization"] = "Bearer #{api_key}"
    f.options.timeout = 30
  end

  store = load_store

  ENTITIES.each do |entity|
    count = backfill_entity(conn, store, entity)
    save_store(store)
    puts "#{entity}: upserted #{count} record(s)"
  end

  puts
  puts "Store now holds #{store.size} total record(s) at #{STORE_PATH}"
end

main if $PROGRAM_NAME == __FILE__
