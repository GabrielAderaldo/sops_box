# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use Test::More;

my $vault = new_vault();
my $file = sub { "$vault/$_[0].enc.yaml" };

contains(output_of('ls'), 'crie um com: sbox new', 'ls em cofre vazio orienta');
ok_run('new cria bucket', 'new', 'pessoal');
ok(file_matches($file->('pessoal'), qr/^sops:/m), 'bucket nasce encriptado');
ko_run('new duplicado falha', 'new', 'pessoal');
contains(output_of('new', 'pessoal'), 'já existe', 'new duplicado explica');
ko_run('nome com / é recusado', 'new', 'a/b');
ko_run('nome .yaml sem .enc é recusado', 'new', 'x.yaml');
ko_run('nome começando com - é recusado', 'new', '--', '-x');
ok_run('aceita nome com sufixo .enc.yaml', 'new', 'outro.enc.yaml');
is(line_count(stdout_of('ls')), 2, 'ls lista 2 buckets');

ok_run('mv renomeia', 'mv', 'outro', 'novo');
ok(!-e $file->('outro') && -e $file->('novo'), 'mv: arquivo antigo sumiu, novo existe');
ko_run('mv para nome existente falha', 'mv', 'novo', 'pessoal');
ok(-e $file->('novo') && -e $file->('pessoal'), 'mv recusado não destruiu nada');

ok_run('del -y apaga', 'del', 'novo', '-y');
ok(!-e $file->('novo'), 'del: arquivo sumiu');
ko_run('del sem -y e sem terminal não apaga', 'del', 'pessoal');
ok(-e $file->('pessoal'), '…e o arquivo continua lá');
ko_run('bucket inexistente falha', 'show', 'nada');

write_file($file->('.oculto'), qq(X: "1"\nsops:\n  a: 1\n));
is(line_count(stdout_of('ls')), 1, 'ls esconde dotfiles');
is(line_count(stdout_of('ls', '-a')), 2, 'ls -a mostra dotfiles');

done_testing();
