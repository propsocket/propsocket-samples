# backfill-paginator (Node.js)

Paginates every PropSocket entity into a local upsert sink keyed by record id.
Re-running is idempotent (a no-op for unchanged data). Read-only (GET).

## What it does

- Iterates all four entities **parents before children**:
  `properties -> units -> residents -> leases`.
- Pages each with `limit=100` and `order-by=created_at:asc` for stable
  pagination (new rows append; records don't shift across pages mid-run).
- **Upserts into `backfill-store.json`** keyed by `id` — a reference sink, not a
  real database. Keying on id makes re-runs idempotent.
- **Handles `429`** by sleeping exactly the seconds in the `Retry-After` header,
  then retrying the same page. It honors the server's wait — no invented backoff.
- On any other non-2xx, prints status + `detail` + `request_id` and exits
  non-zero.

## Prerequisites

- Node.js 18+
- A PropSocket test API key (`ps_test_...`)

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_API_KEY
make run
```

`make run` installs deps, loads `.env`, and runs the backfill. Progress is saved
after each entity, so a crash mid-run keeps prior progress. Run it again any time
to catch up — already-seen ids are simply overwritten in place.

## Expected output

```
Backfilling into .../backfill-store.json

Entity: properties
  properties: 188 fetched, 188 total in sink

Entity: units
  units: 3104 fetched, 3104 total in sink

Entity: residents
  residents: 2871 fetched, 2871 total in sink

Entity: leases
  leases: 2640 fetched, 2640 total in sink

Done. 8803 records across 4 entities.
```

`backfill-store.json` is gitignored (it's a local sink, not source).
