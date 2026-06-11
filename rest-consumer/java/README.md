# rest-consumer (Java)

Lists a single PropSocket entity over the read-only REST API and prints
`id`, `name`, and `status` per row, followed by a total count. Walks every
page using offset-based pagination.

Defaults to the `properties` entity. To consume a different one (units,
residents, leases), change the `ENTITY` constant in
`src/main/java/io/propsocket/samples/RestConsumer.java`.

HTTP uses the JDK built-in `java.net.http.HttpClient`; JSON parsing uses Jackson.

## Prerequisites

- Java 17+
- Maven 3.8+
- `make`

## Run

```bash
cp .env.example .env   # then paste your ps_test_ key into .env
make run
```

`make run` compiles the project, loads `.env`, and runs the program via
`mvn exec:java`.

## Expected output

```
prp_01HX0G5T8N3KEWBYV2QMR4DCFA    Maple Court Apartments                    active
prp_01HX0G5T8N3KEWBYV2QMR4DCFB    Birch Lane Lofts                          active
...

Total properties: 188
```

On any non-2xx response the program prints the API error `detail` and
`request_id` (quote it to support) and exits non-zero.
