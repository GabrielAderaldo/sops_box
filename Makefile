PREFIX ?= $(HOME)/.local
CARGO ?= $(shell command -v cargo || echo $(HOME)/.cargo/bin/cargo)

build:
	$(CARGO) build --release

test: build
	tests/tests.sh target/release/sbox

install: build
	install -Dm755 target/release/sbox $(PREFIX)/bin/sbox

uninstall:
	rm -f $(PREFIX)/bin/sbox

.PHONY: build test install uninstall
