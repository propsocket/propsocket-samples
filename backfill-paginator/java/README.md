# backfill-paginator (Java)

Pages through **every** PropSocket entity and upserts each record into a local
JSON sink, keyed by `id`. Iterates the four top-level entities in dependency
order — **properties, units, residents, leases** (parents before children) —
using the max page size (`limit=100`) and a stable sort (`order-by=created_at:asc`)
so paging stays deterministic as new rows arrive.

HTTP uses the JDK built-in `java.net.http.HttpClient`; JSON uses Jackson.

## Idempotent by id

The sink (`backfill-store.json`) is a `{ entity: { id: record } }` map. Because
every record is keyed by its prefixed-ULID `id`, **re-running is a no-op** — an
overlapping or redelivered record just overwrites itself. This is a reference
upsert sink, not a real database; swap in your store of choice.

## Rate limits

This is the sample most likely to hit the 150 req/min cap. On a `429` it reads
the `Retry-After` header, sleeps that exact number of seconds, and retries the
same page. It honors the server's wait — it does **not** invent its own backoff.

## Prerequisites

- Java 17+
- Maven 3.8+
- `make`

## Run

```bash
cp .env.example .env   # then paste your ps_test_ key into .env
make run
```

`make run` compiles the project, loads `.env`, and runs the backfill via
`mvn exec:java`.

## Expected output

```
properties   upserted 188 records
units        upserted 188 records
residents    upserted 642 records
leases       upserted 511 records

Wrote backfill-store.json (total records: 4)
```

(The final number is the count of entity buckets in the store object.) Run it
again and the same files are produced with no duplicated rows. On any non-2xx
other than `429`, it prints the API `detail` + `request_id`, saves partial
progress, and exits non-zero.
