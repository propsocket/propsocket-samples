# rest-consumer

The minimal correct read loop against the PropSocket REST API: authenticate, list an entity, page
to the end via offset pagination, and print each row. Copy it as the basis for any pull-based
integration.

What it shows:

- **Auth** — `Authorization: Bearer <ps_test_…>` on every request.
- **Offset pagination** — request `?limit=&offset=`, read the `{ meta, results }` envelope, stop
  when `meta.hasMore` is `false`, advance `offset` by the page size otherwise.
- **Swappable entity** — defaults to `properties`; flip one constant to consume `units`,
  `residents`, or `leases`.
- **Honest errors** — on a non-2xx, surface the RFC 7807 `detail` and the `request_id` (so you can
  quote it to support) and exit non-zero.

It stays deliberately simple — it does *not* handle rate limits; see
[`../backfill-paginator/`](../backfill-paginator/) for `Retry-After` handling.

## Pick a language

[`python/`](python/) · [`node/`](node/) · [`go/`](go/) · [`ruby/`](ruby/) · [`php/`](php/) ·
[`java/`](java/) · [`dotnet/`](dotnet/)

```bash
cd <language>
cp .env.example .env   # paste your ps_test_ key
make run
```

Expected output is one `id  name  status` row per record, then a total count. See the per-language
README for prerequisites.
