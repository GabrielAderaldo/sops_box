# SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
# SPDX-License-Identifier: AGPL-3.0-or-later

# Auxiliares da suíte de contrato do sbox. A mesma suíte roda contra qualquer
# implementação (Rust no Linux/macOS, C# no Windows): ela só conversa com o
# binário pela linha de comando, stdin, stdout, stderr e código de saída.
#
# Variáveis:
#   SBOX_BIN               binário testado (obrigatório)
#   SBOX_OTHER_BIN         outro binário, para o teste de interoperabilidade
#   SBOX_EXPECTED_VERSION  versão que o `--version` deve mostrar
#   SBOX_TEST_SOPS_CONFIG  `.sops.yaml` copiado para cada cofre temporário
#                          (padrão: ~/secrets/.sops.yaml)
#
# Sem `use utf8` de propósito: textos esperados e saídas são comparados como bytes UTF-8.

package Sbox::Test;

use strict;
use warnings;

use Exporter 'import';
use Cwd qw(getcwd);
use File::Basename qw(dirname);
use File::Copy qw(copy);
use File::Path qw(make_path);
use File::Spec;
use File::Temp qw(tempdir);
use MIME::Base64 qw(encode_base64);
use Test::More ();

our @EXPORT = qw(
    IS_WINDOWS IS_MACOS IS_LINUX
    $BIN $OTHER_BIN $ROOT
    sbox output_of stdout_of ok_run ko_run contains lacks
    run_command spawn_in_background
    new_vault make_tool write_file read_file file_matches
    line_count trim_trailing_newlines random_text label_for pause
);

use constant IS_WINDOWS => $^O eq 'MSWin32';
use constant IS_MACOS   => $^O eq 'darwin';
use constant IS_LINUX   => $^O eq 'linux';

our $ROOT = tempdir('sbox-tests-XXXXXX', TMPDIR => 1, CLEANUP => 1);
our $BIN = absolute_executable($ENV{SBOX_BIN})
    or Test::More::BAIL_OUT('defina SBOX_BIN com o caminho do binário a testar (ex.: make test)');
our $OTHER_BIN = absolute_executable($ENV{SBOX_OTHER_BIN});

$ENV{NO_COLOR} = '1';
delete $ENV{WAYLAND_DISPLAY_FORCE};
delete $ENV{SOPS_BOX_DIR};

my $SOPS_CONFIG = $ENV{SBOX_TEST_SOPS_CONFIG}
    // File::Spec->catfile($ENV{HOME} // $ENV{USERPROFILE} // '', 'secrets', '.sops.yaml');
-f $SOPS_CONFIG
    or Test::More::BAIL_OUT("sem $SOPS_CONFIG: crie o cofre (docs/MANUAL.md) ou defina SBOX_TEST_SOPS_CONFIG");

# MARK: - Rodar processos

