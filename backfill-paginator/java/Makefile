.PHONY: install run clean

# Compile and resolve deps.
install:
	mvn -q compile

# `make run` is the documented UX: cp .env.example .env && make run
# Load .env ourselves so the program is self-sufficient, then run via exec:java.
run: install
	set -a; . ./.env; set +a; mvn -q exec:java

clean:
	mvn -q clean
	rm -f backfill-store.json
