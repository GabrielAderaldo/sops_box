# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use Test::More;

sub powershell { run_command(['powershell', '-NoProfile', '-Command', $_[0]], %{ $_[1] // {} }) }

my (%clipboard, $kind);
if (IS_MACOS) {
    $kind = 'macOS';
    %clipboard = (
        paste => sub { run_command(['pbpaste'])->{out} },
        copy  => sub { run_command(['pbcopy'], stdin => $_[0]) },
        types => sub {
            run_command(['osascript', '-l', 'JavaScript', '-e',
                'ObjC.import("AppKit"); $.NSPasteboard.generalPasteboard.types.js.map(t => t.js).join(" ")'])->{out};
        },
    );
} elsif (IS_WINDOWS) {
    $kind = 'Windows';
    %clipboard = (
        paste => sub { powershell('Get-Clipboard -Raw')->{out} },
        copy  => sub {
            length $_[0]
                ? powershell('Set-Clipboard -Value ([Console]::In.ReadToEnd())', { stdin => $_[0] })
                : powershell('Add-Type -AssemblyName System.Windows.Forms; [Windows.Forms.Clipboard]::Clear()');
        },
        types => sub {
            powershell('Add-Type -AssemblyName System.Windows.Forms; '
                . '[Windows.Forms.Clipboard]::GetDataObject().GetFormats() -join " "')->{out};
        },
    );
} elsif (($ENV{DISPLAY} || $ENV{WAYLAND_DISPLAY}) && run_command(['xclip', '-version'])->{status} == 0) {
    $kind = 'X11';
    %clipboard = (
        paste => sub { run_command([qw(xclip -selection clipboard -o)])->{out} },
        copy  => sub { run_command([qw(xclip -selection clipboard -i)], stdin => $_[0]) },
    );
}
plan skip_all => 'sem clipboard disponível nesta sessão' unless $kind;

# Sessões sem área de trabalho (alguns CI) aceitam os comandos mas não guardam nada.
$clipboard{copy}->('sbox-probe');
plan skip_all => "o clipboard ($kind) não guarda conteúdo nesta sessão"
    unless trim_trailing_newlines($clipboard{paste}->()) eq 'sbox-probe';

new_vault();
sbox(['new', 'pessoal']);
sbox(['set', 'pessoal', 'TOKEN', 'tok-123']);
my $paste = sub { trim_trailing_newlines($clipboard{paste}->()) };

sbox([qw(get pessoal TOKEN -c --clear 1)]);
is($paste->(), 'tok-123', "copia para o clipboard ($kind)");
if (IS_MACOS) {
    # As marcas do nspasteboard.org que os gerenciadores de histórico respeitam.
    contains($clipboard{types}->(), 'org.nspasteboard.ConcealedType', 'marca o conteúdo como sensível (ConcealedType)');
    contains($clipboard{types}->(), 'org.nspasteboard.TransientType', 'com limpeza agendada, marca como transitório');
}
if (IS_WINDOWS) {
    # Os formatos que o histórico (Win+V), a nuvem e os monitores de clipboard respeitam.
    my $types = $clipboard{types}->();
    contains($types, 'ExcludeClipboardContentFromMonitorProcessing', 'fica fora dos monitores de clipboard');
    contains($types, 'CanIncludeInClipboardHistory', 'fica fora do histórico do Win+V');
    contains($types, 'CanUploadToCloudClipboard', 'não sincroniza com a nuvem');
}
pause(1.6);
is($paste->(), '', 'limpa depois do tempo');

sbox([qw(get pessoal TOKEN -c --clear 1)]);
$clipboard{copy}->('outra coisa');
pause(1.6);
is($paste->(), 'outra coisa', 'não apaga o que você copiou depois');

if (IS_MACOS) {
    sbox([qw(get pessoal TOKEN -c --clear 0)]);
    my $types = $clipboard{types}->();
    ok($types =~ /ConcealedType/ && $types !~ /TransientType/, 'sem limpeza (--clear 0), não marca como transitório');
}
$clipboard{copy}->('');

done_testing();
