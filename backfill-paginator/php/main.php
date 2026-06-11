<?php

/**
 * PropSocket backfill paginator — page every entity and upsert into a local sink.
 *
 * Walks all four top-level entities (properties, units, residents, leases —
 * parents before children) with stable ascending-by-created_at pagination,
 * using the max page size (100). Each record is "upserted" into a local JSON
 * key/value store keyed by `id`. Because we key on `id`, re-running is a no-op
 * (idempotent) — this is a reference upsert sink, NOT a real database.
 *
 * Handles 429 Too Many Requests by honoring the server's Retry-After header
 * (sleep exactly that many seconds, then retry) — we don't invent a backoff.
 *
 * See the shared contract for the full API surface.
 */

declare(strict_types=1);

require __DIR__ . '/vendor/autoload.php';

use GuzzleHttp\Client;
use GuzzleHttp\Exception\GuzzleException;
use Psr\Http\Message\ResponseInterface;

// Default base URL; override with PROPSOCKET_BASE_URL.
const DEFAULT_BASE_URL = 'https://api.propsocket.io/v1';

// Parents before children so referenced records exist first.
const ENTITIES = ['properties', 'units', 'residents', 'leases'];

// Max page size for an efficient backfill (contract caps limit at 100).
const PAGE_LIMIT = 100;

// Local upsert sink. Keyed by record id, so re-running is idempotent.
const STORE_PATH = __DIR__ . '/backfill-store.json';

/**
 * Print an RFC 7807 problem+json error and exit non-zero.
 *
 * We surface request_id so a user can quote it to support. 429 is handled
 * inline by the caller (Retry-After); any other non-2xx is fatal here.
 */
function die_on_error(ResponseInterface $resp): never
{
    $raw = (string) $resp->getBody();
    $problem = json_decode($raw, true);
    if (!is_array($problem)) {
        $problem = [];
    }

    $requestId = $problem['request_id']
        ?? ($resp->getHeaderLine('x-request-id') ?: '(none)');
    $detail = $problem['detail'] ?? ($raw !== '' ? $raw : '(no detail)');
    $title = $problem['title'] ?? 'Request failed';

    fwrite(
        STDERR,
        sprintf(
            "Error %d %s: %s [request_id=%s]\n",
            $resp->getStatusCode(),
            $title,
            $detail,
            $requestId
        )
    );
    exit(1);
}

/**
 * GET with 429 handling. On 429 we sleep exactly Retry-After seconds (the
 * server's instruction — not an invented backoff) and retry the same request.
 */
function get_with_retry(Client $client, string $entity, int $offset): ResponseInterface
{
    while (true) {
        try {
            $resp = $client->get($entity, [
                'query' => [
                    'limit' => PAGE_LIMIT,
                    'offset' => $offset,
                    // Stable ordering keeps offset pagination consistent
                    // while we walk the whole dataset.
                    'order-by' => 'created_at:asc',
                ],
            ]);
        } catch (GuzzleException $e) {
            fwrite(STDERR, 'Request failed: ' . $e->getMessage() . "\n");
            exit(1);
        }

        $status = $resp->getStatusCode();

        if ($status === 429) {
            // Honor the server's wait exactly, then retry the same page.
            $retryAfter = (int) ($resp->getHeaderLine('Retry-After') ?: '1');
            if ($retryAfter < 1) {
                $retryAfter = 1;
            }
            fwrite(STDERR, sprintf("Rate limited; sleeping %ds (Retry-After)...\n", $retryAfter));
            sleep($retryAfter);
            continue;
        }

        if ($status < 200 || $status >= 300) {
            die_on_error($resp);
        }

        return $resp;
    }
}

/** Load the local upsert sink (id => record), or an empty map. */
function load_store(): array
{
    if (!is_file(STORE_PATH)) {
        return [];
    }
    $data = json_decode((string) file_get_contents(STORE_PATH), true);
    return is_array($data) ? $data : [];
}

/** Persist the upsert sink atomically (write temp, then rename). */
function save_store(array $store): void
{
    $tmp = STORE_PATH . '.tmp';
    file_put_contents($tmp, json_encode($store, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES));
    rename($tmp, STORE_PATH);
}

function main(): void
{
    $apiKey = getenv('PROPSOCKET_API_KEY');
    if ($apiKey === false || $apiKey === '') {
        fwrite(STDERR, "PROPSOCKET_API_KEY is not set. Copy .env.example to .env.\n");
        exit(1);
    }

    $baseUrl = getenv('PROPSOCKET_BASE_URL') ?: DEFAULT_BASE_URL;
    $baseUrl = rtrim($baseUrl, '/');

    $client = new Client([
        'base_uri' => $baseUrl . '/',
        'headers' => ['Authorization' => 'Bearer ' . $apiKey],
        'timeout' => 30,
        'http_errors' => false,
    ]);

    // Single store keyed by id across all entities; ids are globally unique
    // (prefixed ULIDs), so one map is safe and keeps the upsert idempotent.
    $store = load_store();

    foreach (ENTITIES as $entity) {
        $offset = 0;
        $seen = 0;
        $upserted = 0;

        while (true) {
            $resp = get_with_retry($client, $entity, $offset);

            $body = json_decode((string) $resp->getBody(), true);
            $results = $body['results'] ?? [];
            $meta = $body['meta'] ?? [];

            foreach ($results as $record) {
                $id = $record['id'] ?? null;
                if ($id === null) {
                    continue;
                }
                $seen++;
                // Upsert: keying on id makes re-runs a no-op.
                if (!array_key_exists($id, $store)) {
                    $upserted++;
                }
                $store[$id] = $record;
            }

            if (empty($meta['hasMore'])) {
                break;
            }
            $offset += PAGE_LIMIT;
        }

        printf("%-12s seen=%d upserted=%d (new this run)\n", $entity, $seen, $upserted);
    }

    save_store($store);
    printf("\nStore: %d records -> %s\n", count($store), STORE_PATH);
}

main();