# Roda um comando e devolve { out, err, status }, com stdout e stderr em bytes
# exatos. Opções: stdin (bytes; padrão vazio, como `</dev/null`), env (chave =>
# valor, ou undef para remover) e cwd.
#
# Os três canais passam por arquivos temporários: nada de pipe que trava com
# saídas grandes, e funciona igual no Windows.
sub run_command {
    my ($command, %option) = @_;
    my $stdin_path = scratch_file();
    write_file($stdin_path, $option{stdin} // '');
    my ($stdout_path, $stderr_path) = (scratch_file(), scratch_file());

    my $status = with_redirections($stdin_path, $stdout_path, $stderr_path, $option{env}, $option{cwd}, sub {
        my @arguments = IS_WINDOWS ? map { quote_for_windows($_) } @$command : @$command;
        my $program = $command->[0];
        system { $program } @arguments;
        return $? == -1 ? 255 : $? & 127 ? 128 + ($? & 127) : $? >> 8;
    });
    return { out => read_file($stdout_path), err => read_file($stderr_path), status => $status };
}

# Inicia um comando em segundo plano com o stdout num arquivo; devolve o pid
# (use waitpid). Mesmas opções de ambiente do run_command.
sub spawn_in_background {
    my ($command, $stdout_path, %option) = @_;
    my $null = scratch_file();
    write_file($null, '');
    return with_redirections($null, $stdout_path, File::Spec->devnull, $option{env}, undef, sub {
        if (IS_WINDOWS) {
            return system(1, map { quote_for_windows($_) } @$command);
        }
        my $pid = fork() // die "fork: $!";
        if ($pid == 0) {
            exec { $command->[0] } @$command;
            exit 127;
        }
        return $pid;
    });
}

# O binário testado, com os argumentos dados.
sub sbox {
    my ($arguments, %option) = @_;
    return run_command([$BIN, @$arguments], %option);
}

# stdout + stderr juntos, como o `$(cmd 2>&1)` do shell.
sub output_of {
    my $result = sbox([@_]);
    return $result->{out} . $result->{err};
}

sub stdout_of { return sbox([@_])->{out} }

sub ok_run {
    my ($name, @arguments) = @_;
    my $result = sbox([@arguments]);
    Test::More::ok($result->{status} == 0, $name)
        or Test::More::diag("esperava sucesso, saiu com $result->{status}: $result->{err}");
}

sub ko_run {
    my ($name, @arguments) = @_;
    my $result = sbox([@arguments]);
    Test::More::ok($result->{status} != 0, $name) or Test::More::diag('esperava falha, saiu com 0');
}

sub contains {
    my ($text, $fragment, $name) = @_;
    Test::More::ok(index($text, $fragment) >= 0, $name)
        or Test::More::diag("esperava conter [$fragment] em [$text]");
}

sub lacks {
    my ($text, $fragment, $name) = @_;
    Test::More::ok(index($text, $fragment) < 0, $name)
        or Test::More::diag("não esperava [$fragment] em [$text]");
}

# MARK: - Cofres e arquivos

# Cofre temporário novo, com o `.sops.yaml` do usuário, já exportado em SOPS_BOX_DIR.
sub new_vault {
    my $vault = tempdir('vault-XXXXXX', DIR => $ROOT);
    copy($SOPS_CONFIG, File::Spec->catfile($vault, '.sops.yaml')) or die "copiar .sops.yaml: $!";
    $ENV{SOPS_BOX_DIR} = $vault;
    return $vault;
}

# Cria um executável falso escrito em Perl (sops, janela de diálogo…) e devolve o
# caminho dele. No Windows vira `nome.cmd` chamando `nome.pl`.
sub make_tool {
    my ($name, $body, $directory) = @_;
    $directory //= tempdir('tools-XXXXXX', DIR => $ROOT);
    make_path($directory);
    if (IS_WINDOWS) {
        my $script = File::Spec->catfile($directory, "$name.pl");
        write_file($script, "$body\n");
        my $launcher = File::Spec->catfile($directory, "$name.cmd");
        write_file($launcher, qq(\@"$^X" "%~dp0$name.pl" %*\r\n\@exit /b %ERRORLEVEL%\r\n));
        return $launcher;
    }
    my $path = File::Spec->catfile($directory, $name);
    write_file($path, "#!$^X\n$body\n");
    chmod 0755, $path or die "chmod $path: $!";
    return $path;
}

sub write_file {
    my ($path, $contents) = @_;
    open(my $handle, '>:raw', $path) or die "gravar $path: $!";
    print {$handle} $contents;
    close($handle) or die "fechar $path: $!";
}

sub read_file {
    my ($path) = @_;
    open(my $handle, '<:raw', $path) or return '';
    local $/;
    my $contents = <$handle>;
    return $contents // '';
}

sub file_matches {
    my ($path, $pattern) = @_;
    return -e $path && read_file($path) =~ $pattern;
}

# MARK: - Texto

sub line_count {
    my ($text) = @_;
    my $count = () = $text =~ /\n/g;
    return $count;
}

# Como o `$(...)` do shell: tira as quebras de linha do fim (\n ou \r\n).
sub trim_trailing_newlines {
    my ($text) = @_;
    $text =~ s/(?:\r?\n)+\z//;
    return $text;
}

# Texto ASCII aleatório (base64, sem quebras) com `length` caracteres.
sub random_text {
    my ($length) = @_;
    my $bytes = join('', map { chr(int(rand(256))) } 1 .. int($length * 3 / 4) + 3);
    return substr(encode_base64($bytes, ''), 0, $length);
}

# Rótulo curto e legível para um valor de teste (22 caracteres, ⏎ e → visíveis).
sub label_for {
    my ($value) = @_;
    require Encode;
    my $characters = substr(Encode::decode('UTF-8', $value), 0, 22);
    $characters =~ s/\n/\x{23CE}/g;
    $characters =~ s/\t/\x{2192}/g;
    return Encode::encode('UTF-8', $characters);
}

sub pause { select(undef, undef, undef, $_[0]) }

# MARK: - Detalhes

# Troca stdin/stdout/stderr, ambiente e diretório só durante `$code`. O
# Test::More guarda cópias próprias dos handles, então o TAP não se perde.
sub with_redirections {
    my ($stdin_path, $stdout_path, $stderr_path, $environment, $directory, $code) = @_;
    open(my $saved_in,  '<&', \*STDIN)  or die "dup stdin: $!";
    open(my $saved_out, '>&', \*STDOUT) or die "dup stdout: $!";
    open(my $saved_err, '>&', \*STDERR) or die "dup stderr: $!";
    my $previous_directory = getcwd();

    local %ENV = %ENV;
    while (my ($key, $value) = each %{ $environment // {} }) {
        if (defined $value) { $ENV{$key} = $value } else { delete $ENV{$key} }
    }

    my $result = eval {
        open(STDIN,  '<', $stdin_path)  or die "stdin: $!";
        open(STDOUT, '>', $stdout_path) or die "stdout: $!";
        open(STDERR, '>', $stderr_path) or die "stderr: $!";
        binmode($_) for \*STDIN, \*STDOUT, \*STDERR;
        chdir($directory) or die "cd $directory: $!" if defined $directory;
        $code->();
    };
    my $error = $@;

    chdir($previous_directory);
    open(STDIN,  '<&', $saved_in)  or die "restaurar stdin: $!";
    open(STDOUT, '>&', $saved_out) or die "restaurar stdout: $!";
    open(STDERR, '>&', $saved_err) or die "restaurar stderr: $!";
    die $error if $error;
    return $result;
}

# Aspas no padrão do CommandLineToArgvW / runtime do C, para que cada argumento
# chegue intacto ao processo filho. O Perl não mexe em argumentos já entre aspas.
sub quote_for_windows {
    my ($argument) = @_;
    $argument =~ s/(\\*)"/$1$1\\"/g;
    $argument =~ s/(\\+)\z/$1$1/;
    return qq("$argument");
}

my $scratch_counter = 0;

sub scratch_file {
    $scratch_counter++;
    return File::Spec->catfile($ROOT, "io-$$-$scratch_counter");
}

sub absolute_executable {
    my ($path) = @_;
    return undef unless defined $path && length $path;
    $path .= '.exe' if IS_WINDOWS && !-e $path && -e "$path.exe";
    return undef unless -e $path;
    return File::Spec->rel2abs($path);
}

1;
