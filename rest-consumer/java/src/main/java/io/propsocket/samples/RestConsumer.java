// rest-consumer (Java) — list one PropSocket entity, page through results, print rows.
//
// Reads PROPSOCKET_API_KEY (and optional PROPSOCKET_BASE_URL) from the environment.
// HTTP is the JDK built-in java.net.http.HttpClient; JSON is Jackson. See README.md.
package io.propsocket.samples;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;

public final class RestConsumer {

    // Swap this single constant to consume a different entity. The API exposes
    // four top-level list endpoints: properties, units, residents, leases.
    private static final String ENTITY = "properties";

    // rest-consumer stays simple: a modest page size is fine (API max is 100).
    private static final int PAGE_LIMIT = 25;

    private static final String DEFAULT_BASE_URL = "https://api.propsocket.io/v1";

    private static final ObjectMapper MAPPER = new ObjectMapper();

    public static void main(String[] args) {
        String apiKey = System.getenv("PROPSOCKET_API_KEY");
        if (apiKey == null || apiKey.isBlank()) {
            System.err.println("PROPSOCKET_API_KEY is not set (copy .env.example to .env).");
            System.exit(1);
        }

        String baseUrl = envOrDefault("PROPSOCKET_BASE_URL", DEFAULT_BASE_URL).replaceAll("/+$", "");

        HttpClient client = HttpClient.newBuilder()
                .connectTimeout(Duration.ofSeconds(30))
                .build();

        int total = 0;
        int offset = 0;

        try {
            while (true) {
                JsonNode body = fetchPage(client, baseUrl, apiKey, offset);
                JsonNode results = body.path("results");

                for (JsonNode record : results) {
                    // Contract: print id, name, status per row.
                    System.out.printf("%-32s  %-40s  %s%n",
                            record.path("id").asText(),
                            record.path("name").asText(),
                            record.path("status").asText());
                    total++;
                }

                // Last page when the server says hasMore is false; otherwise
                // advance the offset by the page size (offset-pagination contract).
                if (!body.path("meta").path("hasMore").asBoolean(false)) {
                    break;
                }
                offset += PAGE_LIMIT;
            }
        } catch (ApiException e) {
            System.err.println(e.getMessage());
            System.exit(1);
        } catch (Exception e) {
            System.err.println("error: " + e.getMessage());
            System.exit(1);
        }

        System.out.printf("%nTotal %s: %d%n", ENTITY, total);
    }

    // fetchPage requests one page of the entity at the given offset.
    private static JsonNode fetchPage(HttpClient client, String baseUrl, String apiKey, int offset)
            throws Exception {
        String url = String.format("%s/%s?limit=%d&offset=%d", baseUrl, ENTITY, PAGE_LIMIT, offset);

        HttpRequest request = HttpRequest.newBuilder(URI.create(url))
                .header("Authorization", "Bearer " + apiKey)
                .header("Accept", "application/json")
                .timeout(Duration.ofSeconds(30))
                .GET()
                .build();

        HttpResponse<String> response = client.send(request, HttpResponse.BodyHandlers.ofString());

        int status = response.statusCode();
        if (status < 200 || status >= 300) {
            // The backfill sample handles 429/Retry-After; here any non-2xx is fatal.
            throw apiError(response);
        }
        return MAPPER.readTree(response.body());
    }

    // apiError turns a non-2xx response into a readable error. It surfaces the
    // request_id (mirrored by the x-request-id header) so users can quote it to support.
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
            // Body may not be problem+json; fall back to headers/status below.
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

    private static String envOrDefault(String key, String fallback) {
        String v = System.getenv(key);
        return v != null && !v.isBlank() ? v : fallback;
    }

    // Signals a non-2xx API response with a message already formatted for the user.
    private static final class ApiException extends Exception {
        ApiException(String message) {
            super(message);
        }
    }
}
