# webhook-receiver

The webhook receiver scaffolding from the [webhooks guide](https://propsocket.io/docs/webhooks),
wired up end to end: capture the raw body, verify the signature, dedupe, acknowledge fast, process
async.

> **Webhooks are a Scale-plan-and-above feature.** See [pricing](https://propsocket.io/pricing).

What it shows — the four things a correct receiver must do:

1. **Capture the raw body *before* parsing.** The signature covers the exact bytes PropSocket
   sent; re-encoding parsed JSON would change them and break verification.
2. **Verify `X-PropSocket-Signature`** — a lowercase-hex HMAC-SHA256 of the raw body keyed by your
   signing secret — using a **constant-time comparison**. Bad signature → `401`, unparseable body
   → `400`.
3. **Dedupe on the event `id`** (prefixed `evt_`). Delivery is **at-least-once**, so the handler
   must be idempotent; a duplicate still returns `2xx` but skips re-processing. The samples use an
   in-memory set — in production use a durable store with a TTL that outlasts the retry window.
4. **ACK fast, process async.** Return a `2xx` within 10 seconds, then do the real work off the
   request path.

PropSocket's retry schedule on a failed delivery: immediate, 30s, 1m, 5m, 30m, 1h, 6h →
dead-letter (~7h36m total). A 7-day dedupe TTL comfortably outlasts it.

## Pick a language

[`python/`](python/) · [`node/`](node/) · [`go/`](go/) · [`ruby/`](ruby/) · [`php/`](php/) ·
[`java/`](java/) · [`dotnet/`](dotnet/)

```bash
cd <language>
cp .env.example .env   # paste your webhook signing secret
make run
```

Each per-language README includes a copy-paste `openssl` command to sign a test payload locally so
you can exercise the endpoint without a live subscription.
