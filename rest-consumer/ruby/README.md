# PropSocket REST consumer — Ruby

Lists a single PropSocket entity using offset-based pagination and prints
`id`, `name`, and `status` per row plus a final total count. Read-only (GET).

Defaults to the `properties` entity. Swap the `ENTITY` constant in `app.rb`
to consume `units`, `residents`, or `leases`.

## Prerequisites

- Ruby 3.1+
- Bundler (`gem install bundler`)

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_API_KEY
make run
```

`make run` installs gems (`bundle install`), loads `.env`, and starts the app.

## Expected output

One tab-separated row per record, then a total:

```
prp_01HX0G5T8N3KEWBYV2QMR4DCFA	Maple Court Apartments	active
prp_01HX0G5T8N3KEWBYV2QMR4DCFB	Birch Hill Residences	active

Total properties: 2
```

On a non-2xx response the program prints the status, title, `detail`, and
`request_id` (quote it to support) and exits non-zero.
