# Arquitetura do `sbox`

Como o código está organizado, por que ele é assim e quais garantias ele mantém. Leitura
recomendada antes de contribuir (veja também o [CONTRIBUTING](../CONTRIBUTING.md)).

## Visão geral

```text
           argv
            │
            ▼
  ┌───────────────────┐      ┌──────────────┐
  │ command_line.rs   │ ───▶ │ enum Command │   tipado: cada variante já traz seus campos
  └───────────────────┘      └──────┬───────┘
                                    │  main.rs: --help/--version saem aqui, sem tocar no cofre
                                    ▼
  ┌───────────────────┐      ┌──────────────┐
  │ vault.rs          │ ───▶ │ Vault        │   --dir → $SOPS_BOX_DIR → ~/secrets (+ .sops.yaml)
  └───────────────────┘      └──────┬───────┘
                                    ▼
  ┌──────────────────────────────────────────┐
  │ sbox.rs — Sbox::run(command)             │   entra no cofre (chdir) e despacha
  └───┬─────────────┬──────────────┬─────────┘
      ▼             ▼              ▼
  bucket.rs      sops.rs       pasteboard.rs / console.rs
  (lê YAML,      (binário      (clipboard, cores,
   sem decriptar) sops)         perguntas no /dev/tty)
                    │
                    ▼
               process.rs + json.rs
```

O binário é **um único crate**, sem bibliotecas além da `libc`. Todo o resto (parser de
argumentos, JSON, cores, processos) é escrito à mão para manter o binário pequeno, a
superfície de dependências mínima e o código auditável de ponta a ponta. Para um programa
que manipula segredos, cada dependência a menos é uma cadeia de suprimentos a menos.

## Mapa do código

| arquivo | responsabilidade |
|---|---|
| `main.rs` | entrada: lê a linha de comando, acha o cofre, despacha. Erros viram `erro: …` e código 1 |
| `command_line.rs` | `argv` → `enum Command` tipado, mais o texto do `--help`. Depois daqui ninguém mais olha string de flag |
| `sbox.rs` | o que cada comando faz. `Sbox` guarda o cofre, o sops e o diretório de onde o usuário chamou |
| `vault.rs` | `Vault`: localiza o cofre e lista os buckets |
| `bucket.rs` | `Bucket` (nome ↔ arquivo), `BucketSummary` (chaves lidas sem decriptar), `Secret` |
| `sops.rs` | `Sops`: `decrypt`, `set`, `unset`, `encrypt`, `encrypt_in_place`, `edit` |
| `json.rs` | `JSONEncoder`/`JSONDecoder` mínimos: strings na ida, objeto plano `{"K": "v"}` na volta |
| `process.rs` | `Process` (builder de processo filho), `ProcessResult`, `ProcessInfo` |
| `file_manager.rs` | `FileManager`: arquivos e diretórios, sem nunca sobrescrever |
| `pasteboard.rs` | `Pasteboard::general()`: `wl-copy`/`xclip` com limpeza agendada |
| `console.rs` | cores, mensagens no stderr, pergunta escondida e s/N pelo `/dev/tty` |
| `error.rs` | `enum SboxError` com **todas** as mensagens num lugar só, e a macro `guard!` |

## Estilo: Rust que se lê como Swift

O código imita, de propósito, a leitura de Swift com Foundation:

- **Tipos com nome de domínio** (`Vault`, `Bucket`, `Secret`, `Pasteboard`) e nomes da
  Foundation (`FileManager`, `Process`, `ProcessInfo`, `JSONDecoder`).
- **Rótulos no nome do método**, no lugar de rótulos de argumento:
  `contents_of_directory(at_path)`, `create_file_without_overwriting`, `move_item_without_overwriting`.
- **`guard!(condição, else: erro)`**: saída antecipada com erro, como o `guard … else` da Swift.
- **`// MARK: -`** para dividir seções dentro de um arquivo.
- **Nomes por extenso** (`standard_input`, `termination_status`), sem abreviações.
- **Um erro, um caso**: toda falha possível é uma variante de `SboxError`, e o texto que o
  usuário vê está no `Display`, num lugar só. Para adicionar um erro, crie a variante e a
  mensagem juntas.

## Garantias (invariantes)

São as regras que o código mantém. Qualquer mudança que quebre uma delas é um bug, e
mudanças nessas áreas pedem teste em `tests/tests.sh`.

### Segredos

1. **Valor nunca na linha de comando de outro processo.** O `set` usa
   `sops set --value-stdin`, e o clipboard recebe o valor pelo stdin. Nada que um `ps`
   mostre contém segredo.
2. **Texto puro nunca no disco.** O `new` encripta o YAML inicial em memória
   (`sops encrypt … /dev/stdin` com `--filename-override` para casar com o `path_regex`) e
   só grava o ciphertext.
