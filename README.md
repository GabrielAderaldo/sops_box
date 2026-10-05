# sops_box — `sbox`

[![CI](https://github.com/GabrielAderaldo/sops_box/actions/workflows/ci.yml/badge.svg)](https://github.com/GabrielAderaldo/sops_box/actions/workflows/ci.yml)
[![Licença: AGPL-3.0-or-later](https://img.shields.io/badge/licen%C3%A7a-AGPL--3.0--or--later-blue.svg)](LICENSE)
[![MSRV: 1.88](https://img.shields.io/badge/rust-1.88%2B-orange.svg)](https://www.rust-lang.org)
[![Contributor Covenant](https://img.shields.io/badge/Contributor%20Covenant-2.1-4baaaa.svg)](CODE_OF_CONDUCT.md)

CLI em Rust para gerenciar um cofre [sops](https://github.com/getsops/sops) (`~/secrets`)
sem digitar `sops` na mão.

Só dois conceitos: **bucket** = arquivo `*.enc.yaml`; **secret** = `CHAVE: valor` dentro dele.

```console
$ sbox ls
  pessoal   2 chaves
  trabalho  1 chave

$ sbox show pessoal
  GITHUB_TOKEN  ••••••••
  NPM_TOKEN     ••••••••

$ sbox get pessoal GITHUB_TOKEN -c
✓ GITHUB_TOKEN copiado (limpa em 45s)

$ sbox run pessoal -- npm publish
```

## Por que usar

- **Seguro por padrão.** O valor nunca aparece na linha de comando de outro processo,
  o texto puro nunca toca o disco, o clipboard se limpa sozinho e nenhum arquivo é
  sobrescrito ou deixado pela metade.
- **Sem nova criptografia.** Quem encripta é o próprio `sops`, com as chaves do seu
  `.sops.yaml` (age, PGP, KMS…). Seus arquivos continuam sendo arquivos sops comuns.
- **Feito para scripts.** Os dados vão para o stdout e as mensagens para o stderr. O `get`
  em pipe sai byte a byte, e as confirmações respondem "não" quando não há terminal.
- **Pequeno e auditável.** Um único binário, cerca de 1.800 linhas, com uma dependência
  (`libc`).

## Instalação

Requisitos: Linux, [sops](https://github.com/getsops/sops) no `PATH` e uma chave configurada
para ele (ex.: [age](https://github.com/FiloSottile/age)). Para compilar: Rust ≥ 1.88.

```sh
git clone https://github.com/GabrielAderaldo/sops_box.git
cd sops_box
make install          # compila e instala em ~/.local/bin/sbox
sbox --help
```

Ainda não tem um cofre? O [manual](docs/MANUAL.md#4-preparando-o-cofre) mostra como criar um
em quatro comandos.

## Uso rápido

```sh
sbox ls                          # buckets, contagem de chaves, aviso de texto puro
sbox new pessoal                 # cria um bucket já encriptado
sbox set pessoal GITHUB_TOKEN    # pergunta o valor escondido (2x)
echo -n "$X" | sbox set pessoal X
sbox show pessoal                # chaves mascaradas (nem decripta)
sbox show pessoal -r             # revela os valores
sbox get pessoal GITHUB_TOKEN    # imprime o valor
sbox get pessoal GITHUB_TOKEN -c # copia e limpa o clipboard em 45s
sbox run pessoal -- npm start    # secrets como variáveis de ambiente
sbox rm pessoal X                # remove uma chave (pede confirmação)
sbox mv pessoal casa             # renomeia, sem nunca sobrescrever
sbox del casa                    # apaga o bucket (pede confirmação)
sbox encrypt velho               # encripta um bucket em texto puro
sbox edit pessoal                # abre no $EDITOR via sops edit
```

| variável | efeito |
|---|---|
| `SOPS_BOX_DIR` | cofre (padrão `~/secrets`; ou `--dir`) |
| `SOPS_BIN` | binário do sops (padrão: `sops` no `PATH`) |
| `NO_COLOR` | desliga as cores |

## Documentação

| documento | para quem |
|---|---|
| [Manual](docs/MANUAL.md) | quem usa: todos os comandos, opções, receitas e solução de problemas |
| [Arquitetura](docs/ARCHITECTURE.md) | quem lê ou altera o código: módulos, garantias, decisões |
| [Contribuindo](CONTRIBUTING.md) | quem quer enviar uma mudança |
| [Segurança](SECURITY.md) | como reportar uma vulnerabilidade, e o modelo de ameaças |
| [Suporte](SUPPORT.md) | onde tirar dúvidas |
| [Changelog](CHANGELOG.md) | o que mudou em cada versão |

## Contribuindo

Contribuições são bem-vindas. Leia o [CONTRIBUTING](CONTRIBUTING.md) e o
[Código de Conduta](CODE_OF_CONDUCT.md). Em resumo:

```sh
make test                                   # suíte de ponta a ponta (sops real, cofres temporários)
cargo fmt --check && cargo clippy --all-targets -- -D warnings
```

Vulnerabilidades **não** devem ser abertas como issue pública. Veja o [SECURITY.md](SECURITY.md).

## Licença

Copyright (C) 2026 Gabriel Vieira Soriano Aderaldo

Este programa é software livre: você pode redistribuí-lo e/ou modificá-lo sob os termos da
**GNU Affero General Public License**, versão 3 ou (a seu critério) qualquer versão posterior,
conforme publicada pela Free Software Foundation. Ele é distribuído na esperança de ser útil,
mas **sem nenhuma garantia**. Veja [`LICENSE`](LICENSE).

Em resumo: quem distribuir o `sbox`, uma versão modificada dele, ou oferecê-lo como serviço
pela rede, precisa disponibilizar o código-fonte completo sob a mesma licença.

Cada arquivo de código traz um cabeçalho [SPDX](https://spdx.dev) com a licença e o
copyright, no padrão [REUSE](https://reuse.software).

### Terceiros

| componente | licença | como é usado |
|---|---|---|
| [`libc`](https://github.com/rust-lang/libc) | MIT OR Apache-2.0 | crate compilado no binário |
| [`sops`](https://github.com/getsops/sops) | MPL-2.0 | executado como programa externo, não distribuído |
| `wl-copy` / `xclip` | GPL-3.0 / MIT | executados como programas externos, não distribuídos |
