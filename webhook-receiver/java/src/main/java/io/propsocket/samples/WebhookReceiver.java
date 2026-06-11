// webhook-receiver (Java / Spark) — verify PropSocket webhook signatures and ack fast.
//
// PropSocket POSTs JSON with an `X-PropSocket-Signature` header: a lowercase hex
// HMAC-SHA256 over the RAW request body, keyed by PROPSOCKET_WEBHOOK_SECRET.
//
// Three things this sample gets right and you must keep:
//   1. Raw body: we hash request.bodyAsBytes() BEFORE any JSON parse. Re-serializing
//      parsed JSON would change bytes and break the signature.
//   2. Constant-time compare: MessageDigest.isEqual avoids leaking the secret via
//      timing. Never use String.equals on the signature.
//   3. Ack fast + idempotent: we return 2xx within ~10s, then process on a background
//      executor. Delivery is at-least-once, so we dedupe on the event id (evt_...).
package io.propsocket.samples;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import spark.Spark;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class WebhookReceiver {

    private static final String SIGNATURE_HEADER = "X-PropSocket-Signature";
    private static final ObjectMapper MAPPER = new ObjectMapper();

    // Dedupe set keyed by event id. In-memory is fine for the sample; in
    // production this must be durable (e.g. Redis/DB) with a TTL that outlasts
    // the ~7h36m retry window (immediate, 30s, 1m, 5m, 30m, 1h, 6h -> dead-letter).
    private static final Set<String> SEEN_EVENTS = ConcurrentHashMap.newKeySet();

    // Background worker: ack the request fast, then do the real work here.
    private static final ExecutorService WORKERS = Executors.newFixedThreadPool(4);

    public static void main(String[] args) {
        String secret = System.getenv("PROPSOCKET_WEBHOOK_SECRET");
        if (secret == null || secret.isBlank()) {
            System.err.println("PROPSOCKET_WEBHOOK_SECRET is not set (copy .env.example to .env).");
            System.exit(1);
        }
        byte[] secretBytes = secret.getBytes(StandardCharsets.UTF_8);

        int port = parsePort(System.getenv("PORT"), 4000);
        Spark.port(port);

        Spark.post("/webhooks/propsocket", (request, response) -> {
            // 1. Capture the RAW bytes before touching the body any other way.
            byte[] rawBody = request.bodyAsBytes();

            // 2. Verify the signature against those exact bytes (constant time).
            String provided = request.headers(SIGNATURE_HEADER);
            if (!signatureValid(secretBytes, rawBody, provided)) {
                response.status(401);
                return "invalid signature";
            }

            // Only now parse JSON. A malformed body is a 400.
            JsonNode event;
            try {
                event = MAPPER.readTree(rawBody);
            } catch (Exception e) {
                response.status(400);
                return "unparseable body";
            }

            String eventId = event.path("id").asText(null);
            String eventType = event.path("type").asText("(unknown)");
            if (eventId == null || eventId.isBlank()) {
                response.status(400);
                return "missing event id";
            }

            // 3. Idempotency: dedupe on event id. add() returns false if already
            // present, so duplicates short-circuit. Still ack 2xx (already done).
            boolean isNew = SEEN_EVENTS.add(eventId);
            if (!isNew) {
                System.out.printf("duplicate  type=%s id=%s (skipped)%n", eventType, eventId);
                response.status(200);
                return "duplicate";
            }

            // ACK fast: hand off the work and return 2xx immediately (< 10s budget).
            WORKERS.submit(() -> process(eventType, eventId));
            response.status(200);
            return "ok";
        });

        Spark.awaitInitialization();
        System.out.printf("webhook-receiver listening on :%d  POST /webhooks/propsocket%n", port);

        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            Spark.stop();
            WORKERS.shutdown();
        }));
    }

    // signatureValid recomputes HMAC-SHA256(rawBody) and compares to the provided
    // header in constant time. Returns false on any missing/mismatched value.
    private static boolean signatureValid(byte[] secret, byte[] rawBody, String provided) {
        if (provided == null || provided.isBlank()) {
            return false;
        }
        byte[] expected = hmacSha256Hex(secret, rawBody).getBytes(StandardCharsets.UTF_8);
        byte[] actual = provided.trim().getBytes(StandardCharsets.UTF_8);
        // MessageDigest.isEqual is constant-time in modern JDKs — no early-out on
        // first differing byte, so it doesn't leak the secret via timing.
        return MessageDigest.isEqual(expected, actual);
    }

    private static String hmacSha256Hex(byte[] secret, byte[] message) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(secret, "HmacSHA256"));
            byte[] digest = mac.doFinal(message);
            StringBuilder hex = new StringBuilder(digest.length * 2);
            for (byte b : digest) {
                hex.append(Character.forDigit((b >> 4) & 0xF, 16));
                hex.append(Character.forDigit(b & 0xF, 16));
            }
            return hex.toString();
        } catch (Exception e) {
            // HmacSHA256 is guaranteed present on every JVM; this never fires.
            throw new IllegalStateException("HMAC-SHA256 unavailable", e);
        }
    }

    // process is the async handler. The receiver just logs type + id; real apps
    // would route per event type here.
    private static void process(String type, String id) {
        System.out.printf("processed  type=%s id=%s%n", type, id);
    }

    private static int parsePort(String value, int fallback) {
        if (value == null || value.isBlank()) {
            return fallback;
        }
        try {
            return Integer.parseInt(value.trim());
        } catch (NumberFormatException e) {
            return fallback;
        }
    }

    private WebhookReceiver() {
    }
}
