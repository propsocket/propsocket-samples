# webhook-receiver (.NET, ASP.NET Core Minimal API)
#
# Usage: cp .env.example .env && make run
#
# ASP.NET Core ships in the shared framework, so `make install` is just
# `dotnet restore` — no external NuGet packages.

.PHONY: install deps run build clean

# install: restore (resolves the build graph; no third-party packages).
install:
	dotnet restore

# deps: alias for install.
deps: install

# build: compile in Release.
build:
	dotnet build -c Release

# run: load .env into the environment, then start the server.
# `set -a` exports every var sourced from .env so the dotnet process inherits them.
run:
	@test -f .env || (echo "missing .env — run: cp .env.example .env" && exit 1)
	set -a; . ./.env; set +a; dotnet run

clean:
	dotnet clean || true
	rm -rf bin obj
