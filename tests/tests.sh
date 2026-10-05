#!/usr/bin/env bash
# Bateria funcional do sbox, de ponta a ponta (sops real, cofres temporários).
# Uso: tests/tests.sh [binário] [outro-binário-para-interop]
#      (padrão: target/release/sbox — rode `make test`)
# Usa cofres temporários; nunca toca ~/secrets. Precisa do sops e da chave age.

set -u
BIN=$(realpath "${1:-$(dirname "$0")/../target/release/sbox}")
OTHER=${2:+$(realpath "$2")}
ROOT=$(mktemp -d)
trap 'rm -rf "$ROOT"' EXIT
export NO_COLOR=1
unset WAYLAND_DISPLAY_FORCE

pass=0; fail=0; failed=()
green() { printf '\033[38;2;166;227;161m%s\033[0m' "$1"; }
red() { printf '\033[38;2;243;139;168m%s\033[0m' "$1"; }

result() {  # result <nome> <0|1> [detalhe]
    if [ "$2" = 0 ]; then pass=$((pass + 1)); printf '  %s %s\n' "$(green ✓)" "$1"
    else fail=$((fail + 1)); failed+=("$1"); printf '  %s %s %s\n' "$(red ✗)" "$1" "${3:-}"; fi
}
ok() { local n=$1; shift; "$@" >/dev/null 2>&1; result "$n" $([ $? = 0 ] && echo 0 || echo 1) "(esperava sucesso)"; }
ko() { local n=$1; shift; "$@" >/dev/null 2>&1; result "$n" $([ $? != 0 ] && echo 0 || echo 1) "(esperava falha)"; }
eq() { result "$1" $([ "$2" == "$3" ] && echo 0 || echo 1) "→ esperado [$2] obtido [$3]"; }
has() { result "$1" $([[ "$3" == *"$2"* ]] && echo 0 || echo 1) "→ esperava conter [$2] em [$3]"; }

newvault() { V=$ROOT/v$RANDOM$RANDOM; mkdir -p "$V"; cp ~/secrets/.sops.yaml "$V/"; export SOPS_BOX_DIR=$V; }
s() { "$BIN" "$@"; }
# Roda o get e preserva bytes exatos (inclusive \n finais) com um sentinela.
getx() { local o; o=$("$BIN" get "$@"; echo x); printf '%s' "${o%x}"; }

echo "▸ ${BIN#"$(dirname "$(dirname "$(realpath "$0")")")"/}"

echo "Buckets"
newvault
has "ls em cofre vazio orienta" "crie um com: sbox new" "$(s ls 2>&1)"
ok  "new cria bucket" s new pessoal
result "bucket nasce encriptado" $(grep -q '^sops:' "$V/pessoal.enc.yaml"; echo $?)
ko  "new duplicado falha" s new pessoal
has "new duplicado explica" "já existe" "$(s new pessoal 2>&1)"
ko  "nome com / é recusado" s new a/b
ko  "nome .yaml sem .enc é recusado" s new x.yaml
ko  "nome começando com - é recusado" s new -- -x
ok  "aceita nome com sufixo .enc.yaml" s new outro.enc.yaml
eq  "ls lista 2 buckets" "2" "$(s ls | wc -l | tr -d ' ')"
ok  "mv renomeia" s mv outro novo
result "mv: arquivo antigo sumiu, novo existe" $([ ! -e "$V/outro.enc.yaml" ] && [ -e "$V/novo.enc.yaml" ]; echo $?)
ko  "mv para nome existente falha" s mv novo pessoal
result "mv recusado não destruiu nada" $([ -e "$V/novo.enc.yaml" ] && [ -e "$V/pessoal.enc.yaml" ]; echo $?)
ok  "del -y apaga" s del novo -y
result "del: arquivo sumiu" $([ ! -e "$V/novo.enc.yaml" ]; echo $?)
ko  "del sem -y e sem terminal não apaga" s del pessoal </dev/null
result "…e o arquivo continua lá" $([ -e "$V/pessoal.enc.yaml" ]; echo $?)
ko  "bucket inexistente falha" s show nada
touch "$V/.oculto.enc.yaml"; printf 'X: "1"\nsops:\n  a: 1\n' > "$V/.oculto.enc.yaml"
eq  "ls esconde dotfiles" "1" "$(s ls | wc -l | tr -d ' ')"
eq  "ls -a mostra dotfiles" "2" "$(s ls -a | wc -l | tr -d ' ')"

echo "Secrets"
echo "tok-123" | s set pessoal TOKEN 2>/dev/null
eq  "set via stdin + get (sem \\n do echo)" "tok-123" "$(getx pessoal TOKEN)"
has "set novo diz 'criado'" "criado" "$(s set pessoal A 1 2>&1)"
has "set existente diz 'atualizado'" "atualizado" "$(s set pessoal A 2 2>&1)"
eq  "valor atualizado" "2" "$(getx pessoal A)"
for case in 'aspas " e barra \ juntas' $'quebra\nde\nlinha' $'tab\there' 'acentuação çãõ' 'emoji 😀🔑 e CJK 漢字' '  espaços nas pontas  ' '{"parece":"json"}' "'aspas simples'" '$HOME `cmd` $(cmd)' 'ENC[AES256_GCM,data:x]' '- começa com hífen' '#comentário' 'null' 'true' '123'; do
    printf '%s' "$case" | s set pessoal K 2>/dev/null
    eq "roundtrip: $(printf '%s' "$case" | head -c 22 | tr '\n\t' '⏎→')" "$case" "$(getx pessoal K)"
