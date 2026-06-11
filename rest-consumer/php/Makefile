.PHONY: install run clean

# Fetch pinned deps (guzzlehttp/guzzle) into vendor/.
install: vendor/autoload.php

vendor/autoload.php: composer.json
	composer install

# `make run` is the documented UX: cp .env.example .env && make run
# Load .env ourselves so the program is self-sufficient.
run: install
	set -a; . ./.env; set +a; php main.php

clean:
	rm -rf vendor composer.lock
