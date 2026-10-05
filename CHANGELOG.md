# Changelog

Todas as mudanças relevantes deste projeto são registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto
adota o [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

## [0.1.0] - 2026-10-05

Primeira versão pública.

### Adicionado

- Comandos de bucket: `ls`, `new`, `mv`, `del`, `encrypt`, `edit`.
- Comandos de secret: `show`, `get` (com `-c` e limpeza automática do clipboard), `set`,
  `rm`, `run`.
- Localização do cofre por `--dir`, `SOPS_BOX_DIR` ou `~/secrets`. Binário do sops por `SOPS_BIN`.
- Suíte de testes de ponta a ponta (`tests/tests.sh`).
- Licença GNU AGPL-3.0-or-later, com cabeçalhos SPDX e conformidade com o REUSE.
- Documentação: manual do usuário (`docs/MANUAL.md`) e arquitetura (`docs/ARCHITECTURE.md`).
- Guias da comunidade: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`.
- Modelos de issue e pull request, `CODEOWNERS` e Dependabot.
- CI no GitHub Actions: formatação, clippy, MSRV (1.88), suíte de ponta a ponta com sops
  real, auditoria de dependências e checagem REUSE.
- Workflow de release que publica binários Linux x86_64 com checksum SHA-256.

[Não lançado]: https://github.com/GabrielAderaldo/sops_box/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/GabrielAderaldo/sops_box/releases/tag/v0.1.0