done
printf '\x01\x02ctrl\x1f' | s set pessoal K 2>/dev/null
eq  "roundtrip: caracteres de controle" "$(printf '\x01\x02ctrl\x1f')" "$(getx pessoal K)"
printf 'fim com newlines\n\n\n' | s set pessoal K 2>/dev/null
eq  "roundtrip: só o último \\n do pipe é removido" $'fim com newlines\n\nx' "$(s get pessoal K; echo x)"
printf '' | s set pessoal VAZIO 2>/dev/null
eq  "valor vazio via pipe" "" "$(getx pessoal VAZIO)"
BIG=$(head -c 150000 /dev/urandom | base64 -w0)
printf '%s' "$BIG" | s set pessoal GRANDE 2>/dev/null
eq  "valor de 200 KB" "${#BIG}" "$(getx pessoal GRANDE | wc -c | tr -d ' ')"
ok  "chave com espaço" s set pessoal "minha chave" v1
eq  "get chave com espaço" "v1" "$(getx pessoal "minha chave")"
ok  "chave com ponto e hífen" s set pessoal "app.db-url" v2
eq  "get chave com ponto" "v2" "$(getx pessoal "app.db-url")"
ko  "chave vazia é recusada" s set pessoal "" x
ko  "get de chave inexistente falha" s get pessoal NAO_EXISTE
has "erro de chave inexistente orienta" "sbox show pessoal" "$(s get pessoal NAO_EXISTE 2>&1)"
SHOW=$(s show pessoal)
has "show lista chave" "TOKEN" "$SHOW"
result "show sem -r não revela valor" $([[ "$SHOW" != *tok-123* ]]; echo $?)
has "show mascara" "••••••••" "$SHOW"
s set pessoal ML $'a\nb' 2>/dev/null
has "show -r revela" "tok-123" "$(s show pessoal -r)"
has "show -r mostra multilinha numa linha" "a⏎b" "$(s show pessoal -r)"
ok  "rm -y remove" s rm pessoal ML -y
ko  "rm de chave inexistente falha" s rm pessoal ML -y
result "rm: chave sumiu do arquivo" $(! grep -q '^ML:' "$V/pessoal.enc.yaml"; echo $?)
ko  "rm sem -y e sem terminal não remove" s rm pessoal A </dev/null
eq  "…e a chave continua" "2" "$(getx pessoal A)"
eq  "get em pipe não adiciona \\n" "7" "$(s get pessoal TOKEN | wc -c | tr -d ' ')"
result "saída em pipe sem códigos de cor" $(s ls | grep -q $'\033'; [ $? != 0 ]; echo $?)
eq  "sops decrypt direto confirma o conteúdo" "tok-123" "$(cd "$V" && sops decrypt --extract '["TOKEN"]' pessoal.enc.yaml)"

echo "run"
# Bucket próprio: o Linux limita cada variável de ambiente a 128 KB, e o GRANDE tem 200 KB.
s new envs >/dev/null 2>&1; echo "tok-123" | s set envs TOKEN 2>/dev/null
MED=${BIG:0:50000}; printf '%s\n%s' "$MED" "$MED" | s set envs MEDIO 2>/dev/null
eq  "run injeta variáveis" "tok-123" "$(s run envs -- sh -c 'printf %s "$TOKEN"')"
eq  "run injeta valor multilinha de 100 KB intacto" "$MED"$'\n'"$MED" "$(s run envs -- sh -c 'printf %s "$MEDIO"')"
eq  "run mantém o diretório de quem chamou" "$PWD" "$(s run envs -- pwd)"
s run envs -- sh -c 'exit 7'
eq  "run propaga exit code" "7" "$?"
ko  "run sem comando falha" s run envs
has "run com valor > 128 KB dá erro claro" "Argument list too long" "$(s run pessoal -- true 2>&1)"
ko  "run com comando inexistente falha" s run envs -- /nao/existe

echo "Texto puro"
printf 'P: "1"\n' > "$V/plano.enc.yaml"
has "ls avisa texto puro" "texto puro" "$(s ls)"
ko  "get recusa bucket em texto puro" s get plano P
ko  "set recusa bucket em texto puro" s set plano P 2
ok  "encrypt encripta" s encrypt plano
result "arquivo agora tem bloco sops" $(grep -q '^sops:' "$V/plano.enc.yaml"; echo $?)
eq  "valor preservado após encrypt" "1" "$(getx plano P)"
has "encrypt de novo é no-op" "já está encriptado" "$(s encrypt plano 2>&1)"

