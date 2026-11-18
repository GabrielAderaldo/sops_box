# `sbox` para Windows — hipóteses

Rascunho para decidir **como** ter o `sbox` no Windows. Cada hipótese termina com o que
precisa ser verdade para ela valer e como testar.

## Decidido

- **Caminho: H1.** Port em C# (.NET 10, NativeAOT), aqui em `windows/`. As duas versões
  podem divergir em detalhes internos.
- **Contrato: a suíte em Perl ([`tests/*.t`](../tests)).** É a mesma para as duas versões.
  Ela só olha a linha de comando, stdin, stdout, stderr e o código de saída, então vale para
  qualquer implementação. Perl 5 em vez de Raku: já vem no Ubuntu, no macOS, nos runners
  Windows do GitHub (Strawberry Perl) e no Git Bash, e a suíte usa só módulos do core.
- **PR válido = suíte verde.** O CI roda a suíte contra o binário Rust (Linux) e, quando o
  port existir, contra o `sbox.exe` (Windows). Os dois jobs viram checagens obrigatórias
  na proteção da `main`.
- O que só existe numa plataforma fica atrás de `IS_WINDOWS`/`IS_LINUX`/`IS_MACOS` na suíte,
  sempre com o motivo. Ex.: `03-dialog.t` pula no Windows, porque a janela do Windows é uma
  API (CredUI) e não um programa que dê para trocar por um falso no `PATH`.

- **Esqueleto feito.** [`Sbox.csproj`](Sbox.csproj) (.NET 10, NativeAOT, sem NuGet, avisos
  como erro, versão própria `0.1.0`) e [`src/Program.cs`](src/Program.cs): `--version` e
  `--help` funcionam; qualquer outro comando sai com 1 e
  `erro: … ainda não implementado no Windows`. O [`global.json`](global.json) fixa o SDK 10.
- **[`tests.txt`](tests.txt) é a régua do port.** Lista os arquivos da suíte que já precisam
  passar contra o `sbox.exe`. O job "Testes de contrato (Windows)" do CI publica o binário
  (NativeAOT), cria um cofre com chave age descartável, roda esses arquivos de forma
  obrigatória e a suíte inteira só como relatório. Ao deixar um arquivo verde, ele entra na
  lista no mesmo commit; nunca sai dela.

- **Comandos portados:** `ls`, `new`, `mv`, `del`, `show` (com e sem `-r`), `set` (argumento,
  stdin ou pergunta escondida), `get` (sem `-c`), `rm` e `encrypt`. Verdes: `01-buckets.t`,
  `02-secrets.t`, `05-plaintext.t`, `06-errors.t`, `07-json.t` e `08-security.t`. O parser de
  argumentos está inteiro; o que ainda não veio (`run`, `edit`, `get -c`, `set --dialog`)
  sai com `erro: … ainda não implementado no Windows`.
