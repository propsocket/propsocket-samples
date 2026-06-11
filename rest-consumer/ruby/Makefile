.PHONY: install deps run

install: ## Install gems
	bundle install

deps: install

# Loads .env into the environment, then runs the consumer.
# `set -a` exports every var sourced from .env so the Ruby process inherits them.
run: install
	set -a; . ./.env; set +a; bundle exec ruby app.rb
