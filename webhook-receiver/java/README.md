# webhook-receiver (Java / Spark)

A minimal HTTP endpoint that receives PropSocket webhooks, verifies their
signature, acknowledges fast, and processes asynchronously.

What it does, in order:

1. **Captures the raw request body bytes** (`request.bodyAsBytes()`) *before*
   any JSON parsing. The signature is computed over the exact bytes on the wire,
   so re-serializing parsed JSON would break verification.
2. **Verifies the signature** in the `X-PropSocket-Signature` header — a
   lowercase hex HMAC-SHA256 of the raw body keyed by `PROPSOCKET_WEBHOOK_SECRET`
   — using a **constant-time** comparison (`MessageDigest.isEqual`). Bad
   signature → `401`. Unparseable body → `400`.
3. **Acks within the 10s budget**, then processes on a background
   `ExecutorService`.
4. **Deduplicates on the event id** (`evt_...`). Delivery is at-least-once, so a
   duplicate is acked `2xx` but not re-processed. The dedupe set is in-memory
   here; in production make it durable with a TTL that outlasts the ~7h36m retry
   window (immediate, 30s, 1m, 5m, 30m, 1h, 6h → dead-letter).

> Webhooks are a **Scale-plan-and-above** feature. See
> [propsocket.io/pricing](https://propsocket.io/pricing).

## Prerequisites

- Java 17+
- Maven 3.8+
- `make`

## Run

```bash
cp .env.example .env   # then paste your webhook signing secret into .env
make run
```

The server listens on `:4000` (override with `PORT`) at
`POST /webhooks/propsocket`.

## Expected output

On startup:

```
webhook-receiver listening on :4000  POST /webhooks/propsocket
```

Per valid delivery:

```
processed  type=LEASE_SIGNED id=evt_01HX9P3K2N7QZRWY4B8MJ5VCDF
```

A redelivery of the same event id logs `duplicate ... (skipped)` and still
returns `200`. A bad signature returns `401`; a malformed body returns `400`.
