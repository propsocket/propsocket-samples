# PropSocket backfill paginator — PHP

Walks every top-level PropSocket entity (`properties`, `units`, `residents`,
`leases` — parents before children) over the read-only REST API and upserts
each record into a local JSON store keyed by `id`.

- Uses the max page size (`limit=100`) with stable `order-by=created_at:asc`
  pagination so a full walk stays consistent.
- The upsert sink is `backfill-store.json` — a reference key/value store, **not**
  a real database. Because records are keyed by `id`, re-running is idempotent
  (a no-op).
- Handles `429 Too Many Requests` by sleeping exactly the `Retry-After` seconds
  the server returns, then retrying — no invented backoff.

## Prerequisites

- PHP 8.1+
- [Composer](https://getcomposer.org/)

## Run

```bash
cp .env.example .env   # then paste your ps_test_ key
make run
```

`make run` fetches deps (`composer install`), loads `.env`, and runs the
program.

## Expected output

Per-entity counts, then the store summary:

```
properties   seen=188 upserted=188 (new this run)
units        seen=188 upserted=188 (new this run)
residents    seen=540 upserted=540 (new this run)
leases       seen=305 upserted=305 (new this run)

Store: 1221 records -> .../backfill-store.json
```

Re-run and `upserted` drops to `0` for already-stored ids (idempotent). Any
non-2xx other than 429 prints `title`/`detail` + `request_id` and exits
non-zero. Run `make clean` to drop the local store and vendored deps.
