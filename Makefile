PREFIX ?= $(HOME)/.local
CARGO ?= $(shell command -v cargo || echo $(HOME)/.cargo/bin/cargo)
VERSION := $(shell sed -n 's/^version = "\(.*\)"/\1/p' Cargo.toml | head -1)

build:
	$(CARGO) build --release

# Suíte de contrato em Perl (tests/*.t), a mesma que roda contra a versão Windows.
# Interoperabilidade: make test SBOX_OTHER_BIN=/caminho/de/outro/sbox
test: build
	SBOX_BIN=target/release/sbox SBOX_EXPECTED_VERSION=$(VERSION) prove tests/

install: build
	install -Dm755 target/release/sbox $(PREFIX)/bin/sbox

uninstall:
	rm -f $(PREFIX)/bin/sbox

.PHONY: build test install uninstall
