# backfill-paginator (.NET)
#
# Usage: cp .env.example .env && make run
#
# BCL only (HttpClient + System.Text.Json), so `make install` is just `dotnet restore`.

.PHONY: install deps run build clean

# install: restore NuGet packages (none beyond the SDK; resolves the build graph).
install:
	dotnet restore

# deps: alias for install.
deps: install

# build: compile in Release.
build:
	dotnet build -c Release

# run: load .env into the environment, then run.
# `set -a` exports every var sourced from .env so the dotnet process inherits them.
run:
	@test -f .env || (echo "missing .env — run: cp .env.example .env" && exit 1)
	set -a; . ./.env; set +a; dotnet run

clean:
	dotnet clean || true
	rm -rf bin obj
	rm -f backfill-store.json
