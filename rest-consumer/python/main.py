"""PropSocket REST consumer — list one entity and print id/name/status per row.

Reads the PropSocket REST API (read-only, GET) using offset-based pagination
and prints a row per record plus a final total count. Defaults to the
`properties` entity; change ENTITY below to swap (units, residents, leases).

See the shared contract for the full API surface.
"""

import os
import sys

import requests

# Default base URL; override with PROPSOCKET_BASE_URL.
DEFAULT_BASE_URL = "https://api.propsocket.io/v1"

# Swap this single constant to consume a different entity. The API exposes
# four top-level list endpoints: properties, units, residents, leases.
ENTITY = "properties"

# rest-consumer stays simple: a modest page size is fine (max allowed is 100).
PAGE_LIMIT = 25


def die_on_error(resp: requests.Response) -> None:
    """Print an RFC 7807 problem+json error and exit non-zero.

    We surface request_id so a user can quote it to support. The backfill
    sample handles 429/Retry-After; here any non-2xx is fatal.
    """
    try:
        problem = resp.json()
    except ValueError:
        problem = {}

    # request_id mirrors the x-request-id header; fall back to the header.
    request_id = problem.get("request_id") or resp.headers.get("x-request-id", "(none)")
    detail = problem.get("detail") or resp.text or "(no detail)"
    title = problem.get("title") or "Request failed"

    print(
        f"Error {resp.status_code} {title}: {detail} [request_id={request_id}]",
        file=sys.stderr,
    )
    sys.exit(1)


def main() -> None:
    api_key = os.environ.get("PROPSOCKET_API_KEY")
    if not api_key:
        print("PROPSOCKET_API_KEY is not set. Copy .env.example to .env.", file=sys.stderr)
        sys.exit(1)

    base_url = os.environ.get("PROPSOCKET_BASE_URL", DEFAULT_BASE_URL).rstrip("/")
    url = f"{base_url}/{ENTITY}"

    session = requests.Session()
    session.headers.update({"Authorization": f"Bearer {api_key}"})

    offset = 0
    total = 0

    while True:
        resp = session.get(
            url,
            params={"limit": PAGE_LIMIT, "offset": offset},
            timeout=30,
        )
        if not resp.ok:
            die_on_error(resp)

        body = resp.json()
        results = body.get("results", [])
        meta = body.get("meta", {})

        for record in results:
            # Contract: print id, name, status per row.
            print(
                f"{record.get('id')}\t{record.get('name')}\t{record.get('status')}"
            )
            total += 1

        # Last page when hasMore is false; otherwise advance by the page size.
        if not meta.get("hasMore"):
            break
        offset += PAGE_LIMIT

    print(f"\nTotal {ENTITY}: {total}")


if __name__ == "__main__":
    main()