3. **Código 0 do sops não basta.** Depois de encriptar, o `sbox` confere se a saída tem o
   bloco `sops:`. Se não tiver, falha sem gravar (`SopsReturnedPlaintext`).
4. **Bucket em texto puro é recusado** por todos os comandos que leem ou escrevem valores
   (`summarize_requiring_encryption`). A única saída oferecida é o `sbox encrypt`.
5. **O clipboard é limpo só se ainda contiver o segredo.** Um `sh` solto guarda o
   `sha256sum` do valor, espera o tempo pedido e só limpa se o hash do conteúdo atual for igual.
6. **O terminal nunca fica sem eco.** O `EchoGuard` religa o eco no `Drop`, e um handler de
   `SIGINT` religa antes de sair com 130.

### Arquivos

7. **Nunca sobrescrever.** Criar = gravar num temporário (`0600`, `create_new`), `fsync` e
   publicar com `link()`, que falha se o destino existir. Renomear = `link()` + `unlink()`.
   Nenhum caminho de código usa `rename()` sobre um destino existente.
8. **Nunca deixar arquivo vazio ou pela metade.** Se qualquer passo da criação falhar, o
   destino não aparece e o temporário é removido.

### Processos

9. **Todo código de saída do sops é conferido**, e o stderr dele é repassado no erro. Falha
   silenciosa aqui significaria segredo perdido. A exceção documentada é o `sops edit`
   devolver 200 (arquivo não alterado).
10. **O diretório de trabalho é o cofre**, porque o sops procura o `.sops.yaml` a partir do
    arquivo. O comando do `run` volta ao diretório original do usuário antes do `exec`.
11. **O `run` usa `exec`**: o `sbox` é substituído pelo comando, sem processo intermediário
    segurando os segredos em memória.

### Leitura sem decriptar

12. `ls` e `show` (sem `-r`) leem **só os nomes das chaves** direto do YAML, porque o sops
    não encripta chaves. Isso não precisa da chave privada nem chama o sops. O parser
    considera chave toda linha de primeiro nível (sem indentação, sem `#`, sem `-`) antes de
    `:`, inclusive com aspas.

## Fluxo de um comando: `sbox set pessoal TOKEN`

1. `Invocation::parse` → `Command::SetSecret { bucket: "pessoal", key: "TOKEN", value: None }`.
2. `Vault::locate` acha `~/secrets` e confere o `.sops.yaml`.
3. `Sbox::open` guarda o diretório atual e entra no cofre.
4. `existing_bucket("pessoal")` → `pessoal.enc.yaml`, que precisa existir.
5. `summarize_requiring_encryption()` lê o YAML e recusa texto puro. A lista de chaves
   decide se a mensagem final diz "criado" ou "atualizado".
6. O valor vem do argumento, da pergunta escondida (`/dev/tty`, duas vezes) ou do stdin.
7. `Sops::set` roda `sops set --value-stdin pessoal.enc.yaml '["TOKEN"]'`, com o valor
   codificado em JSON no stdin.
8. Código ≠ 0 → `SboxError::SopsFailed` com o stderr do sops. Sucesso → `✓ TOKEN criado em pessoal`.

## Testes

A suíte em `tests/tests.sh` é **de ponta a ponta**: compila o binário de release, cria
cofres temporários (fora de `~/secrets`) e roda o `sops` de verdade. Ela cobre:

- todos os comandos, casos de sucesso e de erro;
- valores difíceis (aspas, barras, quebras de linha, tabs, acentos, emoji, CJK, espaços nas
  pontas, coisas que parecem JSON, YAML ou `ENC[…]`);
- falhas do sops simuladas com `SOPS_BIN=false`, `SOPS_BIN=true` e um sops falso que
  devolve JSON arbitrário;
- clipboard, quando há sessão gráfica e `xclip`;
- interoperabilidade com outro binário, se passado como segundo argumento.

No CI, a suíte roda com uma chave age descartável, gerada a cada execução
(veja `.github/workflows/ci.yml`).

## Decisões e alternativas descartadas

| decisão | por quê | alternativa descartada |
|---|---|---|
| chamar o binário `sops` | reaproveita a criptografia auditada e o `.sops.yaml` do usuário | reimplementar o formato sops em Rust |
| sem `clap`/`serde` | binário pequeno, compilação rápida, cadeia de suprimentos mínima | `clap` para argumentos, `serde_json` para JSON |
| só `CHAVE: valor` de primeiro nível | modelo mental simples, mapeia direto para variáveis de ambiente | suporte a caminhos aninhados |
| `link()` em vez de `rename()` | `rename()` sobrescreve o destino em silêncio | `rename()` com checagem prévia (tem corrida) |
| confirmação pelo `/dev/tty` | funciona mesmo com stdin redirecionado; sem terminal, a resposta é "não" | ler a confirmação do stdin |
| mensagens no stderr | o stdout fica só com dados, seguro para pipes | tudo no stdout |
