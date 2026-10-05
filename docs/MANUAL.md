# Manual do `sbox`

Referência completa de uso do `sbox` 0.1. Para uma visão rápida, veja o [README](../README.md);
para entender o código, a [arquitetura](ARCHITECTURE.md).

## Sumário

1. [Conceitos](#1-conceitos)
2. [Requisitos](#2-requisitos)
3. [Instalação](#3-instalação)
4. [Preparando o cofre](#4-preparando-o-cofre)
5. [Sintaxe geral](#5-sintaxe-geral)
6. [Comandos de bucket](#6-comandos-de-bucket)
7. [Comandos de secret](#7-comandos-de-secret)
8. [Variáveis de ambiente](#8-variáveis-de-ambiente)
9. [Códigos de saída](#9-códigos-de-saída)
10. [Receitas](#10-receitas)
11. [Solução de problemas](#11-solução-de-problemas)
12. [Limitações conhecidas](#12-limitações-conhecidas)

---

## 1. Conceitos

O `sbox` é uma camada fina sobre o [sops](https://github.com/getsops/sops). Ele não tem
criptografia própria: toda encriptação e decriptação é feita pelo binário `sops`, com as
chaves que o seu `.sops.yaml` define (normalmente [age](https://github.com/FiloSottile/age)).

| termo | o que é |
|---|---|
| **cofre** | um diretório com um `.sops.yaml` na raiz. Padrão: `~/secrets` |
| **bucket** | um arquivo `*.enc.yaml` (ou `*.enc.yml`) dentro do cofre |
| **secret** | uma linha `CHAVE: valor` de primeiro nível dentro de um bucket |

Exemplo de cofre:

```text
~/secrets/
├── .sops.yaml            ← regras de encriptação do sops
├── pessoal.enc.yaml      ← bucket "pessoal"
├── trabalho.enc.yaml     ← bucket "trabalho"
└── .rascunho.enc.yaml    ← bucket oculto (só aparece com `ls -a`)
```

Como o sops só encripta os **valores**, os **nomes das chaves** ficam em texto claro no
arquivo. Por isso `sbox ls` e `sbox show` (sem `-r`) funcionam sem decriptar nada e sem
precisar da chave privada.

## 2. Requisitos

| requisito | para quê | observação |
|---|---|---|
| Linux | sistema suportado | usa `/dev/tty`, `termios` e `hard_link` |
| [sops](https://github.com/getsops/sops) | toda a criptografia | precisa dos subcomandos `decrypt`, `encrypt`, `set --value-stdin`, `unset` e `edit`. Testado com o 3.13.3 |
| uma chave (ex.: [age](https://github.com/FiloSottile/age)) | o sops decriptar | o sops procura em `~/.config/sops/age/keys.txt` ou `SOPS_AGE_KEY_FILE` |
| `wl-copy`/`wl-paste` (Wayland) ou `xclip` (X11) | só para `get -c` | escolhido pela presença de `WAYLAND_DISPLAY` |
| `sh` e `sha256sum` | limpeza automática do clipboard | presentes em qualquer distribuição comum |
| Rust ≥ 1.88 | só para compilar | via [rustup](https://rustup.rs) |

## 3. Instalação

### A partir do código-fonte

```sh
git clone https://github.com/GabrielAderaldo/sops_box.git
cd sops_box
make install                 # compila em modo release e instala em ~/.local/bin/sbox
```

Para instalar em outro lugar, use `PREFIX`:

```sh
sudo make install PREFIX=/usr/local      # → /usr/local/bin/sbox
```

Garanta que `~/.local/bin` está no seu `PATH`. Para remover:

```sh
make uninstall               # (com o mesmo PREFIX usado na instalação)
```

### Binários prontos

Cada versão publicada em [Releases](https://github.com/GabrielAderaldo/sops_box/releases)
traz um binário Linux x86_64 e o arquivo `.sha256` correspondente. Confira antes de usar:

```sh
sha256sum -c sbox-*-x86_64-linux.tar.gz.sha256
tar -xzf sbox-*-x86_64-linux.tar.gz
install -Dm755 sbox ~/.local/bin/sbox
```

### Conferindo

```sh
sbox --version
sbox --help
```

## 4. Preparando o cofre

Se você ainda não usa sops, este é o caminho mínimo com age:

```sh
# 1. gerar uma chave age (guarde bem este arquivo: sem ele, nada se decripta)
mkdir -p ~/.config/sops/age
age-keygen -o ~/.config/sops/age/keys.txt
grep '^# public key:' ~/.config/sops/age/keys.txt     # → age1...

# 2. criar o cofre com as regras do sops
mkdir -p ~/secrets && cd ~/secrets
cat > .sops.yaml <<'YAML'
creation_rules:
  - path_regex: \.enc\.(yaml|yml)$
    age: age1SUA_CHAVE_PUBLICA_AQUI
YAML

# 3. primeiro bucket
sbox new pessoal
```

Para várias máquinas, coloque uma chave pública por máquina no `.sops.yaml` (lista em
`key_groups`) e rode `sops updatekeys <arquivo>` nos buckets existentes depois de adicionar
uma chave nova.

> **Dica:** o cofre pode (e costuma) ser um repositório git. Os buckets estão encriptados,
> então podem ser commitados. **Nunca** commite o `keys.txt`.

## 5. Sintaxe geral

```text
sbox [--dir <cofre>] <comando> [argumentos] [opções]
```

- **`<bucket>`** aceita o nome (`pessoal`) ou o nome do arquivo (`pessoal.enc.yaml`).
  Nomes não podem ser vazios, conter `/` nem começar com `-`. Um nome terminado em
  `.yaml`, `.yml`, `.json` ou `.env` sem o `.enc` é recusado, para evitar criar um arquivo
  que o `.sops.yaml` não encriptaria.
- **Flags curtas e longas** são equivalentes e podem vir em qualquer posição:

  | curta | longa | usada em |
  |---|---|---|
  | `-a` | `--all` | `ls` |
  | `-r` | `--reveal` | `show` |
  | `-c` | `--copy` | `get` |
  | `-y` | `--yes` | `del`, `rm` |
  | `-d <dir>` | `--dir <dir>` / `--dir=<dir>` | qualquer comando |
  | | `--clear <seg>` / `--clear=<seg>` | `get -c` |
  | `-h` | `--help` | qualquer lugar |
  | `-V` | `--version` | qualquer lugar |

- **`--`** encerra as opções. Tudo depois dele é repassado literalmente (usado pelo `run`).
- Flag desconhecida, ou flag que não pertence ao comando, é erro. O `sbox` não ignora
  argumentos em silêncio.
- **Mensagens** (sucesso, avisos, erros) vão para o **stderr**. Só os **dados** (listas,
  valores) vão para o **stdout**, então pipes e redirecionamentos recebem apenas o que interessa.

### Localização do cofre

Nesta ordem de prioridade:

1. `--dir <cofre>`
2. variável `SOPS_BOX_DIR`
3. `~/secrets`

O diretório precisa ter um `.sops.yaml`. Se não tiver, o comando falha com
`… não parece um cofre sops (falta o .sops.yaml)`.

Todos os comandos rodam com o diretório atual = cofre, porque o sops procura o
`.sops.yaml` a partir do arquivo. A única exceção é o comando do `run`, que roda no
diretório de onde você chamou o `sbox`.

---

## 6. Comandos de bucket

### `sbox ls [-a]`

Lista os buckets em ordem alfabética, com a quantidade de chaves. Não decripta nada.

```console
$ sbox ls
  pessoal   3 chaves
  trabalho  1 chave
  velho     2 chaves  ⚠ texto puro — sbox encrypt velho
```

- `-a`, `--all`: inclui buckets ocultos (arquivos que começam com `.`).
- Um bucket sem o bloco `sops:` é marcado como **texto puro**, com a sugestão de como
  corrigir.
- Em cofre vazio, mostra como criar o primeiro bucket.
- Alias: `sbox list`.

### `sbox new <bucket>`

Cria um bucket vazio, já encriptado.

```console
$ sbox new pessoal
✓ bucket pessoal criado — adicione com: sbox set pessoal <CHAVE>
```

- O conteúdo inicial é encriptado **em memória** pelo sops. O texto puro nunca é gravado
  em disco.
- O arquivo só aparece depois de gravado e sincronizado (`fsync`). Nunca fica arquivo vazio
  ou pela metade.
- Falha se o bucket já existir.
- Falha se o sops terminar sem erro mas não devolver um arquivo encriptado (por exemplo, se
  o `path_regex` do `.sops.yaml` não casar com o nome). Nesse caso, nada é gravado.

### `sbox mv <bucket> <novo-nome>`

Renomeia um bucket.

```console
$ sbox mv trabalho empresa
✓ trabalho → empresa
```

- **Nunca sobrescreve**: se `<novo-nome>` já existir, falha e não mexe em nada.

### `sbox del <bucket> [-y]`

Apaga um bucket inteiro.

```console
$ sbox del velho
Apagar o bucket velho com 2 chave(s)? [s/N] s
✓ bucket velho apagado (se já estava commitado, o git ainda tem)
```

- Pede confirmação pelo terminal (`/dev/tty`). Aceita `s`, `sim`, `y` ou `yes`.
  Qualquer outra resposta cancela.
- **Sem terminal** (em scripts, CI ou `</dev/null`), a resposta é **não**. Use `-y` para
  confirmar de forma explícita.

### `sbox encrypt <bucket>`

Encripta, no próprio arquivo, um bucket que está em texto puro.

```console
$ sbox encrypt velho
✓ velho encriptado
```

- Se o bucket já estiver encriptado, apenas avisa e sai com sucesso.
- Útil quando alguém criou um `*.enc.yaml` à mão. Os outros comandos de escrita recusam
  buckets em texto puro até que sejam encriptados.

### `sbox edit <bucket>`

Abre o bucket decriptado no seu `$EDITOR`, via `sops edit`. Ao salvar e fechar, o sops
encripta de novo.

- Exige bucket encriptado.
- Fechar sem alterar não é erro (o sops sai com código 200 e o `sbox` trata isso como sucesso).
- Útil para editar várias chaves de uma vez.

---

## 7. Comandos de secret

### `sbox show <bucket> [-r]`

Lista as chaves de um bucket.

```console
$ sbox show pessoal
  GITHUB_TOKEN  ••••••••
  NPM_TOKEN     ••••••••

$ sbox show pessoal -r
  GITHUB_TOKEN  ghp_xxxxxxxxxxxx
  NPM_TOKEN     npm_yyyyyyyyyyyy
```

- **Sem `-r`**, não decripta: lê só os nomes das chaves direto do YAML.
- **Com `-r`** (`--reveal`), decripta e mostra os valores. Valores com várias linhas aparecem
  numa linha só, com `⏎` no lugar das quebras, para não desalinhar a tabela. Para o valor
  original, use `get`.
- Em bucket em texto puro, sem `-r`, avisa. Com `-r`, recusa.

### `sbox get <bucket> <CHAVE> [-c] [--clear <seg>]`

Imprime o valor de uma chave.

```console
$ sbox get pessoal GITHUB_TOKEN
ghp_xxxxxxxxxxxx

$ export TOKEN=$(sbox get pessoal GITHUB_TOKEN)

$ sbox get pessoal GITHUB_TOKEN -c
✓ GITHUB_TOKEN copiado (limpa em 45s)
```

- **No terminal**, imprime o valor seguido de uma quebra de linha.
- **Em pipe ou redirecionamento**, imprime o valor **exato**, sem `\n` extra. Quebras de
  linha e espaços nas pontas saem byte a byte como foram gravados.
- `-c`, `--copy`: copia para a área de transferência em vez de imprimir.
  - Depois de 45 segundos, limpa a área de transferência, **mas só se o conteúdo ainda for
    o mesmo**. Se você copiou outra coisa nesse meio-tempo, ela não é apagada.
  - `--clear <seg>` muda o tempo. `--clear 0` desliga a limpeza.
  - O valor é passado ao `wl-copy`/`xclip` pelo stdin, nunca como argumento, então não
    aparece no `ps`.
- Falha se a chave não existir.

### `sbox set <bucket> <CHAVE> [valor]`

Cria ou atualiza uma chave.

```console
$ sbox set pessoal GITHUB_TOKEN
Valor de GITHUB_TOKEN:
Repita:
✓ GITHUB_TOKEN criado em pessoal

$ echo -n "$TOKEN" | sbox set pessoal GITHUB_TOKEN
✓ GITHUB_TOKEN atualizado em pessoal

$ sbox set pessoal CERT < certificado.pem
✓ CERT criado em pessoal
```

O valor vem de uma destas fontes, nesta ordem:

1. **Argumento** `[valor]`. Funciona, mas **evite**: o valor fica no histórico do shell e
   aparece para outros processos via `ps` enquanto o comando roda.
2. **Pergunta escondida**, se o stdin for um terminal. O valor é pedido duas vezes, sem eco,
   para pegar erro de digitação. Valor vazio ou valores diferentes cancelam sem gravar.
   Um Ctrl-C no meio da pergunta religa o eco do terminal antes de sair.
3. **stdin inteiro**, se ele vier de um pipe ou arquivo. Só o último `\n` é removido (aquele
   que o `echo` acrescenta). O resto é preservado.

Detalhes:

- O valor chega ao sops pelo **stdin** (`sops set --value-stdin`), nunca pela linha de comando.
- O nome da chave não pode ser vazio nem conter quebra de linha. Fora isso, qualquer texto
  vale, inclusive espaços, aspas e acentos.
- Exige bucket encriptado.

### `sbox rm <bucket> <CHAVE> [-y]`

Remove uma chave.

```console
$ sbox rm pessoal NPM_TOKEN
Remover NPM_TOKEN de pessoal? [s/N] s
✓ NPM_TOKEN removido de pessoal
```

- Pede confirmação como o `del`: sem terminal, a resposta é **não**, a menos que venha `-y`.
- Falha se a chave não existir.

### `sbox run <bucket> -- <comando> [args...]`

Executa um comando com todos os secrets do bucket como **variáveis de ambiente**.

```console
$ sbox run pessoal -- npm publish
$ sbox run trabalho -- docker compose up
$ sbox run pessoal -- sh -c 'curl -H "Authorization: Bearer $GITHUB_TOKEN" https://api.github.com/user'
```

- O `sbox` é **substituído** pelo comando (`exec`). Não fica nenhum processo intermediário,
  e o código de saída, os sinais e o terminal são os do próprio comando.
- O comando roda no **diretório de onde você chamou** o `sbox`, não no cofre.
- As variáveis do bucket somam-se às do ambiente atual e têm prioridade sobre elas.
- Todas as chaves precisam servir como nome de variável de ambiente: não vazias, sem `=` e
  sem byte nulo. Se alguma não servir, nada é executado.
- O `--` é obrigatório.
- Lembre que expansões como `$GITHUB_TOKEN` na linha de comando são feitas pelo **seu**
  shell, antes de o `sbox` rodar. Para usar a variável dentro do comando, passe por um
  `sh -c '…'` com aspas simples, como no último exemplo.

---

## 8. Variáveis de ambiente

| variável | efeito | padrão |
|---|---|---|
| `SOPS_BOX_DIR` | diretório do cofre (perde para `--dir`) | `~/secrets` |
| `SOPS_BIN` | binário do sops a usar | `sops` do `PATH` |
| `NO_COLOR` | qualquer valor desliga as cores ([no-color.org](https://no-color.org)) | cores ligadas em terminal |
| `WAYLAND_DISPLAY` | presente → `wl-copy`; ausente → `xclip` | definido pela sessão gráfica |
| `EDITOR` | editor do `sbox edit` (lido pelo sops) | o padrão do sops |
| `SOPS_AGE_KEY_FILE` e afins | onde o sops procura a chave | ver a documentação do sops |

As cores só aparecem quando a saída é um terminal. Em pipe ou arquivo, a saída é sempre texto puro.

## 9. Códigos de saída

| código | quando |
|---|---|
| `0` | sucesso, inclusive `edit` sem alterações e `encrypt` em bucket já encriptado |
| `1` | qualquer erro: argumentos, cofre, bucket, chave, falha do sops, cancelamento |
| `130` | Ctrl-C durante uma pergunta de valor escondido |
| *o do comando* | no `run`, depois que o comando começa (o `sbox` deixa de existir) |

Toda mensagem de erro começa com `erro:` no stderr. Quando o sops falha, o stderr dele é
repassado na mensagem, junto com o código de saída.

## 10. Receitas

**Exportar um secret numa sessão de shell**

```sh
export GITHUB_TOKEN=$(sbox get pessoal GITHUB_TOKEN)
```

**Copiar de um bucket para outro**

```sh
sbox get pessoal NPM_TOKEN | sbox set trabalho NPM_TOKEN
```

O `get` em pipe sai exato e o `set` lê o stdin inteiro, então o valor chega idêntico,
inclusive com várias linhas.

**Guardar um arquivo inteiro (certificado, chave SSH)**

```sh
sbox set pessoal SSH_KEY < ~/.ssh/id_ed25519
sbox get pessoal SSH_KEY > /tmp/id && chmod 600 /tmp/id
```

**Usar outro cofre pontualmente**

```sh
sbox --dir ~/projetos/app/secrets ls
SOPS_BOX_DIR=~/projetos/app/secrets sbox run dev -- cargo run
```

**Scripts sem interação**

```sh
printf '%s' "$VALOR" | sbox set ci TOKEN
sbox rm ci TOKEN_ANTIGO -y
sbox del temporario -y
```

**Ver o que mudou num cofre versionado em git**

```sh
git -C ~/secrets diff --stat     # quais buckets mudaram
sbox show pessoal                # quais chaves existem (sem decriptar)
```

## 11. Solução de problemas

| mensagem | causa provável | o que fazer |
|---|---|---|
| `não sei onde fica o cofre` | sem `--dir`, sem `SOPS_BOX_DIR` e sem `HOME` | defina `SOPS_BOX_DIR` ou use `--dir` |
| `… não parece um cofre sops (falta o .sops.yaml)` | diretório errado ou cofre não preparado | veja a [seção 4](#4-preparando-o-cofre) |
| `bucket 'x' não existe` | erro de digitação ou cofre errado | `sbox ls` (e `sbox ls -a` para os ocultos) |
| `'x' está em TEXTO PURO` | arquivo `*.enc.yaml` criado sem o sops | `sbox encrypt x` |
| `sops decrypt falhou (código 128)` + mensagem do sops | a chave privada não está disponível | confira `~/.config/sops/age/keys.txt` ou `SOPS_AGE_KEY_FILE` |
| `o sops terminou sem erro mas não devolveu um arquivo encriptado` | `path_regex` do `.sops.yaml` não casa com `*.enc.yaml` | ajuste o `path_regex` |
| ``não consegui executar `sops` `` | sops fora do `PATH` | instale o sops ou aponte `SOPS_BIN` |
| `valores aninhados (mapas/listas) não são suportados` | bucket editado à mão com estrutura | o `sbox` só lida com `CHAVE: valor` de primeiro nível; use `sops` direto nesse arquivo |
| `'x' não serve como variável de ambiente` | chave com `=` no nome, no `run` | renomeie a chave: `get` → `set` com outro nome → `rm` |
| `sem terminal para perguntar` | `set` sem valor, sem terminal e com stdin fechado | passe o valor pelo stdin |
| `xclip falhou` / `wl-copy falhou` | ferramenta ausente ou sem sessão gráfica | instale `xclip` ou `wl-clipboard`; em SSH, use `get` sem `-c` |
| terminal ficou sem eco | (não deveria acontecer) | rode `stty sane` e [abra uma issue](https://github.com/GabrielAderaldo/sops_box/issues) |

## 12. Limitações conhecidas

- Só valores de **primeiro nível** (`CHAVE: valor`). Mapas e listas aninhados exigem o `sops` direto.
- Só buckets **YAML** (`.enc.yaml` / `.enc.yml`).
- `mv` usa `link()` + `unlink()`, então origem e destino precisam estar no mesmo sistema de
  arquivos. Dentro de um cofre, sempre estão.
- Área de transferência só via `wl-copy` (Wayland) ou `xclip` (X11). macOS (`pbcopy`) e
  terminais remotos (OSC 52) ainda não são suportados.
- Suporte oficial apenas a Linux.
