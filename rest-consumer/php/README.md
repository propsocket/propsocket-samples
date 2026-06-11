# PropSocket REST consumer — PHP

Lists one PropSocket entity over the read-only REST API using offset-based
pagination, printing `id`, `name`, and `status` per row plus a final total.

Defaults to the `properties` entity. To consume a different one, change the
`ENTITY` constant in `main.php` (`units`, `residents`, or `leases`).

## Prerequisites

- PHP 8.1+
- [Composer](https://getcomposer.org/)

## Run

```bash
cp .env.example .env   # then paste your ps_test_ key
make run
```

`make run` fetches deps (`composer install`), loads `.env`, and runs the
program. Set `PROPSOCKET_API_KEY` in `.env`; `PROPSOCKET_BASE_URL` is optional.

## Expected output

A tab-separated row per record, then a total:

```
prp_01HX0G5T8N3KEWBYV2QMR4DCFA	Maple Court Apartments	active
...
Total properties: 188
```

On a non-2xx response the program prints the HTTP status, the problem+json
`title`/`detail`, and the `request_id` (quote it to support), then exits
non-zero. Rate-limit (429) retry handling lives in the backfill-paginator
sample, not here.
