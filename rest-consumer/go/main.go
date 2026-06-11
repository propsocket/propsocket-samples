// rest-consumer (Go) — list one PropSocket entity, page through results, print rows.
//
// Reads PROPSOCKET_API_KEY (and optional PROPSOCKET_BASE_URL) from the environment.
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

// entity is the resource path under the base URL. Swap this single constant to
// consume a different entity: "units", "residents", or "leases".
const entity = "properties"

// pageLimit is the page size for rest-consumer. The API max is 100; the default
// of 25 is plenty for a simple consumer.
const pageLimit = 25

const defaultBaseURL = "https://api.propsocket.io/v1"

// record is the subset of fields the spec asks us to print. The API returns more
// (x_id, integration_id, type, total_units, timestamps, deleted_at); we only
// decode what we display.
type record struct {
	ID     string `json:"id"`
	Name   string `json:"name"`
	Status string `json:"status"`
}

// listResponse is the offset-pagination envelope every list endpoint returns.
type listResponse struct {
	Meta struct {
		Limit   int  `json:"limit"`
		Offset  int  `json:"offset"`
		HasMore bool `json:"hasMore"`
	} `json:"meta"`
	Results []record `json:"results"`
}

// problem is the RFC 7807 problem+json error body the API returns on non-2xx.
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

	total := 0
	offset := 0
	for {
		page, err := fetchPage(client, baseURL, apiKey, offset)
		if err != nil {
			fmt.Fprintf(os.Stderr, "error: %v\n", err)
			os.Exit(1)
		}

		for _, r := range page.Results {
			fmt.Printf("%-32s  %-40s  %s\n", r.ID, r.Name, r.Status)
			total++
		}

		// Last page when the server says there's no more. Advancing the offset
		// by the page size is the offset-pagination contract.
		if !page.Meta.HasMore {
			break
		}
		offset += pageLimit
	}

	fmt.Printf("\nTotal %s: %d\n", entity, total)
}

// fetchPage requests one page of the entity at the given offset.
func fetchPage(client *http.Client, baseURL, apiKey string, offset int) (*listResponse, error) {
	q := url.Values{}
	q.Set("limit", strconv.Itoa(pageLimit))
	q.Set("offset", strconv.Itoa(offset))
	endpoint := fmt.Sprintf("%s/%s?%s", baseURL, entity, q.Encode())

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
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
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

// apiError turns a non-2xx response into a readable error. It surfaces the
// request_id (mirrored by the x-request-id header) so users can quote it to support.
func apiError(resp *http.Response, body []byte) error {
	var p problem
	_ = json.Unmarshal(body, &p) // best-effort; body may not be problem+json

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
