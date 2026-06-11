<?php

/**
 * PropSocket REST consumer — list one entity and print id/name/status per row.
 *
 * Reads the PropSocket REST API (read-only, GET) using offset-based pagination
 * and prints a row per record plus a final total count. Defaults to the
 * `properties` entity; change ENTITY below to swap (units, residents, leases).
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

// Swap this single constant to consume a different entity. The API exposes
// four top-level list endpoints: properties, units, residents, leases.
const ENTITY = 'properties';

// rest-consumer stays simple: a modest page size is fine (max allowed is 100).
const PAGE_LIMIT = 25;

/**
 * Print an RFC 7807 problem+json error and exit non-zero.
 *
 * We surface request_id so a user can quote it to support. The backfill
 * sample handles 429/Retry-After; here any non-2xx is fatal.
 */
function die_on_error(ResponseInterface $resp): never
{
    $raw = (string) $resp->getBody();
    $problem = json_decode($raw, true);
    if (!is_array($problem)) {
        $problem = [];
    }

    // request_id mirrors the x-request-id header; fall back to the header.
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
        // We inspect non-2xx ourselves rather than letting Guzzle throw.
        'http_errors' => false,
    ]);

    $offset = 0;
    $total = 0;

    while (true) {
        try {
            $resp = $client->get(ENTITY, [
                'query' => ['limit' => PAGE_LIMIT, 'offset' => $offset],
            ]);
        } catch (GuzzleException $e) {
            // Network/transport failure (DNS, TLS, connect timeout).
            fwrite(STDERR, 'Request failed: ' . $e->getMessage() . "\n");
            exit(1);
        }

        $status = $resp->getStatusCode();
        if ($status < 200 || $status >= 300) {
            die_on_error($resp);
        }

        $body = json_decode((string) $resp->getBody(), true);
        $results = $body['results'] ?? [];
        $meta = $body['meta'] ?? [];

        foreach ($results as $record) {
            // Contract: print id, name, status per row.
            printf(
                "%s\t%s\t%s\n",
                $record['id'] ?? '',
                $record['name'] ?? '',
                $record['status'] ?? ''
            );
            $total++;
        }

        // Last page when hasMore is false; otherwise advance by the page size.
        if (empty($meta['hasMore'])) {
            break;
        }
        $offset += PAGE_LIMIT;
    }

    printf("\nTotal %s: %d\n", ENTITY, $total);
}

main();
