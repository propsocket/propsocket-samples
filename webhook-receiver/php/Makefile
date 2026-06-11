.PHONY: install run

# Dependency-free: nothing to fetch. Kept for a uniform leaf interface.
install:
	@echo "No dependencies — uses only PHP stdlib (hash_hmac, hash_equals)."

# `make run` is the documented UX: cp .env.example .env && make run
# Load .env ourselves, then serve the front controller with PHP's built-in
# server. PORT is optional (defaults to 4000).
run:
	set -a; . ./.env; set +a; php -S 127.0.0.1:$${PORT:-4000} index.php
