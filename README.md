# PropSocket samples

Three runnable PropSocket samples — **not an SDK**. Each is a complete, minimal app you can
clone and run against `api.propsocket.io` with a `ps_test_` key. Fork it, gut it, ship it.

Why samples instead of an SDK? The REST surface is small and predictable — a handful of
read-only entities, one auth header, one pagination envelope — so a correct starting point is
worth more than a hand-maintained client. The full reasoning lives at
**<https://propsocket.io/docs/sdks>**.

## The three samples

| Sample | What it does | Touches |
| --- | --- | --- |
| [`rest-consumer/`](rest-consumer/) | Authenticate, list an entity, page to the end via offset pagination, print each row. The minimal correct read loop. | `GET /properties` (swappable entity) |
| [`webhook-receiver/`](webhook-receiver/) | Capture the raw body, verify the `X-PropSocket-Signature` HMAC-SHA256, dedupe on the event id, ACK 2xx fast, process async. | Webhook events (Scale plan and above) |
| [`backfill-paginator/`](backfill-paginator/) | Page every entity by `order-by=created_at:asc`, honor `Retry-After` on 429, upsert idempotently into a local sink. | `properties`, `units`, `residents`, `leases` |

## Language availability

Every sample is implemented in all seven languages.

| | Python | Node | Go | Ruby | PHP | Java | .NET |
| --- | :-: | :-: | :-: | :-: | :-: | :-: | :-: |
| **rest-consumer** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **webhook-receiver** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **backfill-paginator** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

## Run any sample

Every leaf follows the same shape — pick a sample and a language, drop in your `ps_test_` key, run:

```bash
git clone https://github.com/propsocket/propsocket-samples
cd propsocket-samples/rest-consumer/python   # <sample>/<language>
cp .env.example .env                          # paste your ps_test_ key
make run
```

Each leaf ships a `.env.example` and a `make run` target (and a `make install` where a fetch step
is needed). Get a `ps_test_` key from the PropSocket dashboard — see the
[quickstart](https://propsocket.io/docs/quickstart). Reads return your real connected data and
writes are dry-run in test mode, so the samples never mutate your PMS.

## Prerequisites

You need `make` plus the toolchain for whichever language you run:

| Language | Needs |
| --- | --- |
| Python | Python 3.11+, `pip` |
| Node | Node 18+, `npm` |
| Go | Go 1.21+ (standard library only — no third-party deps) |
| Ruby | Ruby 3.1+, `bundler` |
| PHP | PHP 8.1+, `composer` (the webhook receiver is dependency-free) |
| Java | JDK 17+, Maven |
| .NET | .NET SDK 8 |

## These are reference code, not a supported SDK

The samples are deliberately small and unversioned — read them, copy them, change them. They are
not a published package and carry no compatibility guarantees. If your team would genuinely rather
`pip install` / `npm install` a client than copy a sample,
[tell us](https://propsocket.io/contact) — that signal is what we're watching to decide whether an
official SDK is worth the commitment.

## What next

- [Quickstart](https://propsocket.io/docs/quickstart) — the same paths as the REST consumer and
  webhook receiver, inline.
- [Recipes](https://propsocket.io/docs/recipes) — task-specific code.
- [API reference](https://propsocket.io/docs/api) — the full REST surface.

## License

[Apache-2.0](LICENSE).
