# rest-consumer (Node.js)

Lists a single PropSocket entity with offset-based pagination and prints
`id`, `name`, and `status` per row plus a final total count. Read-only (GET).

## What it does

- Pages through `GET /v1/properties` (default) using `limit`/`offset`.
- Stops when `meta.hasMore === false`.
- On any non-2xx, prints the HTTP status, the RFC 7807 `detail`, and the
  `request_id` (quote it to support), then exits non-zero.

Swap the entity by editing the `ENTITY` constant at the top of `index.js`
(`properties` | `units` | `residents` | `leases`). Every list endpoint shares
the same response envelope, so nothing else changes.

## Prerequisites

- Node.js 18+
- A PropSocket test API key (`ps_test_...`)

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_API_KEY
make run
```

`make run` installs deps (`npm install`), loads `.env`, and starts the program.

## Expected output

```
prp_01HX0G5T8N3KEWBYV2QMR4DCFA  Maple Court Apartments  [active]
prp_01HX0G7Q1M9JZ2RWB4N6KD8CFA  Birch Hill Lofts        [active]
...

Total properties: 188
```
