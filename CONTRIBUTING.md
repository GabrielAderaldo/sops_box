# Contribuindo com o `sbox`

Obrigado por querer ajudar! Este guia explica como propor mudanças de um jeito que facilite
a revisão e mantenha o projeto seguro, já que ele lida com segredos.

Ao participar, você concorda em seguir o [Código de Conduta](CODE_OF_CONDUCT.md).

## Formas de contribuir

- **Reportar um bug**: abra uma [issue](https://github.com/GabrielAderaldo/sops_box/issues/new/choose)
  usando o modelo de bug.
- **Sugerir uma funcionalidade**: abra uma issue com o modelo de sugestão **antes** de
  escrever código. Assim combinamos o escopo e ninguém perde trabalho.
- **Melhorar a documentação**: erros de digitação, exemplos melhores e explicações mais
  claras são sempre bem-vindos, direto por pull request.
- **Enviar código**: veja o fluxo abaixo.
- **Vulnerabilidades**: **nunca** em issue pública. Siga o [SECURITY.md](SECURITY.md).

## Ambiente de desenvolvimento

Você precisa de:

- Linux;
- Rust ≥ 1.88 com `rustfmt` e `clippy` (`rustup component add rustfmt clippy`);
- [sops](https://github.com/getsops/sops) e [age](https://github.com/FiloSottile/age), para a suíte de testes;
- opcional: `xclip` e uma sessão gráfica, para os testes de clipboard.

Os testes copiam o `.sops.yaml` de `~/secrets` para cofres temporários e nunca tocam nos
seus buckets. Se você ainda não tem um cofre, crie um só para desenvolver:

```sh
mkdir -p ~/.config/sops/age ~/secrets
age-keygen -o ~/.config/sops/age/keys.txt
printf 'creation_rules:\n  - path_regex: \\.enc\\.(yaml|yml)$\n    age: %s\n' \
  "$(age-keygen -y ~/.config/sops/age/keys.txt)" > ~/secrets/.sops.yaml
```

## Fluxo

1. Faça um fork e crie uma branch a partir da `main`: `git switch -c fix/descricao-curta`.
2. Faça a mudança, com testes.
3. Rode a checagem completa, a mesma do CI:

   ```sh
   cargo fmt --check
   cargo clippy --all-targets -- -D warnings
   make test
   ```

4. Atualize a documentação afetada (`docs/MANUAL.md`, `--help` em `command_line.rs`,
   `README.md`) e adicione uma linha em `CHANGELOG.md`, na seção `[Não lançado]`.
5. Abra o pull request preenchendo o modelo. PRs pequenos e focados são revisados mais rápido.

## Padrões de código

O código tem um estilo próprio, descrito em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#estilo-rust-que-se-lê-como-swift).
Em resumo:

- nomes por extenso e de domínio (`standard_input`, `Bucket`), sem abreviações;
- `guard!(condição, else: SboxError::…)` para saídas antecipadas;
- toda mensagem para o usuário é uma variante de `SboxError`, com o texto em `error.rs`;
- comentários explicam o **porquê**, não o quê, em português;
- formatação pelo `rustfmt.toml` do projeto (`cargo fmt`).

### Regras para mudanças sensíveis

Toda mudança precisa preservar as [garantias](docs/ARCHITECTURE.md#garantias-invariantes) do
projeto. Em particular:

- nenhum segredo pode ir para argumentos de processo, variáveis de log ou arquivos em texto puro;
- nenhum código pode sobrescrever ou truncar um bucket existente;
- todo código de saída do sops precisa ser conferido.

**Dependências novas** precisam de justificativa no PR. O projeto tem uma única
dependência de propósito. Qualquer crate novo precisa ter licença compatível com a
AGPL-3.0 (MIT, Apache-2.0, BSD, ISC, MPL-2.0, LGPL, GPL-3.0 etc.).

## Testes

A suíte em [`tests/tests.sh`](tests/tests.sh) é de ponta a ponta. Para cada mudança de
comportamento, adicione casos usando os auxiliares que já existem:

| auxiliar | confere |
|---|---|
| `ok "nome" cmd…` | o comando sai com 0 |
| `ko "nome" cmd…` | o comando falha |
| `eq "nome" esperado obtido` | igualdade exata |
| `has "nome" trecho texto` | o texto contém o trecho |

Correções de bug devem vir com um teste que falha antes da correção.

## Mensagens de commit

Use [Conventional Commits](https://www.conventionalcommits.org/pt-br/), em português:

```text
fix(set): preserva \r no fim do valor lido do stdin

O `read_value_from_standard_input` só deve remover o `\n` final.

Signed-off-by: Seu Nome <voce@exemplo.com>
```

Tipos usados: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`.
Mudanças incompatíveis levam `!` (`feat!:`) e um rodapé `BREAKING CHANGE:`.

## Certificado de Origem (DCO)

Todo commit precisa de um `Signed-off-by`, que certifica que você tem o direito de enviar
aquele código sob a licença do projeto, nos termos do
[Developer Certificate of Origin 1.1](https://developercertificate.org). Basta commitar com `-s`:

```sh
git commit -s -m "fix: …"
```

## Licença das contribuições

O projeto é licenciado sob a [GNU AGPL-3.0-or-later](LICENSE). Ao enviar uma contribuição,
você concorda que ela será distribuída sob essa mesma licença (*inbound = outbound*). Não há
CLA nem cessão de direitos autorais: você continua dono do que escreveu.

Arquivos de código novos devem começar com o cabeçalho SPDX:

```rust
// SPDX-FileCopyrightText: 2026 Seu Nome
// SPDX-License-Identifier: AGPL-3.0-or-later
```

## Versionamento e releases

O projeto segue o [Versionamento Semântico](https://semver.org/lang/pt-BR/). Enquanto estiver
em `0.x`, mudanças incompatíveis sobem a versão *minor*. Para publicar uma versão, o mantenedor:

1. move as entradas de `[Não lançado]` para uma nova seção no `CHANGELOG.md`;
2. atualiza `version` no `Cargo.toml` (e o `Cargo.lock`, com `cargo build`);
3. cria a tag anotada `vX.Y.Z` (assinada, se houver chave configurada) e a envia. O workflow de release compila e publica os binários.
