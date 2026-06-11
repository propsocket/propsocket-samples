# backfill-paginator (.NET)

Full backfill of every PropSocket entity. Pages through `properties`, `units`,
`residents`, and `leases` (parents before children) using offset-based
pagination at the max page size (`limit=100`) with a stable sort
(`order-by=created_at:asc`), and "upserts" each record into a local JSON
key/value store keyed by `id`.

The store (`backfill-store.json`) is a reference upsert sink, **not** a real
database — in production you'd upsert into your warehouse/DB. Because records are
keyed on `id`, **re-running is idempotent**: already-seen records are no-ops.

This sample honors rate limits. On HTTP `429 Too Many Requests` it sleeps exactly
the server-provided `Retry-After` seconds, then retries the same page — it does
not invent its own backoff.

## Prerequisites

- .NET 8 SDK (`dotnet --version` ≥ 8.0)
- `make`

Uses only the base class library (`System.Net.Http.HttpClient` +
`System.Text.Json`) — no NuGet packages.

## Run

```bash
cp .env.example .env   # then paste your ps_test_ key into .env
make run
```

`make run` loads `.env` and runs the console app (`dotnet run`). `make install`
runs `dotnet restore`.

## Expected output

```
properties: 188 fetched, 188 new (store now 188 total)
units: 1042 fetched, 1042 new (store now 1230 total)
residents: 1980 fetched, 1980 new (store now 3210 total)
leases: 1455 fetched, 1455 new (store now 4665 total)

Backfill complete. 4665 records in backfill-store.json.
```

Run it again and the "new" counts drop to `0` — the store already has every id.

On any non-2xx response (other than the 429 it retries) the program prints the
API error `title`, `detail`, and `request_id` (quote it to support), saves
progress made so far, and exits non-zero.
