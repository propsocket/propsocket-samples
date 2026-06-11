<?php

/**
 * PropSocket webhook receiver — verify signature, dedupe, ack fast.
 *
 * A single front-controller script served by PHP's built-in server
 * (`php -S 127.0.0.1:$PORT index.php`). PropSocket POSTs JSON events; we:
 *
 *   1. Read the RAW request body BEFORE any parsing. The signature is an HMAC
 *      over the exact bytes PropSocket sent, so we must verify against those
 *      bytes — re-encoding parsed JSON would change them and break the check.
 *   2. Verify `X-PropSocket-Signature` (lowercase hex HMAC-SHA256 of the raw
 *      body, keyed by PROPSOCKET_WEBHOOK_SECRET) with a CONSTANT-TIME compare
 *      (`hash_equals`) so we don't leak timing information about the secret.
 *   3. Dedupe on the event `id` (prefixed `evt_`). Delivery is at-least-once,
 *      so the handler must be idempotent — a duplicate still returns 2xx but
 *      skips re-processing.
 *   4. ACK fast: return 2xx within 10s, then process asynchronously. PHP is
 *      request/response, so in production you'd enqueue a job (SQS, Redis,
 *      DB-backed queue, ...) and return immediately rather than block here.
 *
 * Responses: 401 bad signature, 400 unparseable body, 200 accepted/duplicate.
 *
 * NOTE: the dedupe set is in-process memory and resets when the server
 * restarts. The built-in server is single-process, so this works for the
 * sample; in production use a durable, shared store with a TTL that outlasts
 * the retry window (see RETRY SCHEDULE below). ~7-day TTL is comfortable.
 *
 * RETRY SCHEDULE (PropSocket side, for reference): immediate, 30s, 1m, 5m,
 * 30m, 1h, 6h -> dead-letter. ~7h36m total. A 7-day dedupe TTL outlasts it.
 *
 * Webhooks are a Scale-plan-and-above feature. See README + pricing.
 */

declare(strict_types=1);

// In-process dedupe set of already-seen event ids. `static` so it persists
// across requests within the single built-in-server process. In production
// this must be a durable, shared store (e.g. Redis SET with a 7-day TTL).
function dedupe_seen(string $eventId): bool
{
    static $seen = [];
    if (isset($seen[$eventId])) {
        return true; // already processed
    }
    $seen[$eventId] = true;
    return false;
}

/** Send a status + JSON body and stop. We keep responses tiny and fast. */
function respond(int $status, array $body): never
{
    http_response_code($status);
    header('Content-Type: application/json');
    echo json_encode($body);
    exit;
}

$secret = getenv('PROPSOCKET_WEBHOOK_SECRET');
if ($secret === false || $secret === '') {
    fwrite(STDERR, "PROPSOCKET_WEBHOOK_SECRET is not set. Copy .env.example to .env.\n");
    respond(500, ['error' => 'server misconfigured: missing signing secret']);
}

// Only POST carries webhook deliveries.
if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'POST') {
    respond(405, ['error' => 'method not allowed']);
}

// 1. Capture the RAW bytes BEFORE parsing — the signature covers these exact
//    bytes. php://input gives the unmodified body (don't json-encode anything).
$rawBody = file_get_contents('php://input');
if ($rawBody === false) {
    $rawBody = '';
}

// 2. Verify the signature in constant time over the raw bytes.
$provided = $_SERVER['HTTP_X_PROPSOCKET_SIGNATURE'] ?? '';
$expected = hash_hmac('sha256', $rawBody, $secret); // lowercase hex
if (!hash_equals($expected, $provided)) {
    // Don't reveal which part failed; just reject.
    respond(401, ['error' => 'invalid signature']);
}

// 3. Parse only AFTER the signature is verified.
$event = json_decode($rawBody, true);
if (!is_array($event) || !isset($event['id']) || !is_string($event['id'])) {
    respond(400, ['error' => 'unparseable or malformed event body']);
}

$eventId = $event['id'];
$eventType = isset($event['type']) && is_string($event['type']) ? $event['type'] : '(unknown)';

// 4. Dedupe on the event id (at-least-once delivery -> idempotent handler).
if (dedupe_seen($eventId)) {
    fwrite(STDOUT, sprintf("[dup]  type=%s id=%s (already processed, skipping)\n", $eventType, $eventId));
    // Still a success: PropSocket should stop retrying.
    respond(200, ['status' => 'duplicate']);
}

fwrite(STDOUT, sprintf("[recv] type=%s id=%s (accepted)\n", $eventType, $eventId));

// ACK fast. Real processing would be enqueued here (e.g. push a job onto a
// queue) and handled by a separate worker — we must not block the response.
// The receiver itself just logs type + id and the dedupe decision.
respond(200, ['status' => 'accepted']);
