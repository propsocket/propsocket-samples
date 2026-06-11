# webhook-receiver (Python / Flask)

A minimal Flask endpoint that receives PropSocket webhooks, verifies their
signature, ACKs fast, and processes events asynchronously and idempotently.

> Webhooks are a **Scale-plan-and-above** feature. See
> [propsocket.io/pricing](https://propsocket.io/pricing).

## What it does

- `POST /webhooks/propsocket`
  1. **Captures the raw request bytes before parsing JSON** — the signature is
     computed over the exact bytes, so re-serializing parsed JSON would break it.
  2. Verifies the `X-PropSocket-Signature` header: lowercase hex
     **HMAC-SHA256** over the raw body, using `PROPSOCKET_WEBHOOK_SECRET`,
     compared in **constant time** (`hmac.compare_digest`).
  3. Bad signature -> `401`. Unparseable body -> `400`.
  4. Otherwise hands the event to a **background thread** and returns `202`
     immediately, so we ACK well within the 10-second budget.
- The worker **dedupes on the event id** (`evt_...`). Delivery is at-least-once,
  so duplicates are expected; they still get a 2xx but are not re-processed. The
  sample uses an in-memory set — in production make this durable and shared.
- `GET /health` -> `200`.

PropSocket retries failed deliveries on this schedule: immediate, 30s, 1m, 5m,
30m, 1h, 6h, then dead-letters (~7h36m total). A 7-day dedupe TTL comfortably
outlasts that window.

## Prerequisites

- Python 3.11+
- `make`

## Run

```bash
cp .env.example .env   # then set PROPSOCKET_WEBHOOK_SECRET in .env
make run
```

`make run` creates a `.venv`, installs [`Flask`](https://pypi.org/project/Flask/),
loads `.env`, and starts the server on `PORT` (default 4000).

## Try it locally

Sign a payload with your secret and POST it:

```bash
SECRET='your-secret'
BODY='{"id":"evt_01HX9P3K2N7QZRWY4B8MJ5VCDF","type":"LEASE_SIGNED","data":{"id":"lse_01HX9P3K2N7QZRWY4B8MJ5VCDF","status":"active"}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')

curl -sS -X POST http://localhost:4000/webhooks/propsocket \
  -H "Content-Type: application/json" \
  -H "X-PropSocket-Signature: $SIG" \
  -d "$BODY"
```

Expected: HTTP `202 {"status":"accepted"}` and a log line
`processing LEASE_SIGNED evt_...  -> new`. POST the same body again and you get
another `202`, but the log shows `duplicate ... -> skip (already processed)`.
Tamper with the body or signature and you get `401`.