echo "Erros e robustez"
ko  "comando desconhecido" s foo
ko  "opção desconhecida" s ls --xyz
ko  "argumentos demais" s show a b c
mkdir -p "$ROOT/semsops"
ko  "diretório sem .sops.yaml é recusado" s --dir "$ROOT/semsops" ls
ok  "--dir <path>" s --dir "$V" ls
ok  "--dir=<path>" s --dir="$V" ls
ok  "-d <path>" s -d "$V" ls
ko  "SOPS_BIN inexistente vira erro" env SOPS_BIN=/nao/existe "$BIN" get pessoal TOKEN
has "erro do sops é repassado" "sops decrypt falhou" "$(SOPS_BIN=false "$BIN" get pessoal TOKEN 2>&1)"
SOPS_BIN=false "$BIN" new falhou >/dev/null 2>&1
result "new com sops falhando não cria arquivo" $([ ! -e "$V/falhou.enc.yaml" ]; echo $?)
SOPS_BIN=true "$BIN" new vazio >/dev/null 2>&1
result "sops com exit 0 mas saída vazia não cria bucket" $([ ! -e "$V/vazio.enc.yaml" ]; echo $?)
result "nenhum temporário largado no cofre" $(ls -A "$V" | grep -q sbox-tmp; [ $? != 0 ]; echo $?)
ok  "--help" s --help
eq  "--version" "sbox 0.1.0" "$(s --version)"

echo "Parser JSON (sops falso, isola o código do sbox)"
FAKE=$ROOT/fake-sops; printf '#!/bin/sh\nexec cat "$FAKE_JSON"\n' > "$FAKE"; chmod +x "$FAKE"
printf 'K: x\nsops:\n  a: 1\n' > "$V/fake.enc.yaml"
fj() { printf '%s' "$1" > "$ROOT/f.json"; SOPS_BIN=$FAKE FAKE_JSON=$ROOT/f.json "$BIN" get fake "$2" 2>&1; }
eq  "escapes \\u e surrogate" "é😀" "$(fj '{"K":"é😀"}' K)"
eq  "escapes \\/ \\b \\f" "/$(printf '\b\f')" "$(fj '{"K":"\/\b\f"}' K)"
eq  "número vira texto" "42.5" "$(fj '{"K": 42.5}' K)"
eq  "booleano vira texto" "true" "$(fj '{"K":true}' K)"
has "aninhado é recusado" "aninhados" "$(fj '{"K":{"a":1}}' K)"
has "JSON quebrado vira erro" "JSON inválido" "$(fj '{"K":"abc' K)"
has "lixo depois do objeto vira erro" "JSON inválido" "$(fj '{"K":"a"} x' K)"
eq  "whitespace e ordem" "b" "$(fj $'{\n\t"A" : "a" ,\n "K":"b"\n}' K)"

echo "Segurança"
LEAK=$ROOT/leak; : > "$LEAK"
( for _ in $(seq 1 300); do grep -l 'SEGREDO-CANARIO' /proc/[0-9]*/cmdline 2>/dev/null >> "$LEAK"; sleep 0.005; done ) &
W=$!
for _ in 1 2 3; do printf 'SEGREDO-CANARIO' | s set pessoal CANARIO 2>/dev/null; done
wait $W
eq  "valor nunca aparece em /proc/*/cmdline" "0" "$(sort -u "$LEAK" | grep -vc "^$" )"
result "plaintext do valor não está no arquivo" $(! grep -q 'SEGREDO-CANARIO' "$V/pessoal.enc.yaml"; echo $?)
result "nenhum arquivo em texto puro criado no cofre" $(for f in "$V"/*; do case $f in *.enc.yaml) grep -q '^sops:' "$f" || echo "$f";; esac; done | grep -qv plano; [ $? != 0 ]; echo $?)

if [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && command -v xclip >/dev/null; then
    echo "Clipboard"
    s get pessoal TOKEN -c --clear 1 2>/dev/null
    eq  "copia para o clipboard" "tok-123" "$(xclip -selection clipboard -o 2>/dev/null)"
    sleep 1.6
    eq  "limpa depois do tempo" "" "$(xclip -selection clipboard -o 2>/dev/null)"
    s get pessoal TOKEN -c --clear 1 2>/dev/null; printf 'outra coisa' | xclip -selection clipboard -i >/dev/null 2>&1
    sleep 1.6
    eq  "não apaga o que você copiou depois" "outra coisa" "$(xclip -selection clipboard -o 2>/dev/null)"
    printf '' | xclip -selection clipboard -i >/dev/null 2>&1
fi

if [ -n "$OTHER" ]; then
    echo "Interoperabilidade com $OTHER"
    "$OTHER" new cruzado >/dev/null 2>&1; printf 'x"y\nz' | "$OTHER" set cruzado C 2>/dev/null
    eq  "lê bucket criado pela outra implementação" $'x"y\nz' "$(getx cruzado C)"
fi

echo
echo "$pass passaram, $fail falharam"
for f in "${failed[@]}"; do echo "  ✗ $f"; done
[ "$fail" = 0 ]
