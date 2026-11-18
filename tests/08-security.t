# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

# Um vigia em segundo plano varre a linha de comando de todos os processos
# enquanto o sbox grava um valor canário. O canário chega ao vigia pelo
# ambiente, para não aparecer na linha de comando do próprio vigia.

use strict;
use warnings;
use FindBin;
use lib "$FindBin::Bin/lib";
use Sbox::Test;
use Test::More;

my $canary = 'SEGREDO-CANARIO';
my $vault = new_vault();
sbox(['new', 'pessoal']);

my $watcher = "$ROOT/watcher.pl";
write_file($watcher, <<'PERL');
$| = 1;
my ($canary, $stop) = @ENV{qw(CANARY STOP_FILE)};
if ($^O eq 'MSWin32') {
    exec 'powershell', '-NoProfile', '-Command',
        'while (-not (Test-Path $env:STOP_FILE)) { Get-CimInstance Win32_Process | '
      . 'Where-Object { $_.CommandLine -like "*$env:CANARY*" } | ForEach-Object { $_.CommandLine } }';
}
my $deadline = time + 120;
while (!-e $stop && time < $deadline) {
    if (-d '/proc/self') {
        for my $file (glob '/proc/[0-9]*/cmdline') {
            open(my $handle, '<', $file) or next;
            local $/;
            my $line = <$handle> // '';
            print "$file\n" if index($line, $canary) >= 0;
        }
    } else {
        print grep { index($_, $canary) >= 0 } `ps -axww -o args= 2>/dev/null`;
    }
    select(undef, undef, undef, 0.005);
}
PERL

my ($leaks, $stop) = ("$ROOT/leaks", "$ROOT/stop-watcher");
my $pid = spawn_in_background([$^X, $watcher], $leaks, env => { CANARY => $canary, STOP_FILE => $stop });
pause(1);
sbox(['set', 'pessoal', 'CANARIO'], stdin => $canary) for 1 .. 3;
write_file($stop, '');
waitpid($pid, 0);

is(read_file($leaks), '', 'valor nunca aparece na linha de comando de um processo');
ok(!file_matches("$vault/pessoal.enc.yaml", qr/\Q$canary\E/), 'plaintext do valor não está no arquivo');
my @plaintext_buckets = grep { !file_matches($_, qr/^sops:/m) } glob("$vault/*.enc.yaml");
is_deeply(\@plaintext_buckets, [], 'nenhum arquivo em texto puro criado no cofre');

done_testing();
