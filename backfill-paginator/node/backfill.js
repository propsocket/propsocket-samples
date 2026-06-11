// PropSocket backfill paginator (Node.js)
//
// Paginates every entity (parents before children) into a local upsert sink
// keyed by record id, so re-running is a no-op. Handles 429/Retry-After by
// sleeping exactly as long as the server asks. Read-only (GET).

import axios from "axios";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// --- Config -----------------------------------------------------------------

// Parents before children: properties -> units -> residents -> leases.
const ENTITIES = ["properties", "units", "residents", "leases"];
const PAGE_SIZE = 100; // max allowed; minimizes round-trips for a full backfill

const API_KEY = process.env.PROPSOCKET_API_KEY;
const BASE_URL = process.env.PROPSOCKET_BASE_URL || "https://api.propsocket.io/v1";

if (!API_KEY) {
  console.error("Missing PROPSOCKET_API_KEY. Copy .env.example -> .env and set your ps_test_ key.");
  process.exit(1);
}

// Reference upsert sink: a JSON file keyed by record id. NOT a real DB — this
// stands in for "write to your warehouse." Keying on id makes re-runs idempotent.
const __dirname = path.dirname(fileURLToPath(import.meta.url));
const STORE_PATH = path.join(__dirname, "backfill-store.json");

const client = axios.create({
  baseURL: BASE_URL,
  headers: { Authorization: `Bearer ${API_KEY}` },
  validateStatus: () => true,
  timeout: 30_000,
});

// --- Sink -------------------------------------------------------------------

function loadStore() {
  try {
    return JSON.parse(fs.readFileSync(STORE_PATH, "utf8"));
  } catch {
    // Missing/corrupt file -> start fresh. Each entity gets its own keyspace.
    return {};
  }
}

function saveStore(store) {
  fs.writeFileSync(STORE_PATH, JSON.stringify(store, null, 2));
}

// --- Helpers ----------------------------------------------------------------

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function failOnError(res) {
  const requestId =
    res.headers?.["x-request-id"] || res.data?.request_id || "(none)";
  const detail = res.data?.detail || res.statusText || "Unknown error";
  console.error(`Request failed: HTTP ${res.status} — ${detail}`);
  console.error(`request_id: ${requestId}`);
  process.exit(1);
}

// GET with 429/Retry-After handling. On 429 the server tells us exactly how
// long to wait via the Retry-After header — honor it, don't invent a backoff.
async function getWithRetry(url, params) {
  for (;;) {
    const res = await client.get(url, { params });

    if (res.status === 429) {
      const retryAfter = parseInt(res.headers?.["retry-after"] ?? "1", 10);
      const seconds = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : 1;
      console.log(`  rate limited (429) — waiting ${seconds}s per Retry-After`);
      await sleep(seconds * 1000);
      continue; // retry the same page
    }

    if (res.status < 200 || res.status >= 300) failOnError(res);
    return res;
  }
}

// --- Backfill ---------------------------------------------------------------

async function backfillEntity(entity, store) {
  if (!store[entity]) store[entity] = {};
  const sink = store[entity];

  let offset = 0;
  let upserted = 0;

  // Stable pagination: created_at:asc so new rows append at the end and never
  // shift records across page boundaries mid-backfill.
  for (;;) {
    const res = await getWithRetry(`/${entity}`, {
      limit: PAGE_SIZE,
      offset,
      "order-by": "created_at:asc",
    });

    const { meta, results } = res.data;

    for (const row of results) {
      // Upsert: keying on id means a re-run overwrites with identical data —
      // idempotent no-op rather than duplicates.
      sink[row.id] = row;
      upserted += 1;
    }

    if (!meta?.hasMore) break;
    offset += meta.limit;
  }

  // Persist after each entity so a crash mid-run keeps prior progress.
  saveStore(store);
  console.log(`  ${entity}: ${upserted} fetched, ${Object.keys(sink).length} total in sink`);
}

async function main() {
  const store = loadStore();

  console.log(`Backfilling into ${STORE_PATH}`);
  for (const entity of ENTITIES) {
    console.log(`\nEntity: ${entity}`);
    await backfillEntity(entity, store);
  }

  const grand = ENTITIES.reduce((n, e) => n + Object.keys(store[e] ?? {}).length, 0);
  console.log(`\nDone. ${grand} records across ${ENTITIES.length} entities.`);
}

main().catch((err) => {
  console.error(`Unexpected error: ${err.message}`);
  process.exit(1);
});
