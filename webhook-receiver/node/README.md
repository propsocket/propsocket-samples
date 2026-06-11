# webhook-receiver (Node.js)

An Express server that receives PropSocket webhooks, verifies the HMAC
signature, dedupes at-least-once deliveries, and ACKs fast before processing.

> Webhooks are a **Scale-plan-and-above** feature. See
> [propsocket.io/pricing](https://propsocket.io/pricing).

## What it does

- Captures the **raw request bytes before any JSON parsing** (`express.raw`),
  because the signature is computed over those exact bytes.
- Verifies `X-PropSocket-Signature` — a single lowercase hex HMAC-SHA256 over
  the raw body — using a **constant-time** comparison (`crypto.timingSafeEqual`).
- Rejects bad signatures with `401` and unparseable bodies with `400`.
- **Dedupes on the event `id` (`evt_...`)** since delivery is at-least-once.
  Duplicates still return `2xx` but skip re-processing. The in-memory set is
  fine for the sample; use a durable store (Redis/DB) with a TTL in production.
- **ACKs within 10s** then processes asynchronously via `setImmediate`.

PropSocket retries on failure: immediate, 30s, 1m, 5m, 30m, 1h, 6h, then
dead-letters (~7h36m total). A 7-day dedupe TTL comfortably outlasts that.

## Prerequisites

- Node.js 18+
- Your webhook signing secret (`PROPSOCKET_WEBHOOK_SECRET`)

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_WEBHOOK_SECRET
make run
```

`make run` installs deps, loads `.env`, and starts the server on `PORT`
(default `4000`). Events are accepted at `POST /webhooks/propsocket`.

## Expected output

```
Webhook receiver listening on http://127.0.0.1:4000
POST PropSocket events to /webhooks/propsocket
type=LEASE_SIGNED id=evt_01HX9P3K2N7QZRWY4B8MJ5VCDF dedupe=new (processing)
type=LEASE_SIGNED id=evt_01HX9P3K2N7QZRWY4B8MJ5VCDF dedupe=duplicate (skipped)
```

## Local testing

Expose the port with a tunnel (e.g. ngrok) and point a PropSocket webhook
endpoint at `https://<tunnel>/webhooks/propsocket`. A valid signature requires
the secret, so end-to-end tests should send real PropSocket deliveries.
