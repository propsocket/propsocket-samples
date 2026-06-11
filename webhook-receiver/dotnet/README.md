# webhook-receiver (.NET, ASP.NET Core Minimal API)

Receives PropSocket webhooks, verifies their signature, and processes events
idempotently. Listens on `POST /webhooks`.

> Webhooks are a **Scale-plan-and-above** feature. See
> <https://propsocket.io/pricing>.

## What it does

- **Verifies the signature** in the `X-PropSocket-Signature` header: a lowercase
  hex `HMAC-SHA256` over the **raw request body**, keyed by
  `PROPSOCKET_WEBHOOK_SECRET`. The raw bytes are read off the request stream
  **before** any JSON parsing, and compared in **constant time**
  (`CryptographicOperations.FixedTimeEquals`). Bad/missing signature → `401`.
- **Parses only after** the signature checks out. Unparseable body → `400`.
- **ACKs fast, processes async.** It returns `2xx` immediately (well under the
  10-second budget) and hands the event to a background worker via a `Channel`.
- **Is idempotent.** Delivery is at-least-once, so it dedupes on the event `id`
  (`evt_...`). Duplicates still return `2xx` but skip re-processing. The dedupe
  set is in-memory here (fine for a sample); in production make it durable with a
  TTL that outlasts the ~7h36m retry window (immediate, 30s, 1m, 5m, 30m, 1h, 6h
  → dead-letter).

The worker just logs each event's `type` + `id` and the dedupe decision.

## Prerequisites

- .NET 8 SDK (`dotnet --version` ≥ 8.0)
- `make`

ASP.NET Core ships in the shared framework — no NuGet packages.

## Run

```bash
cp .env.example .env   # then paste your webhook signing secret into .env
make run
```

Listens on `http://0.0.0.0:$PORT/webhooks` (`PORT` defaults to `4000`).
`make install` runs `dotnet restore`.

## Try it locally

Compute a signature over a raw body and POST it:

```bash
SECRET='whsec_xxxxxxxxxxxxxxxxxxxxxxxx'
BODY='{"id":"evt_01HX9P3K2N7QZRWY4B8MJ5VCDF","type":"LEASE_SIGNED","data":{"id":"lse_01HX9P3K2N7QZRWY4B8MJ5VCDF","status":"active"}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')

curl -i -X POST http://127.0.0.1:4000/webhooks \
  -H "Content-Type: application/json" \
  -H "X-PropSocket-Signature: $SIG" \
  --data-raw "$BODY"
```

Expected: first call returns `200 {"status":"accepted"}` and the server logs
`event LEASE_SIGNED evt_... — processed`. POST the same body again and it returns
`200 {"status":"duplicate"}` without re-processing. Tamper with the body or the
signature and it returns `401`.
