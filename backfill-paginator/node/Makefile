.PHONY: install deps run

install deps:
	npm install

# Loads .env portably (works on Node 18+), then runs the backfill.
run: install
	set -a; . ./.env; set +a; node backfill.js
