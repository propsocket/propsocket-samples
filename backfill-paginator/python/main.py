"""PropSocket backfill paginator — full read of all entities into a local sink.

Iterates the four top-level entities (properties, units, residents, leases —
parents before children) at the max page size, upserting each record into a
local JSON key/value store keyed by `id`. Because we key on `id`, re-running is
idempotent (a no-op on unchanged data) — this is a reference upsert sink, not a
real database.

Stable pagination uses `order-by=created_at:asc` so records don't shift between
pages. Handles 429 Too Many Requests by honoring the server's Retry-After header.
"""

import json
import os
import sys
import time

import requests

DEFAULT_BASE_URL = "https://api.propsocket.io/v1"

# Parents before children: properties -> units -> residents -> leases.
ENTITIES = ["properties", "units", "residents", "leases"]

# Backfill uses the max allowed page size to minimize round trips.
PAGE_LIMIT = 100

# Local upsert sink (gitignored). Keyed by record id, so re-runs are idempotent.
STORE_PATH = "backfill-store.json"


def load_store() -> dict:
    if os.path.exists(STORE_PATH):
        with open(STORE_PATH, "r", encoding="utf-8") as fh:
            return json.load(fh)
    return {}


def save_store(store: dict) -> None:
    with open(STORE_PATH, "w", encoding="utf-8") as fh:
        json.dump(store, fh, indent=2)


def die_on_error(resp: requests.Response) -> None:
    """Print an RFC 7807 problem+json error and exit non-zero.

    429 is handled inline (Retry-After); every other non-2xx is fatal.
    """
    try:
        problem = resp.json()
    except ValueError:
        problem = {}

    request_id = problem.get("request_id") or resp.headers.get("x-request-id", "(none)")
    detail = problem.get("detail") or resp.text or "(no detail)"
    title = problem.get("title") or "Request failed"

    print(
        f"Error {resp.status_code} {title}: {detail} [request_id={request_id}]",
        file=sys.stderr,
    )
    sys.exit(1)


def get_page(session: requests.Session, url: str, offset: int) -> requests.Response:
    """GET one page, retrying on 429 by sleeping exactly Retry-After seconds.

    We honor the server's wait rather than inventing a backoff. The rate limit
    is 150 req/min per org; only the backfill is likely to hit it.
    """
    while True:
        resp = session.get(
            url,
            params={
                "limit": PAGE_LIMIT,
                "offset": offset,
                "order-by": "created_at:asc",  # stable order for pagination
            },
            timeout=30,
        )
        if resp.status_code == 429:
            retry_after = int(resp.headers.get("Retry-After", "1"))
            print(
                f"  rate limited (429); sleeping {retry_after}s then retrying...",
                file=sys.stderr,
            )
            time.sleep(retry_after)
            continue
        return resp


def backfill_entity(session: requests.Session, base_url: str, entity: str, store: dict) -> int:
    url = f"{base_url}/{entity}"
    offset = 0
    count = 0

    while True:
        resp = get_page(session, url, offset)
        if not resp.ok:
            die_on_error(resp)

        body = resp.json()
        results = body.get("results", [])
        meta = body.get("meta", {})

        for record in results:
            # Upsert: keying by id makes re-runs a no-op for unchanged records.
            store[record["id"]] = record
            count += 1

        if not meta.get("hasMore"):
            break
        offset += PAGE_LIMIT

    return count


def main() -> None:
    api_key = os.environ.get("PROPSOCKET_API_KEY")
    if not api_key:
        print("PROPSOCKET_API_KEY is not set. Copy .env.example to .env.", file=sys.stderr)
        sys.exit(1)

    base_url = os.environ.get("PROPSOCKET_BASE_URL", DEFAULT_BASE_URL).rstrip("/")

    session = requests.Session()
    session.headers.update({"Authorization": f"Bearer {api_key}"})

    store = load_store()

    for entity in ENTITIES:
        print(f"Backfilling {entity}...")
        count = backfill_entity(session, base_url, entity, store)
        print(f"  {entity}: {count} records upserted")

    save_store(store)
    print(f"\nStore now holds {len(store)} total records -> {STORE_PATH}")


if __name__ == "__main__":
    main()
