# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

# O comando do `run` é o próprio perl, para o teste ser igual nas três plataformas.
# O `binmode` impede o Windows de trocar \n por \r\n na saída do filho.

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use Cwd qw(getcwd);
use Test::More;

new_vault();
sbox(['new', 'envs']);
sbox(['set', 'envs', 'TOKEN'], stdin => "tok-123\n");

# Limites por variável: 128 KB no Linux, 32.767 caracteres no Windows.
my $half = random_text(IS_WINDOWS ? 15_000 : 50_000);
sbox(['set', 'envs', 'MEDIO'], stdin => "$half\n$half");

sub print_variable { ('run', 'envs', '--', $^X, '-e', "binmode STDOUT; print \$ENV{$_[0]}") }

is(stdout_of(print_variable('TOKEN')), 'tok-123', 'run injeta variáveis');
is(stdout_of(print_variable('MEDIO')), "$half\n$half", 'run injeta valor multilinha grande intacto');
is(stdout_of('run', 'envs', '--', $^X, '-MCwd', '-e', 'print Cwd::getcwd()'), getcwd(), 'run mantém o diretório de quem chamou');
is(sbox(['run', 'envs', '--', $^X, '-e', 'exit 7'])->{status}, 7, 'run propaga exit code');
ko_run('run sem comando falha', 'run', 'envs');
ko_run('run com comando inexistente falha', 'run', 'envs', '--', '/nao/existe');

# Limite por variável é do kernel Linux; o macOS só limita o total (ARG_MAX, ~1 MB).
if (IS_LINUX) {
    sbox(['new', 'grande']);
    sbox(['set', 'grande', 'GRANDE'], stdin => random_text(200_000));
    contains(output_of('run', 'grande', '--', $^X, '-e', '1'), 'Argument list too long', 'run com valor > 128 KB dá erro claro');
}

done_testing();
