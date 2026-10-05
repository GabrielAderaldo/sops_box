# Política de Segurança

O `sbox` manipula segredos, então levamos relatos de segurança a sério. Obrigado por ajudar a
manter o projeto e quem o usa em segurança.

## Versões suportadas

| versão | recebe correções de segurança |
|---|---|
| `0.1.x` (mais recente) | ✅ |
| anteriores | ❌ |

Enquanto o projeto estiver em `0.x`, só a versão mais recente é corrigida.

## Como reportar uma vulnerabilidade

**Não abra uma issue pública, discussão ou pull request** para vulnerabilidades.

Use o **reporte privado de vulnerabilidades** do GitHub:

1. Acesse a aba [Security → Advisories](https://github.com/GabrielAderaldo/sops_box/security/advisories/new).
2. Clique em **Report a vulnerability**.
3. Descreva o problema, como reproduzir, o impacto e, se tiver, uma sugestão de correção.

O relato fica visível só para você e para o mantenedor.

### O que esperar

| etapa | prazo-alvo |
|---|---|
| confirmação de recebimento | até 3 dias úteis |
| avaliação inicial e classificação de severidade | até 7 dias úteis |
| correção publicada (severidade alta ou crítica) | até 30 dias |

Você será mantido informado durante o processo. Depois da correção, publicaremos um
*security advisory* no GitHub e, se você quiser, daremos crédito pela descoberta.
Pedimos que a divulgação pública espere a correção ou 90 dias após o relato, o que vier primeiro.

## Modelo de ameaças

Para orientar relatos, este é o escopo de proteção que o `sbox` pretende oferecer.

### O que o `sbox` protege

- **Segredos fora da linha de comando de processos.** Valores vão ao sops e ao clipboard
  pelo stdin, nunca como argumento visível em `ps` ou em `/proc/*/cmdline`.
- **Segredos fora do disco em texto puro.** Buckets novos são encriptados em memória;
  buckets em texto puro são recusados por todos os comandos que leem ou gravam valores.
- **Integridade dos arquivos.** Nenhum comando sobrescreve ou trunca um bucket. Criação e
  renomeação usam `link()`, que falha se o destino existir.
- **Clipboard.** Limpeza automática (45 s por padrão), que só apaga se o conteúdo ainda for o segredo.
- **Terminal.** A entrada de valores não tem eco, e o eco é restaurado mesmo com Ctrl-C.
- **Falhas visíveis.** Todo código de saída do sops é conferido. O `sbox` nunca reporta
  sucesso se o sops falhou.

### Fora do escopo

- **A criptografia em si.** Ela é feita pelo [sops](https://github.com/getsops/sops) e pelos
  provedores de chave (age, PGP, KMS). Vulnerabilidades nesses projetos devem ser reportadas a eles.
- **Um atacante com acesso à sua conta de usuário** na mesma máquina. Ele pode ler sua chave
  age, o ambiente dos seus processos e a memória deles.
- **Segredos depois de entregues.** O que um comando executado por `sbox run`, um
  `sbox get` redirecionado para arquivo ou um gerenciador de clipboard faz com o valor está
  fora do controle do `sbox`.
- **Valores passados como argumento** (`sbox set bucket CHAVE valor`). Essa forma é aceita por
  conveniência e documentada como insegura (histórico do shell e `ps`).
- **Nomes das chaves.** O formato sops não encripta as chaves, só os valores. Não coloque
  informação sensível no nome de uma chave.

### Exemplos do que reportar

- um caminho em que um valor aparece em argumentos de processo, logs, mensagens de erro ou
  arquivos temporários em texto puro;
- um caminho em que um bucket é sobrescrito, truncado ou perdido;
- um caso em que o `sbox` reporta sucesso mas o sops falhou;
- o terminal ficar sem eco depois de uma pergunta;
- a limpeza do clipboard apagar algo que não é o segredo, ou não apagar o segredo.

## Boas práticas para quem usa

- Prefira `sbox set bucket CHAVE` (pergunta escondida) ou o stdin a passar o valor como argumento.
- Faça backup da sua chave privada age **fora** do cofre e nunca a commite.
- Versione o cofre em git: os buckets estão encriptados e o histórico ajuda a recuperar erros.
- Confira o `sha256` dos binários baixados de [Releases](https://github.com/GabrielAderaldo/sops_box/releases).
