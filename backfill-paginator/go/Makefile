# backfill-paginator (Go)
#
# Usage: cp .env.example .env && make run
#
# Stdlib only, so `make install` just builds; there are no deps to fetch.

.PHONY: run install build clean

# install: no third-party deps; verify the module builds.
install:
	go build ./...

# build: compile the binary into the local dir.
build:
	go build -o backfill-paginator .

# run: load .env into the environment, then run.
# `set -a` exports every var sourced from .env so the Go process inherits them.
run:
	@test -f .env || (echo "missing .env — run: cp .env.example .env" && exit 1)
	set -a; . ./.env; set +a; go run .

# clean: drop the local upsert sink to force a full re-backfill.
clean:
	rm -f backfill-store.json
