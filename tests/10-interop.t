# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

# Um bucket criado por outra implementação (SBOX_OTHER_BIN) é lido por esta.

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use Test::More;

plan skip_all => 'defina SBOX_OTHER_BIN para testar a interoperabilidade' unless $OTHER_BIN;

new_vault();
run_command([$OTHER_BIN, 'new', 'cruzado']);
run_command([$OTHER_BIN, 'set', 'cruzado', 'C'], stdin => qq(x"y\nz));
is(stdout_of('get', 'cruzado', 'C'), qq(x"y\nz), "lê bucket criado por $OTHER_BIN");

done_testing();
