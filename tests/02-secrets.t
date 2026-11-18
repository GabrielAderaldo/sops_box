# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use Test::More;

my $vault = new_vault();
my $bucket_file = "$vault/pessoal.enc.yaml";
sbox(['new', 'pessoal']);

sub set_from_stdin { my ($key, $value) = @_; sbox(['set', 'pessoal', $key], stdin => $value) }
sub get { stdout_of('get', 'pessoal', $_[0]) }

set_from_stdin(TOKEN => "tok-123\n");
is(get('TOKEN'), 'tok-123', 'set via stdin + get (sem \n do echo)');
contains(output_of('set', 'pessoal', 'A', '1'), 'criado', "set novo diz 'criado'");
contains(output_of('set', 'pessoal', 'A', '2'), 'atualizado', "set existente diz 'atualizado'");
is(get('A'), '2', 'valor atualizado');

my @difficult_values = (
    'aspas " e barra \ juntas', "quebra\nde\nlinha", "tab\there", 'acentuação çãõ',
    'emoji 😀🔑 e CJK 漢字', '  espaços nas pontas  ', '{"parece":"json"}', "'aspas simples'",
    '$HOME `cmd` $(cmd)', 'ENC[AES256_GCM,data:x]', '- começa com hífen', '#comentário',
    'null', 'true', '123',
);
for my $value (@difficult_values) {
    set_from_stdin(K => $value);
    is(get('K'), $value, 'roundtrip: ' . label_for($value));
}
set_from_stdin(K => "\x01\x02ctrl\x1f");
is(get('K'), "\x01\x02ctrl\x1f", 'roundtrip: caracteres de controle');
set_from_stdin(K => "fim com newlines\n\n\n");
is(get('K'), "fim com newlines\n\n\n", 'roundtrip: várias linhas mantêm os \n finais');
my $ssh_key = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaA==\n-----END OPENSSH PRIVATE KEY-----\n";
set_from_stdin(SSH => $ssh_key);
is(get('SSH'), $ssh_key, 'roundtrip: chave SSH mantém o \n final');

ko_run('stdin vazio é recusado', 'set', 'pessoal', 'VAZIO');
contains(output_of('set', 'pessoal', 'VAZIO'), '--dialog', 'stdin vazio sugere --dialog');
ok(!file_matches($bucket_file, qr/^VAZIO:/m), '…e nada foi gravado');
sbox(['set', 'pessoal', 'VAZIO', '']);
is(get('VAZIO'), '', 'valor vazio de propósito, como argumento');

my $big = random_text(200_000);
set_from_stdin(GRANDE => $big);
is(length(get('GRANDE')), length($big), 'valor de 200 KB');

ok_run('chave com espaço', 'set', 'pessoal', 'minha chave', 'v1');
is(get('minha chave'), 'v1', 'get chave com espaço');
ok_run('chave com ponto e hífen', 'set', 'pessoal', 'app.db-url', 'v2');
is(get('app.db-url'), 'v2', 'get chave com ponto');
ko_run('chave vazia é recusada', 'set', 'pessoal', '', 'x');
ko_run('get de chave inexistente falha', 'get', 'pessoal', 'NAO_EXISTE');
contains(output_of('get', 'pessoal', 'NAO_EXISTE'), 'sbox show pessoal', 'erro de chave inexistente orienta');

my $show = stdout_of('show', 'pessoal');
contains($show, 'TOKEN', 'show lista chave');
lacks($show, 'tok-123', 'show sem -r não revela valor');
contains($show, '••••••••', 'show mascara');
sbox(['set', 'pessoal', 'ML', "a\nb"]);
contains(stdout_of('show', 'pessoal', '-r'), 'tok-123', 'show -r revela');
contains(stdout_of('show', 'pessoal', '-r'), 'a⏎b', 'show -r mostra multilinha numa linha');

ok_run('rm -y remove', 'rm', 'pessoal', 'ML', '-y');
ko_run('rm de chave inexistente falha', 'rm', 'pessoal', 'ML', '-y');
ok(!file_matches($bucket_file, qr/^ML:/m), 'rm: chave sumiu do arquivo');
ko_run('rm sem -y e sem terminal não remove', 'rm', 'pessoal', 'A');
is(get('A'), '2', '…e a chave continua');

is(length(get('TOKEN')), 7, 'get em pipe não adiciona \n');
lacks(stdout_of('ls'), "\e", 'saída em pipe sem códigos de cor');
my $direct = run_command(['sops', 'decrypt', '--extract', '["TOKEN"]', 'pessoal.enc.yaml'], cwd => $vault);
is($direct->{out}, 'tok-123', 'sops decrypt direto confirma o conteúdo');

done_testing();
