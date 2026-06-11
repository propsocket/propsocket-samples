VENV := .venv
PY := $(VENV)/bin/python
PIP := $(VENV)/bin/pip

.PHONY: install run clean

# Create a virtualenv and install pinned deps.
install: $(VENV)/.installed

$(VENV)/.installed: requirements.txt
	python3 -m venv $(VENV)
	$(PIP) install --upgrade pip
	$(PIP) install -r requirements.txt
	touch $@

# `make run` is the documented UX: cp .env.example .env && make run
# Load .env ourselves so the program is self-sufficient.
run: install
	set -a; . ./.env; set +a; $(PY) main.py

clean:
	rm -rf $(VENV) __pycache__
