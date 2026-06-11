# PropSocket webhook receiver — Ruby (Sinatra)

Receives PropSocket webhooks, verifies the signature, dedupes, ACKs fast, and
processes asynchronously.

> Webhooks are a **Scale-plan-and-above** feature. See
> [propsocket.io/pricing](https://propsocket.io/pricing).

## What it does

- Listens on `POST /webhooks/propsocket` (and `GET /healthz`).
- Captures the **raw request body before parsing** and verifies the
  `X-PropSocket-Signature` header — a lowercase hex HMAC-SHA256 over those exact
  bytes, using `PROPSOCKET_WEBHOOK_SECRET`. Comparison is **constant-time**
  (`Rack::Utils.secure_compare`).
- Rejects a bad/missing signature with **401**, an unparseable body with **400**.
- **Dedupes** on the event `id` (delivery is at-least-once). A duplicate still
  returns 2xx but skips re-processing. The dedupe set is in-memory here; make it
  durable (and TTL'd) in production.
- **ACKs within 10s** (returns 2xx), then runs the work in a background `Thread`.

PropSocket retries on non-2xx: immediate, 30s, 1m, 5m, 30m, 1h, 6h, then
dead-letters (~7h36m total) — which is why the handler must be idempotent.

## Prerequisites

- Ruby 3.1+
- Bundler (`gem install bundler`)

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_WEBHOOK_SECRET
make run
```

`make run` installs gems, loads `.env`, and starts Sinatra on `PORT` (default 4000).

## Try it locally

Compute a signature over the raw body and POST it:

```bash
SECRET=$(grep PROPSOCKET_WEBHOOK_SECRET .env | cut -d= -f2)
BODY='{"id":"evt_01HX9P3K2N7QZRWY4B8MJ5VCDF","type":"LEASE_SIGNED","data":{"id":"lse_01","status":"active"}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')
curl -i -X POST http://localhost:4000/webhooks/propsocket \
  -H "Content-Type: application/json" \
  -H "X-PropSocket-Signature: $SIG" \
  -d "$BODY"
```

First call logs `[recv] ... accepted` and returns `200 ok`; a repeat logs
`[dedupe] ... skipping` and returns `200 duplicate ok`. A wrong signature
returns `401`.
