# PropSocket backfill paginator — Ruby

Walks every page of all four top-level entities — `properties`, `units`,
`residents`, `leases` (parents before children) — and upserts each record into a
local JSON store keyed by `id`. Read-only against the API (GET).

## What it does

- Paginates with `limit=100` and a stable sort (`order-by=created_at:asc`) so the
  offset walk doesn't skip or duplicate rows.
- "Upserts" each record into `backfill-store.json`, keyed by `id`. This is a
  reference sink (a JSON file), **not a real database**.
- Re-running is a **no-op (idempotent)** because records are keyed by `id`.
- Handles **429 Too Many Requests**: sleeps exactly the server's `Retry-After`
  seconds, then retries. It honors the server's wait rather than inventing a
  backoff.

## Prerequisites

- Ruby 3.1+
- Bundler (`gem install bundler`)

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_API_KEY
make run
```

`make run` installs gems, loads `.env`, and runs the backfill.

## Expected output

```
properties: upserted 188 record(s)
units: upserted 1042 record(s)
residents: upserted 1990 record(s)
leases: upserted 1755 record(s)

Store now holds 4975 total record(s) at .../backfill-store.json
```

On a second run the same records overwrite their keys, so the store size is
unchanged. On a non-2xx (other than 429) the program prints status, title,
`detail`, and `request_id`, then exits non-zero.
