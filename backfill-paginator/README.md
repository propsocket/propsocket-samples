# backfill-paginator

The [backfill recipe](https://propsocket.io/docs/recipes/backfill-after-downtime) as a runnable
app: page every entity from the start, honor rate limits, and upsert idempotently so re-running is
a no-op.

What it shows:

- **Stable ordering** — page each entity by `order-by=created_at:asc` at the max page size
  (`limit=100`), so the offset window stays consistent while you walk the whole dataset.
- **Parents before children** — `properties → units → residents → leases`, so referenced records
  exist first.
- **Honor `Retry-After`** — a full backfill is the most likely thing to hit the 150 req/min
  per-Organization limit. On a `429`, sleep exactly the server-supplied `Retry-After` seconds and
  retry the same page. Don't invent a backoff.
- **Idempotent upserts** — each record is written to a local key/value sink keyed by its stable
  `id`. The second run overwrites with identical data — no duplicates. (The sink is a local JSON
  file for the sample, *not* a real database — swap in your warehouse/DB upsert.)

## Pick a language

[`python/`](python/) · [`node/`](node/) · [`go/`](go/) · [`ruby/`](ruby/) · [`php/`](php/) ·
[`java/`](java/) · [`dotnet/`](dotnet/)

```bash
cd <language>
cp .env.example .env   # paste your ps_test_ key
make run
```

Expected output is a per-entity `seen` / `upserted` count and a total. Run it twice: the second run
reports `upserted=0` because the upsert keys on `id`.
