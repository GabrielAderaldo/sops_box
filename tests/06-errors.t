# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use File::Path qw(make_path);
use Test::More;

my $vault = new_vault();
sbox(['new', 'pessoal']);
sbox(['set', 'pessoal', 'TOKEN', 'tok-123']);

ko_run('comando desconhecido', 'foo');
ko_run('opção desconhecida', 'ls', '--xyz');
ko_run('argumentos demais', 'show', 'a', 'b', 'c');
make_path("$ROOT/semsops");
ko_run('diretório sem .sops.yaml é recusado', '--dir', "$ROOT/semsops", 'ls');
ok_run('--dir <path>', '--dir', $vault, 'ls');
ok_run('--dir=<path>', "--dir=$vault", 'ls');
ok_run('-d <path>', '-d', $vault, 'ls');

# Um sops que sempre falha e outro que sempre "dá certo" sem imprimir nada.
my $failing_sops = make_tool('false', 'exit 1');
my $silent_sops  = make_tool('true', 'exit 0');
ok(sbox([qw(get pessoal TOKEN)], env => { SOPS_BIN => '/nao/existe' })->{status} != 0, 'SOPS_BIN inexistente vira erro');
my $failed = sbox([qw(get pessoal TOKEN)], env => { SOPS_BIN => $failing_sops });
contains($failed->{err}, 'sops decrypt falhou', 'erro do sops é repassado');
sbox([qw(new falhou)], env => { SOPS_BIN => $failing_sops });
ok(!-e "$vault/falhou.enc.yaml", 'new com sops falhando não cria arquivo');
sbox([qw(new vazio)], env => { SOPS_BIN => $silent_sops });
ok(!-e "$vault/vazio.enc.yaml", 'sops com exit 0 mas saída vazia não cria bucket');

opendir(my $directory, $vault) or die $!;
my @leftovers = grep { /sbox-tmp/ } readdir($directory);
closedir($directory);
is(scalar(@leftovers), 0, 'nenhum temporário largado no cofre');

ok_run('--help', '--help');
my $version = trim_trailing_newlines(stdout_of('--version'));
if (defined $ENV{SBOX_EXPECTED_VERSION}) {
    is($version, "sbox $ENV{SBOX_EXPECTED_VERSION}", '--version bate com a versão do projeto');
} else {
    like($version, qr/^sbox \d+\.\d+\.\d+/, '--version mostra a versão');
}

done_testing();
