// PropSocket webhook receiver (Node.js / Express)
//
// Verifies the X-PropSocket-Signature HMAC, dedupes at-least-once deliveries,
// ACKs fast (2xx within 10s), then processes asynchronously.

import express from "express";
import crypto from "node:crypto";

const PORT = process.env.PORT || 4000;
const SECRET = process.env.PROPSOCKET_WEBHOOK_SECRET;

if (!SECRET) {
  console.error("Missing PROPSOCKET_WEBHOOK_SECRET. Copy .env.example -> .env and set it.");
  process.exit(1);
}

const app = express();

// CRITICAL: capture the RAW, unmodified request bytes BEFORE any JSON parsing.
// The HMAC is computed over those exact bytes; parsing + re-serializing would
// change whitespace/key order and break verification. express.raw gives us a
// Buffer on req.body. `type: '*/*'` ensures we capture regardless of the
// declared Content-Type.
app.use(express.raw({ type: "*/*", limit: "1mb" }));

// Delivery is at-least-once, so the handler must be idempotent. Dedupe on the
// event id (evt_...). An in-memory set is fine for a sample; in production use a
// durable store (Redis/DB) with a TTL that outlasts the ~7h36m retry window
// (a 7-day TTL is comfortable).
const seenEvents = new Set();

// Constant-time signature check. The signature is a single lowercase hex
// HMAC-SHA256 over the raw body (NOT a t=...,v1=... format).
function isValidSignature(rawBody, providedSig) {
  if (typeof providedSig !== "string" || providedSig.length === 0) return false;

  const expected = crypto
    .createHmac("sha256", SECRET)
    .update(rawBody) // rawBody is a Buffer — hashed exactly as received
    .digest("hex");

  const a = Buffer.from(expected, "utf8");
  const b = Buffer.from(providedSig, "utf8");

  // timingSafeEqual throws if lengths differ; guard first to avoid leaking
  // length via an exception while still using a constant-time compare.
  if (a.length !== b.length) return false;
  return crypto.timingSafeEqual(a, b);
}

app.post("/webhooks/propsocket", (req, res) => {
  const rawBody = req.body; // Buffer, courtesy of express.raw
  const signature = req.get("X-PropSocket-Signature");

  // Reject bad signatures with 401 BEFORE parsing or trusting any content.
  if (!isValidSignature(rawBody, signature)) {
    console.warn("Rejected webhook: invalid signature");
    return res.status(401).send("invalid signature");
  }

  // Now safe to parse. Unparseable body -> 400.
  let event;
  try {
    event = JSON.parse(rawBody.toString("utf8"));
  } catch {
    console.warn("Rejected webhook: unparseable JSON body");
    return res.status(400).send("invalid JSON");
  }

  const eventId = event?.id;
  if (!eventId) {
    return res.status(400).send("missing event id");
  }

  // Dedupe: on a duplicate, still 2xx (already processed) but skip work.
  if (seenEvents.has(eventId)) {
    console.log(`type=${event.type} id=${eventId} dedupe=duplicate (skipped)`);
    return res.status(200).send("ok (duplicate)");
  }

  seenEvents.add(eventId);
  console.log(`type=${event.type} id=${eventId} dedupe=new (processing)`);

  // ACK fast (within 10s), THEN process out of band. Returning before the work
  // keeps us under PropSocket's delivery timeout; setImmediate defers handling
  // until after the response is flushed.
  res.status(200).send("ok");
  setImmediate(() => processEvent(event));
});

// Stand-in for real work (enqueue a job, update a DB, etc.). Kept trivial here.
async function processEvent(event) {
  // The receiver doesn't need per-type logic — it just demonstrates the ACK /
  // async-processing split. Throwing here must NOT crash the process, since the
  // 2xx is already sent and PropSocket won't retry a delivery we acknowledged.
  try {
    // ... do work keyed on event.data ...
  } catch (err) {
    console.error(`Async processing failed for ${event.id}: ${err.message}`);
  }
}

// Health check.
app.get("/healthz", (_req, res) => res.status(200).send("ok"));

app.listen(PORT, () => {
  console.log(`Webhook receiver listening on http://127.0.0.1:${PORT}`);
  console.log(`POST PropSocket events to /webhooks/propsocket`);
});
