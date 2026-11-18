# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use Test::More;

my $vault = new_vault();
write_file("$vault/plano.enc.yaml", qq(P: "1"\n));

contains(stdout_of('ls'), 'texto puro', 'ls avisa texto puro');
ko_run('get recusa bucket em texto puro', 'get', 'plano', 'P');
ko_run('set recusa bucket em texto puro', 'set', 'plano', 'P', '2');
ok_run('encrypt encripta', 'encrypt', 'plano');
ok(file_matches("$vault/plano.enc.yaml", qr/^sops:/m), 'arquivo agora tem bloco sops');
is(stdout_of('get', 'plano', 'P'), '1', 'valor preservado após encrypt');
contains(output_of('encrypt', 'plano'), 'já está encriptado', 'encrypt de novo é no-op');

done_testing();
