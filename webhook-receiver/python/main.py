"""PropSocket webhook receiver (Flask).

Verifies the HMAC-SHA256 signature over the raw request body, ACKs fast
(2xx within 10s), and processes the event asynchronously on a background
thread. Delivery is at-least-once, so processing is idempotent: we dedupe on
the event id (`evt_...`).

Security-critical details, by design:
  * Capture the RAW request bytes BEFORE parsing JSON. Re-serializing parsed
    JSON would change the bytes and break the signature.
  * Use a CONSTANT-TIME comparison (hmac.compare_digest) for the signature to
    avoid leaking timing information.
"""

import hashlib
import hmac
import json
import logging
import os
import threading

from flask import Flask, request

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(message)s")
log = logging.getLogger("webhook")

app = Flask(__name__)

SIGNATURE_HEADER = "X-PropSocket-Signature"

# In-memory dedupe set keyed by event id. The contract notes a 7-day TTL
# comfortably outlasts the ~7h36m retry window. For the sample an in-memory set
# is fine; in production this MUST be durable (Redis/DB) and shared across
# instances so dedupe survives restarts and scales horizontally.
_seen_event_ids: set[str] = set()
_seen_lock = threading.Lock()


def verify_signature(raw_body: bytes, provided_sig: str, secret: str) -> bool:
    """Constant-time check of the lowercase hex HMAC-SHA256 over raw bytes."""
    expected = hmac.new(secret.encode("utf-8"), raw_body, hashlib.sha256).hexdigest()
    # compare_digest is constant-time; provided_sig may be None/garbage.
    return hmac.compare_digest(expected, provided_sig or "")


def process_event(event: dict) -> None:
    """Async event handler. Idempotent: dedupe on event id.

    Runs on a background thread so the HTTP handler can ACK within 10s. The
    sample just logs type + id + the dedupe decision; real handlers branch on
    `type` (PROPERTY_CREATED, LEASE_SIGNED, SYNC_COMPLETE, ...).
    """
    event_id = event.get("id", "(missing id)")
    event_type = event.get("type", "(missing type)")

    with _seen_lock:
        if event_id in _seen_event_ids:
            log.info("duplicate %s %s -> skip (already processed)", event_type, event_id)
            return
        _seen_event_ids.add(event_id)

    # Simulate real work happening off the request path.
    log.info("processing %s %s -> new", event_type, event_id)
    # ... business logic here ...


@app.post("/webhooks/propsocket")
def receive():
    secret = os.environ.get("PROPSOCKET_WEBHOOK_SECRET")
    if not secret:
        log.error("PROPSOCKET_WEBHOOK_SECRET is not set")
        return {"error": "server misconfigured"}, 500

    # 1. Capture the RAW bytes BEFORE any JSON parsing — the signature is over
    #    these exact bytes.
    raw_body = request.get_data()

    # 2. Verify the signature (constant-time). Reject bad signature with 401.
    provided_sig = request.headers.get(SIGNATURE_HEADER, "")
    if not verify_signature(raw_body, provided_sig, secret):
        log.warning("rejected: bad signature")
        return {"error": "invalid signature"}, 401

    # 3. Only now parse JSON. Reject unparseable body with 400.
    try:
        event = json.loads(raw_body)
    except (ValueError, UnicodeDecodeError):
        log.warning("rejected: unparseable body")
        return {"error": "invalid JSON"}, 400

    # 4. ACK fast: hand off to a background thread, then return 2xx immediately
    #    (well within the 10s budget). Even duplicates get a 2xx; the worker
    #    skips re-processing.
    threading.Thread(target=process_event, args=(event,), daemon=True).start()
    return {"status": "accepted"}, 202


@app.get("/health")
def health():
    return {"status": "ok"}, 200


if __name__ == "__main__":
    port = int(os.environ.get("PORT", "4000"))
    # Flask's dev server is fine for the sample; use a real WSGI server in prod.
    app.run(host="0.0.0.0", port=port)
