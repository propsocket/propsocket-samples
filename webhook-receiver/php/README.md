# PropSocket webhook receiver — PHP

A dependency-free front controller that receives PropSocket webhook deliveries,
verifies the signature, dedupes, and acks fast. Served by PHP's built-in
server — no framework.

> Webhooks are a **Scale-plan-and-above** feature. See
> [propsocket.io/pricing](https://propsocket.io/pricing).

## What it does

1. **Raw-body capture before parsing** — reads `php://input` and verifies the
   signature against those exact bytes (re-encoding parsed JSON would change
   them and break verification).
2. **Constant-time signature check** — `X-PropSocket-Signature` is a lowercase
   hex HMAC-SHA256 of the raw body keyed by `PROPSOCKET_WEBHOOK_SECRET`,
   compared with `hash_equals` to avoid timing leaks. Bad signature → `401`.
3. **Idempotent dedupe** — delivery is at-least-once, so events are deduped on
   the `evt_` id. Duplicates still return `2xx` but skip re-processing.
   Unparseable body → `400`.
4. **Ack fast** — returns `200` within 10s. PHP is request/response, so a real
   deployment would **enqueue a job** (SQS/Redis/DB queue) and return
   immediately rather than process inline. This sample just logs `type` + `id`
   and the dedupe decision.

The dedupe set is in-process memory (fine for the single-process built-in
server) and resets on restart. In production use a durable, shared store with
a ~7-day TTL — comfortably longer than PropSocket's retry window (immediate,
30s, 1m, 5m, 30m, 1h, 6h → dead-letter; ~7h36m total).

## Prerequisites

- PHP 8.1+ (no Composer dependencies)

## Run

```bash
cp .env.example .env   # then paste your webhook signing secret
make run                # serves on http://127.0.0.1:4000 (override with PORT)
```

`make run` loads `.env` and starts `php -S 127.0.0.1:$PORT index.php`.

### Try it locally

With the server running, sign a body with the same secret and POST it:

```bash
SECRET="your-secret-from-.env"
BODY='{"id":"evt_01HX9P3K2N7QZRWY4B8MJ5VCDF","type":"LEASE_SIGNED","data":{"id":"lse_01","status":"active"}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')
curl -i -X POST http://127.0.0.1:4000/ \
  -H "X-PropSocket-Signature: $SIG" \
  -H "Content-Type: application/json" \
  --data-raw "$BODY"
```

Expected: `200 {"status":"accepted"}` on first send, `200 {"status":"duplicate"}`
on a repeat, and `401 {"error":"invalid signature"}` if you tamper with the
body or signature. The server logs `[recv]` / `[dup]` lines per delivery.
