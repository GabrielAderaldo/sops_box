## O que muda

<!-- Descreva a mudança e o motivo. Referencie a issue: "Closes #123". -->

## Tipo

- [ ] Correção de bug
- [ ] Nova funcionalidade
- [ ] Mudança incompatível (breaking change)
- [ ] Documentação
- [ ] Refatoração, testes ou CI

## Checklist

- [ ] `cargo fmt --check` e `cargo clippy --all-targets -- -D warnings` passam
- [ ] `make test` passa, e adicionei testes para o novo comportamento
- [ ] Atualizei a documentação afetada (`docs/MANUAL.md`, `--help`, `README.md`)
- [ ] Adicionei uma linha no `CHANGELOG.md`, em `[Não lançado]`
- [ ] As [garantias de segurança](../blob/main/docs/ARCHITECTURE.md#garantias-invariantes) continuam valendo
- [ ] Arquivos novos têm cabeçalho SPDX
- [ ] Commits assinados com `Signed-off-by` (`git commit -s`), conforme o [DCO](https://developercertificate.org)

Ao abrir este PR, concordo que minha contribuição é licenciada sob a AGPL-3.0-or-later.
