# webhook-receiver (Go)

A minimal HTTP server that receives PropSocket webhooks, verifies the
signature, dedupes, and acks fast. It logs each event's `type` and `id` plus
the dedupe decision; it has no per-type logic.

> Webhooks are a **Scale-plan-and-above** feature. See
> [propsocket.io/pricing](https://propsocket.io/pricing).

## What it does

1. **Captures the raw request body before parsing.** The signature is computed
   over the exact bytes sent; re-serializing parsed JSON would change them.
2. **Verifies `X-PropSocket-Signature`** — a lowercase-hex HMAC-SHA256 of the
   raw body using `PROPSOCKET_WEBHOOK_SECRET` — using a constant-time compare
   (`hmac.Equal`). Bad signature → `401`. Unparseable body → `400`.
3. **Dedupes on the event `id` (`evt_...`).** Delivery is at-least-once, so the
   handler is idempotent. Duplicates still return `2xx` but skip re-processing.
   The dedupe set is in-memory with a 7-day TTL (outlasting the ~7h36m retry
   window) — **make it durable in production.**
4. **Acks fast:** returns `2xx` within 10 seconds, then processes the event in a
   goroutine. PropSocket retries on a slow/failed response.

PropSocket's retry schedule: immediate, 30s, 1m, 5m, 30m, 1h, 6h → dead-letter
(~7h36m total).

## Prerequisites

- Go 1.21+
- Your webhook signing secret

Stdlib only — no third-party dependencies.

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_WEBHOOK_SECRET
make run
```

The server listens on `POST /webhooks/propsocket` at `PORT` (default 4000).

## Expected output

```
listening on :4000 (POST /webhooks/propsocket)
processing: type=LEASE_SIGNED id=evt_01HX9P3K2N7QZRWY4B8MJ5VCDF
duplicate: type=LEASE_SIGNED id=evt_01HX9P3K2N7QZRWY4B8MJ5VCDF (skipped)
rejected: bad signature
```
