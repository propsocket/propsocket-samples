# backfill-paginator (Go)

Pages through all four PropSocket entities — `properties`, `units`,
`residents`, `leases` (parents before children) — and "upserts" each record
into a local key/value sink keyed by `id`.

The sink (`backfill-store.json`) is a reference upsert target, **not a real
DB**. Because every record is keyed by `id`, re-running is idempotent: a clean
re-run reports `upserted=0`.

## How it works

- Uses `limit=100` (the API max) for an efficient backfill.
- Sorts with `order-by=created_at:asc` for stable pagination — new rows append
  at the end and don't shift pages already read.
- Advances offset-based pagination until `meta.hasMore` is `false`.
- **Handles `429 Too Many Requests`** by sleeping exactly the `Retry-After`
  seconds the server specifies, then retrying the same page. It honors the
  server's wait rather than inventing a backoff.
- On any other non-2xx, prints status + `detail` + `request_id` and exits
  non-zero.

## Prerequisites

- Go 1.21+
- A PropSocket API key (a `ps_test_` key is fine)

Stdlib only — no third-party dependencies.

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_API_KEY
make run
```

`make clean` removes `backfill-store.json` to force a full re-backfill.

## Expected output

```
properties  seen=188 upserted=188 (skipped=0)
units       seen=188 upserted=188 (skipped=0)
residents   seen=402 upserted=402 (skipped=0)
leases      seen=210 upserted=210 (skipped=0)

Store: backfill-store.json (988 records total)
```

Run it again and every entity reports `upserted=0` — proof the upsert is
idempotent.
