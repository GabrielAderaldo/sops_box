# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

# Parser JSON: um sops falso devolve JSON arbitrário, isolando o código do sbox.

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use Test::More;

my $vault = new_vault();
write_file("$vault/fake.enc.yaml", "K: x\nsops:\n  a: 1\n");
my $json_file = "$ROOT/fake.json";
my $fake_sops = make_tool('fake-sops', <<'PERL');
open(my $json, '<:raw', $ENV{FAKE_JSON}) or die $!;
binmode STDOUT;
local $/;
print <$json>;
PERL

sub decode {
    my ($json, $key) = @_;
    write_file($json_file, $json);
    return sbox(['get', 'fake', $key], env => { SOPS_BIN => $fake_sops, FAKE_JSON => $json_file });
}

is(decode('{"K":"é😀"}', 'K')->{out}, 'é😀', 'escapes \u e surrogate');
is(decode('{"K":"\/\b\f"}', 'K')->{out}, "/\b\f", 'escapes \/ \b \f');
is(decode('{"K": 42.5}', 'K')->{out}, '42.5', 'número vira texto');
is(decode('{"K":true}', 'K')->{out}, 'true', 'booleano vira texto');
contains(decode('{"K":{"a":1}}', 'K')->{err}, 'aninhados', 'aninhado é recusado');
contains(decode('{"K":"abc', 'K')->{err}, 'JSON inválido', 'JSON quebrado vira erro');
contains(decode('{"K":"a"} x', 'K')->{err}, 'JSON inválido', 'lixo depois do objeto vira erro');
is(decode(qq({\n\t"A" : "a" ,\n "K":"b"\n}), 'K')->{out}, 'b', 'whitespace e ordem');

done_testing();
