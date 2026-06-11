// webhook-receiver (Go) — verify PropSocket webhook signatures, dedupe, ack fast.
//
// Reads PROPSOCKET_WEBHOOK_SECRET (required) and PORT (optional, default 4000).
// Stdlib only: net/http + crypto/hmac + crypto/sha256 + encoding/hex. See README.md.
package main

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"io"
	"log"
	"net/http"
	"os"
)

// signatureHeader carries the lowercase-hex HMAC-SHA256 of the raw body.
// Single value — NOT a t=...,v1=... format.
const signatureHeader = "X-PropSocket-Signature"

// event is the subset of the webhook payload we need. We only decode the event
// id (for dedupe) and type (for logging); the full `data` object is ignored here.
type event struct {
	ID   string `json:"id"`   // prefixed evt_...
	Type string `json:"type"` // e.g. LEASE_SIGNED
}

func main() {
	secret := os.Getenv("PROPSOCKET_WEBHOOK_SECRET")
	if secret == "" {
		log.Fatal("error: PROPSOCKET_WEBHOOK_SECRET is not set (copy .env.example to .env)")
	}

	port := os.Getenv("PORT")
	if port == "" {
		port = "4000"
	}

	d := newDeduper()

	mux := http.NewServeMux()
	mux.HandleFunc("/webhooks/propsocket", webhookHandler([]byte(secret), d))

	addr := ":" + port
	log.Printf("listening on %s (POST /webhooks/propsocket)", addr)
	if err := http.ListenAndServe(addr, mux); err != nil {
		log.Fatal(err)
	}
}

func webhookHandler(secret []byte, d *deduper) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodPost {
			http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
			return
		}

		// Capture the RAW bytes before any parsing. The signature is computed over
		// the exact bytes PropSocket sent; re-serializing parsed JSON would change
		// whitespace/key order and break verification.
		body, err := io.ReadAll(r.Body)
		if err != nil {
			http.Error(w, "cannot read body", http.StatusBadRequest)
			return
		}

		// Verify the signature against those exact raw bytes.
		if !validSignature(secret, r.Header.Get(signatureHeader), body) {
			log.Printf("rejected: bad signature")
			http.Error(w, "invalid signature", http.StatusUnauthorized)
			return
		}

		// Parse only after the signature checks out. A 400 here means the body
		// wasn't valid JSON (or had no event id).
		var ev event
		if err := json.Unmarshal(body, &ev); err != nil || ev.ID == "" {
			http.Error(w, "unparseable body", http.StatusBadRequest)
			return
		}

		// Delivery is at-least-once, so the handler must be idempotent. Dedupe on
		// the event id. On a duplicate we still ack 2xx but skip re-processing.
		if d.seen(ev.ID) {
			log.Printf("duplicate: type=%s id=%s (skipped)", ev.Type, ev.ID)
			w.WriteHeader(http.StatusOK)
			return
		}
		d.mark(ev.ID)

		// ACK fast: respond 2xx within 10s, then do the real work in a goroutine.
		// PropSocket treats a slow/failed response as a delivery failure and retries.
		w.WriteHeader(http.StatusOK)

		go process(ev)
	}
}

// validSignature recomputes the HMAC-SHA256 over the raw body and compares it to
// the header value in constant time (hmac.Equal) to avoid timing leaks.
func validSignature(secret []byte, header string, body []byte) bool {
	if header == "" {
		return false
	}
	mac := hmac.New(sha256.New, secret)
	mac.Write(body)
	expected := hex.EncodeToString(mac.Sum(nil))
	return hmac.Equal([]byte(expected), []byte(header))
}

// process is where real work would happen (enqueue a job, update a read model,
// etc.). Here it just logs that the event was accepted for processing.
func process(ev event) {
	log.Printf("processing: type=%s id=%s", ev.Type, ev.ID)
}
