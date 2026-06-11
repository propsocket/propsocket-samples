.PHONY: install deps run

install deps:
	npm install

# Loads .env portably (works on Node 18+), then runs the consumer.
# `set -a` exports every var defined while sourcing .env.
run: install
	set -a; . ./.env; set +a; node index.js
