// backfill-paginator (Java) — page through every PropSocket entity into a local sink.
//
// Iterates all four top-level entities (properties, units, residents, leases —
// parents before children), pages with limit=100 and a stable order-by=created_at:asc,
// and "upserts" each record into a JSON file keyed by id. Keying on id makes
// re-runs idempotent (a no-op). This is a REFERENCE upsert sink, not a real DB.
//
// Rate limits: this is the sample most likely to hit the 150 req/min cap. On a
// 429 we honor the server's Retry-After header exactly (sleep that many seconds,
// then retry the same request) — we do NOT invent our own backoff.
package io.propsocket.samples;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;

public final class BackfillPaginator {

    // Parents before children: properties and units anchor residents and leases.
    private static final String[] ENTITIES = {"properties", "units", "residents", "leases"};

    // Backfill uses the max page size and a stable sort so paging is deterministic
    // even as new rows arrive (created_at:asc never reshuffles earlier pages).
    private static final int PAGE_LIMIT = 100;
    private static final String ORDER_BY = "created_at:asc";

    private static final String DEFAULT_BASE_URL = "https://api.propsocket.io/v1";
    private static final Path STORE_PATH = Path.of("backfill-store.json");

    private static final ObjectMapper MAPPER = new ObjectMapper();

    public static void main(String[] args) throws Exception {
        String apiKey = System.getenv("PROPSOCKET_API_KEY");
        if (apiKey == null || apiKey.isBlank()) {
            System.err.println("PROPSOCKET_API_KEY is not set (copy .env.example to .env).");
            System.exit(1);
        }
        String baseUrl = envOrDefault("PROPSOCKET_BASE_URL", DEFAULT_BASE_URL).replaceAll("/+$", "");

        HttpClient client = HttpClient.newBuilder()
                .connectTimeout(Duration.ofSeconds(30))
                .build();

        // Load any existing sink so re-runs are idempotent across processes too.
        ObjectNode store = loadStore();

        try {
            for (String entity : ENTITIES) {
                int seen = backfillEntity(client, baseUrl, apiKey, entity, store);
                System.out.printf("%-12s upserted %d records%n", entity, seen);
            }
        } catch (ApiException e) {
            System.err.println(e.getMessage());
            // Persist what we have so far before exiting — partial progress is safe
            // to resume because we key on id.
            saveStore(store);
            System.exit(1);
        }

        saveStore(store);
        System.out.printf("%nWrote %s (total records: %d)%n", STORE_PATH, store.size());
    }

    // backfillEntity pages through one entity, upserting each record into `store`
    // under its bucket. Returns the number of records seen for this entity.
    private static int backfillEntity(HttpClient client, String baseUrl, String apiKey,
                                      String entity, ObjectNode store) throws Exception {
        ObjectNode bucket = store.has(entity)
                ? (ObjectNode) store.get(entity)
                : store.putObject(entity);

        int offset = 0;
        int seen = 0;

        while (true) {
            JsonNode body = fetchPage(client, baseUrl, apiKey, entity, offset);
            JsonNode results = body.path("results");

            for (JsonNode record : results) {
                String id = record.path("id").asText(null);
                if (id == null || id.isBlank()) {
                    continue; // every record has a prefixed-ULID id; skip if absent
                }
                // Upsert: keying on id means a redelivered/overlapping record just
                // overwrites itself — re-running the whole backfill is a no-op.
                bucket.set(id, record);
                seen++;
            }

            if (!body.path("meta").path("hasMore").asBoolean(false)) {
                break;
            }
            offset += PAGE_LIMIT;
        }
        return seen;
    }

    // fetchPage requests one page, transparently honoring 429/Retry-After by
    // sleeping the exact server-specified duration and retrying the same request.
    private static JsonNode fetchPage(HttpClient client, String baseUrl, String apiKey,
                                      String entity, int offset) throws Exception {
        String url = String.format("%s/%s?limit=%d&offset=%d&order-by=%s",
                baseUrl, entity, PAGE_LIMIT, offset, ORDER_BY);

        HttpRequest request = HttpRequest.newBuilder(URI.create(url))
                .header("Authorization", "Bearer " + apiKey)
                .header("Accept", "application/json")
                .timeout(Duration.ofSeconds(30))
                .GET()
                .build();

        while (true) {
            HttpResponse<String> response =
                    client.send(request, HttpResponse.BodyHandlers.ofString());
            int status = response.statusCode();

            if (status == 429) {
                // Honor the server's wait exactly; don't invent a backoff.
                long waitSeconds = response.headers()
                        .firstValue("Retry-After")
                        .map(BackfillPaginator::parseLong)
                        .orElse(1L);
                System.err.printf("rate limited on %s; sleeping %ds (Retry-After)%n",
                        entity, waitSeconds);
                Thread.sleep(waitSeconds * 1000L);
                continue; // retry the same page
            }

            if (status < 200 || status >= 300) {
                throw apiError(response);
            }
            return MAPPER.readTree(response.body());
        }
    }

    private static ObjectNode loadStore() {
        if (Files.exists(STORE_PATH)) {
            try {
                JsonNode node = MAPPER.readTree(STORE_PATH.toFile());
                if (node.isObject()) {
                    return (ObjectNode) node;
                }
            } catch (IOException e) {
                System.err.println("warning: could not read existing store, starting fresh: "
                        + e.getMessage());
            }
        }
        return MAPPER.createObjectNode();
    }

    private static void saveStore(ObjectNode store) throws IOException {
        MAPPER.writerWithDefaultPrettyPrinter().writeValue(STORE_PATH.toFile(), store);
    }

    // apiError surfaces the request_id (mirrors the x-request-id header) so users
    // can quote it to support.
    private static ApiException apiError(HttpResponse<String> response) {
        String title = null;
        String detail = null;
        String requestId = null;
        try {
            JsonNode problem = MAPPER.readTree(response.body());
            title = textOrNull(problem, "title");
            detail = textOrNull(problem, "detail");
            requestId = textOrNull(problem, "request_id");
        } catch (Exception ignored) {
            // Body may not be problem+json.
        }
        if (requestId == null) {
            requestId = response.headers().firstValue("x-request-id").orElse("(none)");
        }
        if (detail == null) {
            detail = title != null ? title : "(no detail)";
        }
        return new ApiException(String.format(
                "Error %d: %s [request_id=%s]", response.statusCode(), detail, requestId));
    }

    private static String textOrNull(JsonNode node, String field) {
        JsonNode v = node.get(field);
        return v != null && !v.isNull() ? v.asText() : null;
    }

    private static long parseLong(String s) {
        try {
            return Long.parseLong(s.trim());
        } catch (NumberFormatException e) {
            return 1L;
        }
    }

    private static String envOrDefault(String key, String fallback) {
        String v = System.getenv(key);
        return v != null && !v.isBlank() ? v : fallback;
    }

    private static final class ApiException extends Exception {
        ApiException(String message) {
            super(message);
        }
    }

    private BackfillPaginator() {
    }
}
