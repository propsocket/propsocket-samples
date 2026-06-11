# rest-consumer (Go)

Lists a single PropSocket entity, pages through every result with offset
pagination, and prints `id`, `name`, and `status` per row plus a total count.

Defaults to the `properties` entity. To consume a different one, change the
`entity` constant at the top of `main.go` to `"units"`, `"residents"`, or
`"leases"`.

## Prerequisites

- Go 1.21+
- A PropSocket API key (a `ps_test_` key is fine)

Stdlib only — no third-party dependencies.

## Run

```bash
cp .env.example .env   # then edit .env and set PROPSOCKET_API_KEY
make run
```

`make run` loads `.env` and starts the program. `make install` (or
`make build`) just compiles, since there's nothing to fetch.

## Expected output

One line per record, then a total:

```
prp_01HX0G5T8N3KEWBYV2QMR4DCFA  Maple Court Apartments                    active
prp_01HX0G5T8N3KEWBYV2QMR4DCFB  Birch Hollow Townhomes                    active
...

Total properties: 188
```

On an API error the program prints the HTTP status, the `detail` from the
problem+json body, and the `request_id` (quote it to support), then exits
non-zero.
