# frozen_string_literal: true

# PropSocket REST consumer — list one entity and print id/name/status per row.
#
# Reads the PropSocket REST API (read-only, GET) using offset-based pagination
# and prints a row per record plus a final total count. Defaults to the
# `properties` entity; change ENTITY below to swap (units, residents, leases).
#
# See the shared contract for the full API surface.

require "faraday"
require "json"

# Default base URL; override with PROPSOCKET_BASE_URL.
DEFAULT_BASE_URL = "https://api.propsocket.io/v1"

# Swap this single constant to consume a different entity. The API exposes
# four top-level list endpoints: properties, units, residents, leases.
ENTITY = "properties"

# rest-consumer stays simple: a modest page size is fine (max allowed is 100).
PAGE_LIMIT = 25

# Print an RFC 7807 problem+json error and exit non-zero. We surface request_id
# so a user can quote it to support. The backfill sample handles 429/Retry-After;
# here any non-2xx is fatal.
def die_on_error(response)
  problem = begin
    JSON.parse(response.body.to_s)
  rescue JSON::ParserError
    {}
  end

  # request_id mirrors the x-request-id header; fall back to the header.
  request_id = problem["request_id"] || response.headers["x-request-id"] || "(none)"
  detail = problem["detail"] || (response.body.to_s.empty? ? "(no detail)" : response.body.to_s)
  title = problem["title"] || "Request failed"

  warn "Error #{response.status} #{title}: #{detail} [request_id=#{request_id}]"
  exit 1
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

  offset = 0
  total = 0

  loop do
    response = conn.get(ENTITY, { limit: PAGE_LIMIT, offset: offset })
    die_on_error(response) unless response.success?

    body = JSON.parse(response.body.to_s)
    results = body["results"] || []
    meta = body["meta"] || {}

    results.each do |record|
      # Contract: print id, name, status per row.
      puts "#{record['id']}\t#{record['name']}\t#{record['status']}"
      total += 1
    end

    # Last page when hasMore is false; otherwise advance by the page size.
    break unless meta["hasMore"]

    offset += PAGE_LIMIT
  end

  puts
  puts "Total #{ENTITY}: #{total}"
end

main if $PROGRAM_NAME == __FILE__
