# sops_box — `sbox`

CLI em Rust para gerenciar o cofre sops (`~/secrets`) sem digitar `sops` na mão.
Só dois conceitos: **bucket** = arquivo `*.enc.yaml`, **secret** = `CHAVE: valor` dentro dele.

```sh
make install          # compila e instala em ~/.local/bin/sbox
make test             # bateria de ponta a ponta (sops real, cofres temporários)
sbox --help
```

```sh
sbox ls                          # buckets, contagem de chaves, aviso de texto puro
sbox new pessoal
sbox set pessoal GITHUB_TOKEN    # pergunta o valor escondido (2x)
echo -n "$X" | sbox set pessoal X
sbox show pessoal                # chaves mascaradas (nem decripta)
sbox show pessoal -r             # revela
sbox get pessoal GITHUB_TOKEN -c # copia e limpa o clipboard em 45s
sbox run pessoal -- npm start    # secrets como variáveis de ambiente
sbox rm pessoal X / mv / del / encrypt / edit
```

## Mapa do código

Escrito para ler como Swift com Foundation: tipos com nome de domínio,
métodos com rótulos no nome (`contents_of_directory(at_path)`) e `guard!`.

| arquivo | o que é |
|---|---|
| `main.rs` | entrada: lê a linha de comando, acha o cofre, despacha |
| `command_line.rs` | `argv` → `enum Command` tipado (+ texto do `--help`) |
| `sbox.rs` | o que cada comando faz |
| `vault.rs` | `Vault`: o diretório do cofre e seus buckets |
| `bucket.rs` | `Bucket`, `BucketSummary` (chaves lidas sem decriptar), `Secret` |
| `sops.rs` | `Sops`: decrypt / set / unset / encrypt / edit |
| `json.rs` | `JSONEncoder` / `JSONDecoder` mínimos para falar com o sops |
| `process.rs` | `Process`, `ProcessResult`, `ProcessInfo` |
| `file_manager.rs` | `FileManager`: arquivos, sem nunca sobrescrever |
| `pasteboard.rs` | `Pasteboard::general()`: wl-copy / xclip com limpeza automática |
| `console.rs` | cores, mensagens, pergunta escondida e s/N pelo `/dev/tty` |
| `error.rs` | `enum SboxError` com todas as mensagens, e o `guard!` |

## Regras que o código garante

- **Valor nunca na linha de comando do sops**: `sops set --value-stdin`.
- **Plaintext nunca no disco**: bucket novo é encriptado em memória; o ciphertext vai para
  um temporário, `fsync`, e é publicado com `link()` (nunca sobrescreve, nunca deixa arquivo vazio).
- **Código de saída do sops sempre conferido**, stderr dele repassado no erro.
- Nomes das chaves são lidos direto do YAML (o sops só encripta valores), então `ls` e
  `show` sem `-r` não decriptam nada.
- Tudo roda com o diretório atual = cofre, porque o sops procura o `.sops.yaml` a partir do arquivo.

## Variáveis

| | |
|---|---|
| `SOPS_BOX_DIR` | cofre (padrão `~/secrets`; ou `--dir`) |
| `SOPS_BIN` | binário do sops (padrão: `sops` no PATH) |
| `NO_COLOR` | desliga cores |

## Licença

Copyright (C) 2026 Gabriel Vieira Soriano Aderaldo

Este programa é software livre: você pode redistribuí-lo e/ou modificá-lo sob os termos da
**GNU Affero General Public License**, versão 3 ou (a seu critério) qualquer versão posterior,
conforme publicada pela Free Software Foundation. Veja [`LICENSE`](LICENSE).

Em resumo: quem distribuir o `sbox`, uma versão modificada dele, ou oferecê-lo como serviço
pela rede, precisa disponibilizar o código-fonte completo sob a mesma licença.

### Terceiros

| componente | licença | como é usado |
|---|---|---|
| [`libc`](https://github.com/rust-lang/libc) | MIT OR Apache-2.0 | crate compilado no binário |
| [`sops`](https://github.com/getsops/sops) | MPL-2.0 | executado como programa externo, não distribuído |
| `wl-copy` / `xclip` | GPL-3.0 / MIT | executados como programas externos, não distribuídos |
