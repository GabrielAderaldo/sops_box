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

A suíte em [`tests/`](tests) é de ponta a ponta e escrita em Perl 5, só com módulos do core
(`Test::More`, rodada pelo `prove`). Ela é o **contrato** do `sbox`: conversa com o binário
só pela linha de comando, stdin, stdout, stderr e código de saída, e roda igual contra a
versão em Rust (Linux/macOS) e contra a versão em C# ([`windows/`](windows)). Todo pull
request precisa passar por ela no CI.

```sh
make test                                  # compila e roda tudo
SBOX_BIN=target/release/sbox prove -v tests/02-secrets.t   # um arquivo só
```

Para cada mudança de comportamento, adicione casos no arquivo da área (`01-buckets.t`,
`02-secrets.t`, …) com os auxiliares de [`tests/lib/Sbox/Test.pm`](tests/lib/Sbox/Test.pm):

| auxiliar | confere |
|---|---|
| `ok_run('nome', args…)` | o comando sai com 0 |
| `ko_run('nome', args…)` | o comando falha |
| `is(stdout_of(args…), esperado, 'nome')` | igualdade exata, byte a byte |
| `contains(output_of(args…), trecho, 'nome')` | stdout + stderr contêm o trecho |
| `sbox([args], stdin => …, env => {…})` | roda com stdin e ambiente próprios |
| `make_tool('nome', código_perl)` | cria um executável falso (sops, janela) que funciona até no Windows |

Comportamento que só existe numa plataforma fica atrás de `IS_LINUX`, `IS_MACOS` ou
`IS_WINDOWS`. Para um arquivo inteiro, use `plan skip_all`, sempre com o motivo.

Correções de bug devem vir com um teste que falha antes da correção.

### Versão Windows (C#)

O port para Windows mora em [`windows/`](windows): .NET 10, NativeAOT e sem NuGet (só BCL e
P/Invoke). A versão dele é a do `<Version>` em `windows/Sbox.csproj`, independente da do
`Cargo.toml`. Com o .NET SDK 10, `sops`, `age` e Perl (Strawberry Perl):

```powershell
dotnet publish windows/Sbox.csproj -c Release -r win-x64 -o out
$env:SBOX_BIN = "$PWD\out\sbox.exe"; $env:SBOX_EXPECTED_VERSION = '0.1.0'
prove tests/
```

[`windows/tests.txt`](windows/tests.txt) lista os arquivos da suíte que já precisam passar no
Windows: o CI roda esses de forma obrigatória e o resto só como relatório. Ao deixar um
arquivo verde, adicione-o à lista no mesmo commit; nunca tire um arquivo de lá para
"consertar" o CI.

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
