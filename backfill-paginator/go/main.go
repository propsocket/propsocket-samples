// backfill-paginator (Go) — page through all four entities and upsert into a
// local key/value sink keyed by id. Idempotent: re-running is a no-op.
//
// Reads PROPSOCKET_API_KEY (and optional PROPSOCKET_BASE_URL) from the environment.
// Handles 429 Too Many Requests by honoring the Retry-After header.
// Stdlib only: net/http + encoding/json. See README.md.
package main

import (
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"strconv"
	"time"
)

// entities are backfilled in this order: parents before children. The spec's
// four top-level list endpoints.
var entities = []string{"properties", "units", "residents", "leases"}

// pageLimit uses the API max for an efficient backfill.
const pageLimit = 100

// storePath is the local upsert sink. A JSON file keyed by id — a reference
// sink, NOT a real DB. Re-running keys on id, so it's idempotent.
const storePath = "backfill-store.json"

const defaultBaseURL = "https://api.propsocket.io/v1"

// record keeps only the id we key on plus the raw JSON for the sink. We don't
// need typed fields here; the backfill just stores whatever the API returns.
type record struct {
	ID string `json:"id"`
}

// listResponse is the offset-pagination envelope.
type listResponse struct {
	Meta struct {
		HasMore bool `json:"hasMore"`
	} `json:"meta"`
	// Results is kept as raw messages so we can store full records verbatim
	// while still reading the id out of each.
	Results []json.RawMessage `json:"results"`
}

// problem is the RFC 7807 problem+json error body.
type problem struct {
	Title     string `json:"title"`
	Detail    string `json:"detail"`
	RequestID string `json:"request_id"`
}

func main() {
	apiKey := os.Getenv("PROPSOCKET_API_KEY")
	if apiKey == "" {
		fmt.Fprintln(os.Stderr, "error: PROPSOCKET_API_KEY is not set (copy .env.example to .env)")
		os.Exit(1)
	}

	baseURL := os.Getenv("PROPSOCKET_BASE_URL")
	if baseURL == "" {
		baseURL = defaultBaseURL
	}

	client := &http.Client{Timeout: 30 * time.Second}

	// Load the existing sink so a re-run is idempotent (keyed by id).
	store, err := loadStore(storePath)
	if err != nil {
		fmt.Fprintf(os.Stderr, "error loading store: %v\n", err)
		os.Exit(1)
	}

	for _, entity := range entities {
		seen, upserted, err := backfill(client, baseURL, apiKey, entity, store)
		if err != nil {
			fmt.Fprintf(os.Stderr, "error: %v\n", err)
			os.Exit(1)
		}
		// Persist after each entity so progress survives an interrupt.
		if err := saveStore(storePath, store); err != nil {
			fmt.Fprintf(os.Stderr, "error saving store: %v\n", err)
			os.Exit(1)
		}
		fmt.Printf("%-11s seen=%d upserted=%d (skipped=%d)\n", entity, seen, upserted, seen-upserted)
	}

	fmt.Printf("\nStore: %s (%d records total)\n", storePath, len(store))
}

// backfill pages through one entity with stable ascending sort and upserts each
// record into the store. Returns (records seen, records upserted).
func backfill(client *http.Client, baseURL, apiKey, entity string, store map[string]json.RawMessage) (int, int, error) {
	seen, upserted := 0, 0
	offset := 0
	for {
		page, err := fetchPage(client, baseURL, apiKey, entity, offset)
		if err != nil {
			return seen, upserted, err
		}

		for _, raw := range page.Results {
			var r record
			if err := json.Unmarshal(raw, &r); err != nil || r.ID == "" {
				return seen, upserted, fmt.Errorf("record without id in %s", entity)
			}
			seen++
			// Upsert keyed by id. We only count it as "upserted" when it's new,
			// so a clean re-run reports upserted=0 (the idempotency signal).
			if _, exists := store[r.ID]; !exists {
				upserted++
			}
			store[r.ID] = raw
		}

		if !page.Meta.HasMore {
			break
		}
		offset += pageLimit
	}
	return seen, upserted, nil
}

// fetchPage requests one page, retrying on 429 by sleeping exactly Retry-After
// seconds (the server tells us how long to wait — we don't invent a backoff).
func fetchPage(client *http.Client, baseURL, apiKey, entity string, offset int) (*listResponse, error) {
	q := url.Values{}
	q.Set("limit", strconv.Itoa(pageLimit))
	q.Set("offset", strconv.Itoa(offset))
	// Stable pagination requires a deterministic sort; ascending created_at means
	// new rows append at the end and don't shift the pages we've already read.
	q.Set("order-by", "created_at:asc")
	endpoint := fmt.Sprintf("%s/%s?%s", baseURL, entity, q.Encode())

	for {
		req, err := http.NewRequest(http.MethodGet, endpoint, nil)
		if err != nil {
			return nil, err
		}
		req.Header.Set("Authorization", "Bearer "+apiKey)
		req.Header.Set("Accept", "application/json")

		resp, err := client.Do(req)
		if err != nil {
			return nil, err
		}

		if resp.StatusCode == http.StatusTooManyRequests {
			wait := retryAfter(resp.Header.Get("Retry-After"))
			resp.Body.Close()
			fmt.Fprintf(os.Stderr, "rate limited (429); waiting %s before retry\n", wait)
			time.Sleep(wait)
			continue // retry the same offset
		}

		body, err := io.ReadAll(resp.Body)
		resp.Body.Close()
		if err != nil {
			return nil, err
		}

		if resp.StatusCode < 200 || resp.StatusCode >= 300 {
			return nil, apiError(resp, body)
		}

		var page listResponse
		if err := json.Unmarshal(body, &page); err != nil {
			return nil, fmt.Errorf("decoding response: %w", err)
		}
		return &page, nil
	}
}

// retryAfter parses the Retry-After header (delta-seconds). Falls back to 1s if
// it's missing or unparseable so we always make forward progress.
func retryAfter(header string) time.Duration {
	if secs, err := strconv.Atoi(header); err == nil && secs >= 0 {
		return time.Duration(secs) * time.Second
	}
	return time.Second
}

// apiError turns a non-2xx response into a readable error, surfacing request_id
// (mirrored by the x-request-id header) for support.
func apiError(resp *http.Response, body []byte) error {
	var p problem
	_ = json.Unmarshal(body, &p)

	reqID := p.RequestID
	if reqID == "" {
		reqID = resp.Header.Get("x-request-id")
	}
	detail := p.Detail
	if detail == "" {
		detail = p.Title
	}
	if detail == "" {
		detail = http.StatusText(resp.StatusCode)
	}
	return fmt.Errorf("HTTP %d: %s (request_id: %s)", resp.StatusCode, detail, reqID)
}

// loadStore reads the existing sink, returning an empty map if it doesn't exist.
func loadStore(path string) (map[string]json.RawMessage, error) {
	data, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		return make(map[string]json.RawMessage), nil
	}
	if err != nil {
		return nil, err
	}
	store := make(map[string]json.RawMessage)
	if len(data) == 0 {
		return store, nil
	}
	if err := json.Unmarshal(data, &store); err != nil {
		return nil, err
	}
	return store, nil
}

// saveStore writes the sink as pretty JSON.
func saveStore(path string, store map[string]json.RawMessage) error {
	data, err := json.MarshalIndent(store, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, data, 0o644)
}
