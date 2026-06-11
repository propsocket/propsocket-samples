# rest-consumer (.NET)

Lists a single PropSocket entity over the read-only REST API and prints `id`,
`name`, and `status` per row, followed by a total count. Walks every page using
offset-based pagination.

Defaults to the `properties` entity. To consume a different one (units,
residents, leases), change the `Entity` constant at the top of `Program.cs`.

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
prp_01HX0G5T8N3KEWBYV2QMR4DCFA    Maple Court Apartments                    active
prp_01HX0G5T8N3KEWBYV2QMR4DCFB    Birch Lane Lofts                          active
...

Total properties: 188
```

On any non-2xx response the program prints the API error `title`, `detail`, and
`request_id` (quote it to support) and exits non-zero.
