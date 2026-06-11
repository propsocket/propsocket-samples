# frozen_string_literal: true

# PropSocket webhook receiver — Sinatra.
#
# Verifies the X-PropSocket-Signature HMAC over the RAW request body, dedupes
# at-least-once deliveries on the event id, ACKs fast (2xx), then processes the
# event asynchronously.
#
# See the shared contract for the webhook details.

require "sinatra"
require "openssl"
require "json"
require "set"
require "rack/utils"

# Signing secret used to verify the HMAC. Set in .env.
WEBHOOK_SECRET = ENV["PROPSOCKET_WEBHOOK_SECRET"].to_s

set :bind, "0.0.0.0"
set :port, (ENV["PORT"] || 4000).to_i

# Dedupe store: delivery is at-least-once, so the handler MUST be idempotent.
# We key on the event id (prefixed `evt_`). An in-memory set is fine for the
# sample; in production this must be durable (and TTL'd — a 7-day TTL comfortably
# outlasts PropSocket's ~7h36m retry window). Guard with a mutex since we hand
# processing off to background threads.
SEEN_EVENTS = Set.new
SEEN_MUTEX = Mutex.new

# Constant-time HMAC-SHA256 verification over the exact raw bytes.
def valid_signature?(raw_body, signature)
  return false if signature.nil? || signature.empty? || WEBHOOK_SECRET.empty?

  # Lowercase hex digest, single value (NOT a t=...,v1=... format).
  expected = OpenSSL::HMAC.hexdigest("SHA256", WEBHOOK_SECRET, raw_body)

  # Constant-time compare to avoid leaking match progress via timing.
  Rack::Utils.secure_compare(expected, signature)
end

# Where real work would happen. Runs off the request thread so we can ACK fast.
def process_event(event)
  puts "[process] type=#{event['type']} id=#{event['id']}"
end

post "/webhooks/propsocket" do
  # Capture the RAW, unmodified bytes BEFORE any parsing. The signature is
  # computed over these exact bytes; parsing/re-serializing would change them.
  request.body.rewind
  raw_body = request.body.read

  signature = request.env["HTTP_X_PROPSOCKET_SIGNATURE"]

  # Reject a bad/missing signature with 401 before doing anything else.
  unless valid_signature?(raw_body, signature)
    halt 401, "invalid signature"
  end

  # Reject an unparseable body with 400.
  event = begin
    JSON.parse(raw_body)
  rescue JSON::ParserError
    halt 400, "invalid JSON"
  end

  event_id = event["id"]
  halt 400, "missing event id" if event_id.nil? || event_id.empty?

  # Dedupe: on a duplicate, still ACK 2xx (it's already processed) but skip work.
  duplicate = false
  SEEN_MUTEX.synchronize do
    if SEEN_EVENTS.include?(event_id)
      duplicate = true
    else
      SEEN_EVENTS.add(event_id)
    end
  end

  if duplicate
    puts "[dedupe] duplicate event id=#{event_id} — skipping"
    status 200
    return "duplicate ok"
  end

  puts "[recv] type=#{event['type']} id=#{event_id} — accepted"

  # ACK fast (return 2xx within 10s), then process asynchronously in a thread.
  Thread.new { process_event(event) }

  status 200
  "ok"
end

get "/healthz" do
  "ok"
end
