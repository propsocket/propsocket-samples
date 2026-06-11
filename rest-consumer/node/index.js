// PropSocket REST consumer (Node.js)
//
// Lists one entity (default: properties) with offset-based pagination and prints
// id / name / status per row plus a final total. The API is read-only (GET only).
//
// Swap the entity by changing ENTITY below — every list endpoint shares the same
// envelope ({ meta, results }) and pagination contract, so nothing else changes.

import axios from "axios";

// --- Config -----------------------------------------------------------------

const ENTITY = "properties"; // swap to "units" | "residents" | "leases"
const PAGE_SIZE = 25; // default; max 100 (>100 returns 400)

const API_KEY = process.env.PROPSOCKET_API_KEY;
const BASE_URL = process.env.PROPSOCKET_BASE_URL || "https://api.propsocket.io/v1";

if (!API_KEY) {
  console.error("Missing PROPSOCKET_API_KEY. Copy .env.example -> .env and set your ps_test_ key.");
  process.exit(1);
}

const client = axios.create({
  baseURL: BASE_URL,
  headers: { Authorization: `Bearer ${API_KEY}` },
  // Don't throw on non-2xx — we surface the RFC 7807 body ourselves.
  validateStatus: () => true,
  timeout: 30_000,
});

// --- Error handling ---------------------------------------------------------

// On any non-2xx, print status + detail + request_id (so the user can quote it
// to support) and exit non-zero. The 429 path doesn't apply here — rest-consumer
// stays simple and lets the backfill paginator own rate-limit handling.
function failOnError(res) {
  const requestId =
    res.headers?.["x-request-id"] || res.data?.request_id || "(none)";
  const detail = res.data?.detail || res.statusText || "Unknown error";
  console.error(`Request failed: HTTP ${res.status} — ${detail}`);
  console.error(`request_id: ${requestId}`);
  process.exit(1);
}

// --- Main -------------------------------------------------------------------

async function main() {
  let offset = 0;
  let total = 0;

  // Walk pages until meta.hasMore === false.
  for (;;) {
    const res = await client.get(`/${ENTITY}`, {
      params: { limit: PAGE_SIZE, offset },
    });

    if (res.status < 200 || res.status >= 300) failOnError(res);

    const { meta, results } = res.data;

    for (const row of results) {
      console.log(`${row.id}  ${row.name ?? "(no name)"}  [${row.status ?? "?"}]`);
      total += 1;
    }

    if (!meta?.hasMore) break;
    offset += meta.limit; // advance by the page size the server actually used
  }

  console.log(`\nTotal ${ENTITY}: ${total}`);
}

main().catch((err) => {
  // Network/transport failures (DNS, TLS, timeout) land here.
  console.error(`Unexpected error: ${err.message}`);
  process.exit(1);
});
