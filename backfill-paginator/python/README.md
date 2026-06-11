# backfill-paginator (Python)

Full read of every PropSocket entity into a local upsert sink. Iterates
`properties`, `units`, `residents`, `leases` (parents before children) at the
max page size (100), walking all pages with offset-based pagination and stable
`order-by=created_at:asc` ordering.

Each record is upserted into `backfill-store.json` keyed by `id`, so **re-running
is idempotent** — unchanged records are a no-op. This local JSON map is a
reference upsert sink, not a real database.

Handles `429 Too Many Requests` by sleeping exactly the server's `Retry-After`
seconds, then retrying (no invented backoff).

## Prerequisites

- Python 3.11+
- `make`

## Run

```bash
cp .env.example .env   # then paste your ps_test_ key into .env
make run
```

`make run` creates a `.venv`, installs [`requests`](https://pypi.org/project/requests/),
loads `.env`, and runs `main.py`.

## Expected output

```
Backfilling properties...
  properties: 188 records upserted
Backfilling units...
  units: 4021 records upserted
Backfilling residents...
  residents: 3550 records upserted
Backfilling leases...
  leases: 3550 records upserted

Store now holds 11309 total records -> backfill-store.json
```

Re-run and the counts stay the same while the store is unchanged. On any
non-2xx (other than the handled 429) the program prints the API error and exits
non-zero. `make clean` removes the venv and the local store.
