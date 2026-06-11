.PHONY: install deps run

install deps:
	npm install

# Loads .env portably (works on Node 18+), then starts the server.
run: install
	set -a; . ./.env; set +a; node server.js
