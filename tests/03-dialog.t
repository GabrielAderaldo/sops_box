# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

# set --dialog com uma janela falsa no PATH. O mesmo script faz o papel do
# osascript (macOS) e do zenity (Linux); DISPLAY finge a sessão gráfica que o
# Linux exige.

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use File::Basename qw(dirname);
use Test::More;

plan skip_all => 'a janela do Windows (CredUI) é chamada por API, não por um programa que dê para trocar no PATH'
    if IS_WINDOWS;

my $vault = new_vault();
my $bucket_file = "$vault/pessoal.enc.yaml";
sbox(['new', 'pessoal']);

my $arguments_file = "$ROOT/dialog-args";
my $fake_dialog = <<'PERL';
open(my $record, '>', $ENV{FAKE_DIALOG_ARGS}) or die $!;
print {$record} join(' ', @ARGV), "\n";
close($record);
if ($ENV{FAKE_DIALOG_CANCEL}) { print STDERR "execution error: User canceled. (-128)\n"; exit 1 }
if ($ENV{FAKE_DIALOG_BROKEN}) { print STDERR "quebrado\n"; exit 3 }
print "$ENV{FAKE_DIALOG_VALUE}\n";
PERL
my $tools = dirname(make_tool('osascript', $fake_dialog));
make_tool('zenity', $fake_dialog, $tools);

sub with_dialog {
    my ($environment, @arguments) = @_;
    my $result = sbox([@arguments], env => {
        DISPLAY => ':99', PATH => "$tools:$ENV{PATH}", FAKE_DIALOG_ARGS => $arguments_file, %$environment,
    });
    return $result->{out} . $result->{err};
}

contains(with_dialog({ FAKE_DIALOG_VALUE => 'valor da janela' }, qw(set pessoal DO_DIALOGO --dialog)),
    'DO_DIALOGO criado', 'grava o valor digitado na janela');
is(stdout_of('get', 'pessoal', 'DO_DIALOGO'), 'valor da janela', '…e o valor é o da janela');
contains(read_file($arguments_file), 'Valor de DO_DIALOGO (bucket pessoal)', 'a janela mostra bucket e chave');
lacks(read_file($arguments_file), 'valor da janela', 'o valor nunca vira argumento da janela');

with_dialog({ FAKE_DIALOG_VALUE => 'novo' }, qw(set pessoal DO_DIALOGO --dialog));
is(stdout_of('get', 'pessoal', 'DO_DIALOGO'), 'novo', 'atualiza chave existente');
contains(with_dialog({ FAKE_DIALOG_CANCEL => 1 }, qw(set pessoal CANCELADA --dialog)), 'cancelado', 'cancelar não grava');
ok(!file_matches($bucket_file, qr/^CANCELADA:/m), '…e a chave não existe');
contains(with_dialog({ FAKE_DIALOG_VALUE => '' }, qw(set pessoal VAZIA --dialog)), 'valor vazio', 'valor vazio na janela não grava');
contains(with_dialog({ FAKE_DIALOG_BROKEN => 1 }, qw(set pessoal X --dialog)), 'quebrado', 'falha da ferramenta é repassada');
contains(with_dialog({}, qw(set pessoal X valor --dialog)), 'não os dois', '--dialog com valor é recusado');

unlink $arguments_file;
contains(with_dialog({ FAKE_DIALOG_VALUE => 'v' }, qw(set nada X --dialog)), 'não existe', 'bucket inexistente falha…');
ok(!-e $arguments_file, '…antes de abrir a janela');

if (IS_LINUX) {
    my $result = sbox([qw(set pessoal X --dialog)], env => { DISPLAY => undef, WAYLAND_DISPLAY => undef });
    contains($result->{err}, 'sem janela', 'sem sessão gráfica, explica');
}

done_testing();