- **Arquitetura em camadas** (decisão do dono; ver [Arquitetura](#arquitetura), abaixo).
- **T1, T2, T7 e "sem terminal" decididos, OneDrive e ACL testados** (detalhes em cada
  hipótese, abaixo).
- **Diferenças de propósito em relação ao Rust**, todas por causa do Windows:
  - o nome de bucket também recusa `\`, `:` (abriria um fluxo alternativo do NTFS: `a:b`
    gravaria dentro do arquivo `a`), os outros caracteres que o Windows não aceita em nome
    de arquivo e os nomes de dispositivo (`con`, `nul`, `com1`…);
  - a última linha do `--help` fala em `%USERPROFILE%\secrets` e `%SOPS_BIN%`;
  - a confirmação do `del`/`rm` sem `-y` e a pergunta escondida do `set` só acontecem
    quando o stdin é o console (ver T9); sem console, a s/N responde não e o `set` lê o stdin;
  - no `set` pelo stdin, uma linha só perde também o `\r` do `\r\n` (o `echo` do PowerShell
    e do cmd), e um BOM UTF-8 no começo é descartado (o PowerShell com `$OutputEncoding` em
    UTF-8 manda o BOM no pipe para programas nativos). Sem isso, o segredo ganharia um
    caractere invisível. Encontrado ao testar: `'x' | sbox set b K` chegava ao sops como
    `"﻿x"`.

Ambiente desta máquina (2026-11-17): .NET SDK 10 e 11 instalados; `cargo` não. `sops`
v3.13.3 e `age` v1.3.2 instalados pelo dono via winget (antes, baixados com checksum para uma
pasta temporária). A suíte local usa uma chave age descartável fora de `%USERPROFILE%\secrets`
(`SOPS_AGE_KEY_FILE` e `SBOX_TEST_SOPS_CONFIG`).

### Rodar localmente

O NativeAOT precisa do Visual Studio (ou Build Tools) com a carga **Desenvolvimento para
desktop com C++**. No VS 2026, o `vcvarsall.bat` chama o `vswhere.exe` sem caminho, e o erro
dele entra no comando do linker (`'vswhere.exe' não é reconhecido…` + `MSB3073`). A correção
é pôr a pasta do instalador no PATH do usuário, uma vez:

```powershell
$dir = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
[Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path', 'User') + ";$dir", 'User')
```

Rode o `publish` pelo PowerShell ou cmd, não pelo Git Bash: o bash descarta a variável
`ProgramFiles(x86)`, que o .NET usa para achar o Visual Studio.

```powershell
dotnet build windows/Sbox.csproj                      # rápido, sem AOT
dotnet publish windows/Sbox.csproj -c Release -r win-x64 -o out   # sbox.exe nativo
$env:SBOX_BIN = "$PWD\out\sbox.exe"   # ou windows\bin\Debug\net10.0\sbox.exe
$env:SBOX_EXPECTED_VERSION = '0.1.0'
prove -v tests/06-errors.t                            # um arquivo só
prove tests/                                          # a suíte inteira
```

A suíte precisa de um `.sops.yaml` em `%USERPROFILE%\secrets` (ou `SBOX_TEST_SOPS_CONFIG`)
e, para os testes que gravam de verdade, de `sops` e `age` no PATH.

### Arquitetura

O Rust é um crate plano; aqui o código fica em camadas, e as dependências só apontam para
dentro. Os nomes de domínio continuam os do Rust, para a leitura lado a lado.

```text
  argv ──▶ Presentation ──────────────▶ Application ──────────▶ Domain
           CommandLine (argv → Command)  um caso de uso por      BucketName, Bucket,
           Cli (fluxo, código de saída)  comando: OpenVault,     BucketSummary, Vault,
           Presenter (único que formata) ListBuckets, ShowBucket, Ciphertext, SecretKey,
           TerminalConfirmation,         CreateBucket, MoveBucket, SecretValue, Secret,
           ArgumentSecretInput,          DeleteBucket, SetSecret, PromptRules, SboxError
           TerminalSecretInput           GetSecret, RevealBucket, (sem I/O)
                     │                   RemoveSecret, EncryptBucket
                     │                         │
                     │                   Ports: IFileSystem, ISops, IConfirmation,
                     │                   ISecretInput, IEnvironment, ITerminal
                     │                         ▲
  Program.cs ──▶ Infrastructure ───────────────┘ (implementa as portas)
  (monta tudo    WindowsFileSystem, SopsProcess, Json (encoder/decoder à mão),
   à mão)        Win32Terminal, ProcessEnvironment, ProcessRunner, Interop/Kernel32
```

| camada | o que tem | regra |
|---|---|---|
| `Domain/` | value objects que validam ao nascer (`BucketName`, `SecretKey`, `SecretValue` com as regras de cada origem do valor), entidades (`Bucket`, `Vault`, `Secret`), o parser do YAML sem decriptar (`BucketSummary`), a conferência do bloco `sops:` (`Ciphertext`), as regras puras das perguntas no console (`PromptRules`) e todas as mensagens (`SboxError`) | sem I/O, sem console, sem processos |
| `Application/` | um caso de uso por comando e as portas (`Ports/`) | recebe portas, devolve resultados tipados, nunca escreve no console |
| `Presentation/` | `CommandLine` (argv → `Command`), `Cli` (fluxo e código de saída), `Presenter`, `TerminalConfirmation` | única camada que formata texto e cores |
| `Infrastructure/` | os adaptadores das portas e o P/Invoke (`Interop/`) | conversa com o Windows |
| `Program.cs` | composition root | monta tudo à mão, sem container de DI (NativeAOT, sem reflection) |

O `ITerminal` fica em `Application/Ports` mesmo sem caso de uso nenhum usá-lo: assim a
Presentation (que formata) e a Infrastructure (que fala com o console) dependem só para
dentro, e não uma da outra. A confirmação recebe um pedido tipado
(`ConfirmationRequest.DeleteBucket(nome, chaves)`), e o texto da pergunta sai do `Presenter`.

## O que precisa continuar valendo

As garantias do [`ARCHITECTURE.md`](../docs/ARCHITECTURE.md#garantias-invariantes). As que
dependem de Unix hoje:

| # | garantia | depende de | no Windows |
|---|---|---|---|
| 2 | texto puro nunca no disco | `sops encrypt … /dev/stdin` | **mantida**: `sops encrypt` sem arquivo lê o stdin (T1) |
| 5 | clipboard limpo só se ainda tiver o segredo | `sh` + `sha256sum` soltos | a fazer (T3) |
| 6 | terminal nunca fica sem eco | `/dev/tty`, `termios`, `SIGINT` | **mantida**: eco desligado com `SetConsoleMode`, religado no `finally` e no Ctrl+C (T4) |
| 7 | nunca sobrescrever | `link()` + temporário `0600` | **mantida**: `File.Move` sem sobrescrever e temporário com ACL só do usuário (T2) |
| 11 | `run` sem processo intermediário | `exec` | não dá igual (T5) |

O resto (argumentos, leitura do YAML, JSON, chamadas ao sops, mensagens) é lógica pura.

---

## Parte 1 — Hipóteses de caminho

### H1. Port em C# (.NET 10, NativeAOT), código separado em `windows/`

Reescrever os ~2.000 linhas em C#, um `sbox.exe` único e sem runtime, **sem NuGet**
(só BCL + P/Invoke), mantendo os mesmos nomes de tipos (`Vault`, `Bucket`, `Sops`,
`FileManager`, `Pasteboard`…) para a leitura lado a lado com o Rust.

- **A favor:** P/Invoke para Win32 é natural em C#; NativeAOT dá binário único;
  quem só conhece C# consegue contribuir na versão Windows.
- **Contra:** duas bases para manter. Todo comando, flag ou mensagem nova tem que ser
  feito duas vezes, e as duas podem divergir.
- **Vale se:** o NativeAOT gerar um `.exe` pequeno e rápido, e a suíte de contrato
  conseguir rodar contra os dois binários.

### H2. Mesmo código Rust, com `#[cfg(windows)]` nos módulos de plataforma

O Rust compila para Windows. Parte já é portável: `fs::hard_link` usa `CreateHardLinkW`
e também falha se o destino existe. Só `console.rs`, `pasteboard.rs`, `dialog.rs`,
`process.rs` (o `exec`) e o `.mode(0o600)` do `file_manager.rs` precisariam de uma
variante Windows.

- **A favor:** uma base só; os testes já existem; menos de ~500 linhas novas.
- **Contra:** precisa da crate `windows-sys` ou de FFI à mão (o projeto evita dependências);
  não é C#.
- **Vale se:** a regra "só `libc`" aceitar uma exceção para Windows, ou se o FFI à mão
  ficar pequeno.

### H3. Módulo PowerShell em cima do `sops`

Um `sbox.psm1` com `Get-SboxSecret`, `Set-SboxSecret`, `Invoke-Sbox` etc. chamando o
`sops.exe`.

- **A favor:** zero compilação; é o "idioma" do Windows; pronto em um dia.
- **Contra:** comandos diferentes do `sbox` do Linux/macOS; difícil manter as garantias
  (o PowerShell guarda strings na memória e no histórico; clipboard sem as marcas de
  privacidade sem P/Invoke).
- **Vale se:** o objetivo for só uso pessoal rápido, não paridade.

### H4. H1 + extensão do SecretManagement (fase 2)

O mesmo núcleo em C# exposto também como *vault extension* do
`Microsoft.PowerShell.SecretManagement`. Aí `Get-Secret -Vault sbox GITHUB_TOKEN` lê do
mesmo cofre sops que o `sbox.exe`.

- **A favor:** integra com o que o Windows/PowerShell já usa; o cofre continua portátil.
- **Contra:** mais superfície; só faz sentido depois de H1 estar sólida.

**Escolhida:** H1, com a suíte de contrato em Perl rodando contra o `sbox.exe`.

---

## Parte 2 — Hipóteses técnicas (valem para H1 e H2)

Cada uma é um experimento pequeno, para fazer antes de escrever o port.

### T1. O `sops` no Windows encripta a partir do stdin *(bloqueante, garantia 2)*

`/dev/stdin` não existe no Windows. Testar com o `sops.exe`:
`sops encrypt --input-type yaml --output-type yaml --filename-override x.enc.yaml` com o
YAML no stdin, variando o último argumento (`-`, nenhum, `CON`, `\\.\pipe\…`).
**Se nenhum funcionar:** gravar o texto puro num arquivo temporário (garantia 2 cai) ou
criar o bucket por `sops set` em cima de um arquivo já encriptado vazio. Decidir antes
de começar.

**Resultado (2026-11-17, `sops-v3.13.3.amd64.exe`, PowerShell 5.1, NTFS):** omitir o
arquivo funciona, e a garantia 2 continua valendo.

| último argumento | resultado |
|---|---|
| nenhum | **código 0**, YAML encriptado com o bloco `sops:` no stdout |
| `-` | código 100: `cannot operate on non-existent file "…\-"` |
| `/dev/stdin` | código 100: `cannot operate on non-existent file "C:\dev\stdin"` |
| `CON` | trava esperando o teclado (lê o console, não o pipe) |

No código do sops (`cmd/sops/main.go` e `cmd/sops/encrypt.go` da tag v3.13.3), sem
argumento posicional o `encrypt` liga `ReadFromStdin` e faz `io.ReadAll(os.Stdin)`: o texto
fica na memória, nada de temporário. Nesse modo o `--filename-override` é obrigatório
(`must specify --filename-override when reading from stdin`), e é ele que casa com o
`path_regex` (T7). Conferido também: um YAML com `K: segredo` encriptado assim não tem
`segredo` no arquivo, e o `sops decrypt` devolve `{"K": "segredo"}`; stdin vazio dá
código 92 (`File cannot be completely empty`). O `sbox` continua conferindo o bloco
`sops:` na saída (garantia 3). Vale para o sops 3.10 em diante, quando o `encrypt` passou
a aceitar o stdin; a suíte e o CI usam o 3.13.3.

### T2. "Nunca sobrescrever" sobrevive (garantia 7)

`File.Move(origem, destino, overwrite: false)` (= `MoveFileExW` sem
`MOVEFILE_REPLACE_EXISTING`) e `CreateHardLinkW` falham se o destino existe.
Temporário com `FileMode.CreateNew`, `FileShare.None`, ACL só do usuário atual, e
`Flush(flushToDisk: true)` no lugar do `fsync`. Testar em NTFS e numa pasta do OneDrive
(o `%USERPROFILE%` costuma estar sincronizado).

**Decidido: `File.Move(temporário, destino, overwrite: false)`**, em
[`WindowsFileSystem.cs`](src/Infrastructure/WindowsFileSystem.cs). Criar = `.sbox-tmp-<pid>`
com `FileMode.CreateNew`, `FileShare.None` e ACL só do usuário (abaixo), gravar, `Flush(flushToDisk: true)` (`FlushFileBuffers`) e publicar com
`File.Move` sem sobrescrever; o temporário é apagado num `finally`, então some em qualquer
falha. Renomear (`mv`) = o mesmo `File.Move`. O `File.Move` vira `MoveFileExW` sem
`MOVEFILE_REPLACE_EXISTING`: falha com `ERROR_ALREADY_EXISTS` (vira `… já existe`) e, no
mesmo volume, é um rename atômico. Preferido ao `CreateHardLinkW` porque hard link não
existe em FAT/exFAT (pendrives) nem em alguns compartilhamentos de rede, e porque o
`File.Move` faz em um passo o que no Rust é `link()` + `unlink()`.
Testado em NTFS (suíte local e CI).

**ACL só do usuário (o `0600` do Rust): feito.** O temporário é criado por
`FileInfo.Create(FileMode.CreateNew, …, FileSecurity)` (o `FileSystemAclExtensions` do BCL),
com uma DACL **protegida** (não herda nada da pasta), dono = usuário atual e uma única regra:
o SID do usuário atual com controle total. A ACL vai no próprio `CreateFileW`
(`SECURITY_ATTRIBUTES`), então não há instante em que o arquivo exista com a ACL herdada.
Escolhido em vez de `CreateFileW` + SDDL por P/Invoke porque é só BCL e publica com NativeAOT
sem nenhum aviso de trimming/AOT; o custo é ~150 KB no `sbox.exe` e declarar
`[assembly: SupportedOSPlatform("windows")]` (CA1416) no `Program.cs`. Conferido com
`icacls`/`Get-Acl`, numa pasta cuja ACL herdada é `SISTEMA`, `Administradores` e o usuário:

| arquivo | ACL |
|---|---|
| pasta do cofre | `SISTEMA:(I)(OI)(CI)(F)`, `Administradores:(I)(OI)(CI)(F)`, `PC:(I)(OI)(CI)(F)` |
| `.sops.yaml` (copiado) | `SISTEMA:(I)(F)`, `Administradores:(I)(F)`, `PC:(I)(F)` |
| bucket do `sbox new` | `PC:(F)`, sem `(I)`; `AreAccessRulesProtected = True` |
| o mesmo bucket depois do `sbox mv` | `PC:(F)` (o `File.Move` no mesmo volume preserva o descritor) |

Nada de ACL estranha depois do `File.Move`. Consequências: `SISTEMA` e `Administradores`
perdem o acesso direto ao bucket (como o `root` no `0600`, um administrador ainda consegue
tomar posse); backup e antivírus que rodam como `SISTEMA` usam o privilégio de backup e não
são afetados.

**ACL ao regravar (conferido com `icacls`):** `sops set`, `sops unset` e `sops encrypt -i`
regravam o mesmo arquivo (truncam e escrevem), sem criar outro, então o descritor de
segurança fica. Um bucket do `sbox new` continua `PC:(F)` e protegido depois de `set`/`unset`
(pelo sops direto e pelo `sbox set` repetido). Um bucket em texto puro criado à mão, com a ACL
herdada da pasta, continua com ela depois do `sbox encrypt`, como no Unix, onde o
`encrypt -i` mantém o modo do arquivo. Possível melhoria, ainda não feita: o `sbox encrypt`
aplicar a ACL só do usuário depois de encriptar.

**OneDrive: testado (2026-11-17).** A máquina tem `%OneDrive%` (`C:\Users\PC\OneDrive`,
pasta com reparse point) e o serviço `OneDrive.Sync.Service` rodando. Numa pasta descartável
dentro dele (apagada no fim; a chave age ficou fora, no diretório temporário), com
`TMPDIR` apontando para lá para a suíte criar os cofres ali dentro:

- `01-buckets.t`: 21/21.
- À mão: 5 rodadas de `new` + `mv` seguidas, `ls`, 5 `del -y`: todas com código 0, nenhum
  `.sbox-tmp-*` largado, nenhum bloqueio do sync (`CreateNew`, `Flush(true)` e `File.Move`
  sem erro). O bucket publicado fica com atributo só `Archive` e ACL `PC:(F)`.

**Não testado:** bucket "somente online" (atributo `RecallOnDataAccess`). O `ls` lê o arquivo
inteiro, então nesse caso o Windows baixaria o conteúdo (precisa de rede) antes de mostrar as
chaves; sem rede, a leitura falharia com `não consegui abrir …`. Também não dá para garantir
como o OneDrive trata a DACL protegida ao sincronizar para outra máquina (ele não sincroniza
ACLs; o arquivo baixado em outro PC herda a pasta de lá).

### T3. Clipboard privado e com limpeza (garantia 5)

`SetClipboardData` com os formatos `ExcludeClipboardContentFromMonitorProcessing`,
`CanIncludeInClipboardHistory = 0` e `CanUploadToCloudClipboard = 0`. Verificar que o
valor **não** aparece no Win+V nem sincroniza. Limpeza: o próprio `sbox.exe` relançado
em segundo plano (`sbox __clear-clipboard`, hash SHA-256 pelo stdin), que só limpa se o
conteúdo atual ainda tiver o mesmo hash.

### T4. Pergunta escondida e terminal sempre restaurado (garantia 6)

Abrir `CONIN$` (equivalente ao `/dev/tty`: funciona com stdin redirecionado), desligar
`ENABLE_ECHO_INPUT` com `SetConsoleMode` e religar no `finally` e no `Console.CancelKeyPress`.
Sem console (ex.: agente de IA) → confirmações respondem "não", como hoje.

**Feito.** `sbox set <bucket> <CHAVE>` sem valor, com o stdin no console, pergunta
`Valor de CHAVE:` e `Repita:` sem eco ([`Win32Terminal.cs`](src/Infrastructure/Win32Terminal.cs)):
- abre `CONIN$`/`CONOUT$`, lê o modo de entrada e desliga só `ENABLE_ECHO_INPUT` (o modo de
  linha e o Ctrl+C continuam);
- religa no `finally`, e também no `Console.CancelKeyPress`, que roda em outra thread (o
  handle e o modo original ficam guardados sob um `Lock` para isso). O mesmo handler devolve
  o modo de saída que o sbox ligou para as cores;
- depois do Enter (que não foi ecoado), escreve `\n` no console;
- se não conseguir desligar o eco, não pergunta (segredo nenhum é lido com eco).

A regra de quando perguntar é a mesma da s/N (`PromptRules.CanAskInConsole`): com o stdin
redirecionado não há pergunta, e o valor vem do stdin. Diferença em relação ao Unix: lá a
pergunta é feita quando o stdin é um terminal, e o Ctrl+C sai com 130; no Windows o processo
termina com o código padrão do Ctrl+C (`0xC000013A`), já com o eco devolvido.
**Não verificado localmente:** digitar num console de verdade (roteiro no fim do T9).

### T5. `run` sem `exec` (garantia 11 — não dá para manter igual)

O Windows não tem `exec`. Proposta: processo filho com o ambiente montado, num Job
Object com `KILL_ON_JOB_CLOSE`; o `sbox` ignora o Ctrl+C (o filho recebe do console) e
sai com o código do filho. O segredo fica na memória do `sbox` enquanto o comando roda,
e strings do .NET não podem ser zeradas. **Documentar no `SECURITY.md`.**
Testar: código de saída repassado, Ctrl+C, `sbox run x -- cmd /c echo %TOKEN%`.

### T6. `set --dialog` com janela nativa

`CredUIPromptForWindowsCredentialsW` (janela do sistema, sem processo externo). Testar
se aceita só um campo de "senha" e se funciona sem console.

### T7. `path_regex` do `.sops.yaml` com caminhos do Windows

O sops casa o `path_regex` com o caminho do arquivo. Regras escritas com `/` podem não
casar com `C:\Users\…\secrets\x.enc.yaml`. Testar com o `.sops.yaml` do cofre real
antes de qualquer coisa; afeta a interoperabilidade entre máquinas.

**Resultado:** para os buckets do `sbox`, casa igual nas duas plataformas. O sops tira do
caminho o diretório do `.sops.yaml` antes de aplicar a regex
(`strings.TrimPrefix(filePath, configDir + separador)` em `config/config.go`, v3.13.3), e o
`sbox` roda com o diretório atual = cofre e passa `--filename-override <nome>.enc.yaml`
relativo. Como bucket não pode ter separador no nome, a regex sempre vê só
`nome.enc.yaml`, sem barra nenhuma. Conferido com a regra do CI
(`\.enc\.(yaml|yml|json|env)$`): `--filename-override x.enc.yaml` encripta, e
`--filename-override x.txt` falha com `no matching creation rules found` (então a regra é
mesmo aplicada). Uma regra que dependa de diretório (`^secrets/.*`, `/`) não casaria no
Windows, porque o sops usa `\` ali; para o `sbox` ela não faz sentido de qualquer jeito.
**Falta:** conferir com o `.sops.yaml` do cofre real do dono.

### T8. Finais de linha e encoding

O parser que lê as chaves sem decriptar (`bucket.rs`) precisa aceitar CRLF, caso o
bucket passe pelo Bloco de Notas ou por `git` com `autocrlf`. Saída no console em
UTF-8 (`Console.OutputEncoding`) para acentos, emoji e as bolinhas do `show`.

**Feito:** o `Bucket.Summarize` aceita CRLF. A saída não mexe no `Console.OutputEncoding`
(isso mudaria a página de código do console de quem chamou): num console o texto vai em
UTF-16 por `WriteConsoleW`; em pipe ou arquivo, bytes UTF-8 sem BOM e `\n` puro. Cores só
quando a saída é um console, sem `NO_COLOR`, ligando `ENABLE_VIRTUAL_TERMINAL_PROCESSING`
(e devolvendo o modo original ao sair).

### T9. Testes e distribuição

- ~~A suíte roda no Windows?~~ A biblioteca `tests/lib/Sbox/Test.pm` já foi conferida no
  Strawberry Perl: argumentos com aspas, barras, vazios e `%PATH%` chegam intactos, o
  stdin/stdout são byte a byte e as ferramentas falsas viram `.cmd`. Falta rodar contra um
  `sbox.exe` de verdade.
- Sem terminal: no Unix, as confirmações (`del`, `rm`) leem do `/dev/tty`. O `sbox.exe`
  precisa responder "não" quando roda sem console, como nos testes `… sem terminal não apaga`.

  **Decidido:** no Windows a confirmação só acontece quando **o próprio stdin é o console**
  (`Console.IsInputRedirected` falso) **e** `CONIN$`/`CONOUT$` abrem; a pergunta vai para o
  `CONOUT$` e a resposta vem do `CONIN$` por `ReadConsoleW` (em
  [`Win32Terminal.cs`](src/Infrastructure/Win32Terminal.cs)). Em qualquer outro caso a resposta é "não", sem
  perguntar. O motivo: a suíte roda o `sbox.exe` com o stdin vindo de um arquivo, mas
  herdando o console do `prove`. Abrir o `CONIN$` direto, como o Rust faz com o
  `/dev/tty`, funcionaria nesse caso, e o teste ficaria esperando quem roda a suíte num
  console local digitar a resposta (no CI não há console, e passaria). Exigir o stdin no
  console evita isso, e vale para qualquer script ou agente de IA que chame o `sbox` com
  pipe. **Diferença em relação ao Unix:** lá, `echo | sbox del x` ainda pergunta pelo
  `/dev/tty` quando há terminal; no Windows, responde "não" e quem quer apagar sem console
  passa `-y`. `GetConsoleWindow` não serve como critério: ele diz se há uma janela de
  console, não se quem chamou espera uma pergunta.

  **Testável sem console:** as duas decisões ficam em funções puras em
  [`PromptRules.cs`](src/Domain/PromptRules.cs): `CanAskInConsole` (stdin
  redirecionado? console abriu?) e `IsAffirmative` (`s`, `sim`, `y`, `yes`, sem diferença de
  maiúsculas e ignorando espaços nas pontas; `n`, vazio, qualquer outra coisa e nulo são
  "não"). O `Win32Terminal` só coleta os fatos e o `TerminalConfirmation` liga as pontas.
  Conferidas com 26 casos num projeto de rascunho fora do repositório (sem projeto de teste
  com NuGet por enquanto: a suíte Perl é o contrato). O ramo interativo de verdade (digitar
  num console) não dá para a suíte cobrir; roteiro manual (num console, com uma chave age
  descartável em `SOPS_AGE_KEY_FILE` e o `.sops.yaml` dela em `$config`):

  ```powershell
  $sbox = "$PWD\out\sbox.exe"; $v = Join-Path $env:TEMP "sbox-manual-$(Get-Random)"; mkdir $v | Out-Null
  Copy-Item $config $v; $env:SOPS_BOX_DIR = $v; & $sbox new teste
  & $sbox set teste TOKEN; & $sbox get teste TOKEN   # digita o valor 2x sem eco → "✓ TOKEN criado"; get mostra o valor
  & $sbox set teste X                                # digita valores diferentes → "erro: os valores não batem"; Ctrl+C no meio → o eco volta
  & $sbox del teste; "código $LASTEXITCODE"          # n ou Enter → "erro: cancelado", código 1; repita com s → apagado, código 0
  Remove-Item -Recurse $v; Remove-Item Env:SOPS_BOX_DIR
  ```
- Job `windows-latest` no CI com sops + age descartável, `dotnet publish` e `prove tests/`.
- **O `08-security.t` é fraco no Windows** (achado ao portar o `set`; o teste não foi
  mudado). O vigia varre a linha de comando dos processos com `Get-CimInstance
  Win32_Process`, e uma volta leva ~220 ms nesta máquina, enquanto um `sbox set` inteiro
  (com o sops) leva ~86 ms. Controle negativo: com o canário passado **como argumento** do
  `sbox`, o vigia não achou nada em 3 tentativas; um processo de 3 s com o canário, ele acha.
  Ou seja, o teste passa, mas pegaria um vazamento só por sorte. A garantia 1 foi conferida
  de outro jeito: um sops falso (via `SOPS_BIN`) gravou os próprios argumentos (`set`,
  `--value-stdin`, `pessoal.enc.yaml`, `["CANARIO"]`) e o stdin (`"SEGREDO-CANARIO"`).
  Proposta para o teste (a decidir): no Windows, usar um sops falso que registra argv/stdin,
  como acima, ou um vigia por evento (`Register-CimIndicationEvent` com
  `__InstanceCreationEvent WITHIN 0.05`), ou um `SOPS_BIN` que demora de propósito para o
  processo ficar visível.
- Tamanho e tempo de início do NativeAOT. Com `ls`/`new`/`mv`/`del`: `sbox.exe` de
  2.065.408 bytes (o esqueleto tinha 929.280; o salto é o `System.Diagnostics.Process`).
  Com as camadas e a ACL do temporário: 2.292.736 bytes (~35 KB das camadas, ~150 KB do
  `System.Security.AccessControl`). Com os comandos de secrets e o JSON: 2.336.768 bytes.
- `.exe` sem assinatura cai no SmartScreen/Defender: assinar, ou distribuir via
  `winget`/`scoop`.

---

## Próximo passo sugerido

1. ~~Esqueleto em C# e job `windows-latest` no CI.~~ Feito; com o esqueleto, quase toda a
   suíte falha, e o trabalho do port é deixá-la verde arquivo por arquivo (e crescer o
   `tests.txt`).
2. ~~Rodar **T1** e **T7**.~~ Feito: nenhum dos dois muda o desenho.
3. ~~Primeiro alvo: `01-buckets.t` (`ls`, `new`, `mv`, `del`).~~ Feito.
4. ~~`set`/`get`/`show -r`/`rm`/`encrypt` com o `json.rs` portado.~~ Feito (`02`, `05`,
   `06`, `07` verdes).
5. Próximo: `run` (T5, `04-run.t`), `get -c` (T3, `09-clipboard.t`), `set --dialog` (T6) e
   `edit`.
6. Marcar os jobs de teste como obrigatórios na proteção da `main`.
