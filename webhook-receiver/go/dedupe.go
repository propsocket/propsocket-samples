package main

import (
	"sync"
	"time"
)

// dedupeTTL outlasts PropSocket's ~7h36m retry window (immediate, 30s, 1m, 5m,
// 30m, 1h, 6h → dead-letter). 7 days gives comfortable margin.
const dedupeTTL = 7 * 24 * time.Hour

// deduper is an in-memory, TTL'd set of processed event ids. It's safe for
// concurrent use because handlers ack and then process in goroutines.
//
// NOTE: in production this MUST be durable (Redis/Postgres). An in-memory set
// loses its state on restart, which would let already-processed events through.
type deduper struct {
	mu    sync.Mutex
	seen_ map[string]time.Time
}

func newDeduper() *deduper {
	return &deduper{seen_: make(map[string]time.Time)}
}

// seen reports whether the event id is already recorded and unexpired. It also
// opportunistically evicts expired entries it encounters.
func (d *deduper) seen(id string) bool {
	d.mu.Lock()
	defer d.mu.Unlock()
	ts, ok := d.seen_[id]
	if !ok {
		return false
	}
	if time.Since(ts) > dedupeTTL {
		delete(d.seen_, id)
		return false
	}
	return true
}

// mark records the event id as processed, stamped now for TTL purposes.
func (d *deduper) mark(id string) {
	d.mu.Lock()
	defer d.mu.Unlock()
	d.seen_[id] = time.Now()
}
