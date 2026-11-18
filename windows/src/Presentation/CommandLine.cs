// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Transforma `argv` num `Command` tipado (espelho de src/command_line.rs). Depois daqui
// ninguém mais olha string de flag: cada comando já chega com os campos que precisa.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sbox.Domain;

namespace Sbox.Presentation;

internal static class CommandLine
{
    // O mesmo texto do Rust; só a última linha muda, porque no Windows o cofre padrão é
    // %USERPROFILE%\secrets e as variáveis se escrevem com %.
    public static readonly string Usage = $"""
        sbox {BuildInfo.Version} — interface para o cofre sops

        Uso: sbox [--dir <cofre>] <comando> [argumentos]

        Buckets
          ls [-a]                       lista os buckets (-a inclui os ocultos)
          new <bucket>                  cria um bucket vazio
          mv <bucket> <novo-nome>       renomeia
          del <bucket> [-y]             apaga (pede confirmação)
          encrypt <bucket>              encripta um bucket que está em texto puro
          edit <bucket>                 abre no %EDITOR% via `sops edit`

        Secrets
          show <bucket> [-r]            lista as chaves (-r revela os valores)
          get <bucket> <CHAVE> [-c]     imprime o valor (-c copia; limpa em 45s,
                                        ajuste com --clear <seg>, 0 desliga)
          set <bucket> <CHAVE> [valor]  cria/atualiza; sem valor, pergunta escondido
                                        ou lê do stdin (prefira: valor na linha de
                                        comando vai pro histórico do shell)
          set <bucket> <CHAVE> --dialog pergunta numa janela do sistema; para quando
                                        não há terminal (ex.: um agente de IA)
          rm <bucket> <CHAVE> [-y]      remove a chave
          run <bucket> -- <cmd...>      roda o comando com os secrets como variáveis
                                        de ambiente

        <bucket> pode ser o nome (gitlab) ou o arquivo (gitlab.enc.yaml).
        Cofre: --dir, %SOPS_BOX_DIR% ou %USERPROFILE%\secrets. Binário do sops: %SOPS_BIN%.
        """.ReplaceLineEndings("\n"); // o checkout com autocrlf traria "\r\n" para o literal

    public static readonly TimeSpan DefaultClearDelay = TimeSpan.FromSeconds(45);
}

// MARK: - Comandos

internal abstract record Command
{
    public sealed record ShowHelp : Command;
    public sealed record ShowVersion : Command;

    public sealed record ListBuckets(bool IncludingHidden) : Command;
    public sealed record CreateBucket(string Bucket) : Command;
    public sealed record RenameBucket(string Bucket, string NewName) : Command;
    public sealed record DeleteBucket(string Bucket, bool SkipConfirmation) : Command;
    public sealed record EncryptBucket(string Bucket) : Command;
    public sealed record EditBucket(string Bucket) : Command;

    public sealed record ShowSecrets(string Bucket, bool RevealValues) : Command;
    public sealed record GetSecret(string Bucket, string Key, SecretDestination Destination) : Command;
    public sealed record SetSecret(string Bucket, string Key, ValueSource Source) : Command;
    public sealed record RemoveSecret(string Bucket, string Key, bool SkipConfirmation) : Command;
    public sealed record RunWithSecrets(string Bucket, IReadOnlyList<string> CommandToRun) : Command;
}

/// Para onde vai o valor do `get`.
internal abstract record SecretDestination
{
    public sealed record StandardOutput : SecretDestination;

    /// `ClearingAfter` nulo = não limpa (`--clear 0`).
    public sealed record Pasteboard(TimeSpan? ClearingAfter) : SecretDestination;
}

/// De onde vem o valor do `set`.
internal abstract record ValueSource
{
    public sealed record Argument(string Value) : ValueSource;

    /// Pergunta escondida se o stdin for um terminal; senão, lê o stdin.
    public sealed record TerminalOrStandardInput : ValueSource;

    /// Janela do sistema (`--dialog`).
    public sealed record Dialog : ValueSource;
}

internal sealed record Invocation(string? VaultDirectory, Command Command)
{
    public static Invocation Parse(IEnumerable<string> argv)
    {
        RawArguments arguments = RawArguments.Parse(argv);
        arguments.Options.Remove("--dir", out string? vaultDirectory);
        return new Invocation(vaultDirectory, CommandFrom(arguments));
    }

    private static Command CommandFrom(RawArguments arguments)
    {
        if (arguments.HasFlag("--version"))
        {
            return new Command.ShowVersion();
        }
        if (arguments.Positional.Count == 0 || arguments.Positional.Peek() == "help")
        {
            return new Command.ShowHelp();
        }
        string name = arguments.Positional.Dequeue();
        if (arguments.HasFlag("--help"))
        {
            return new Command.ShowHelp();
        }

        switch (name)
        {
            case "ls" or "list":
                arguments.Validate("ls", 0, 0, "--all");
                return new Command.ListBuckets(arguments.HasFlag("--all"));
            case "new":
                arguments.Validate("new", 1, 1);
                return new Command.CreateBucket(arguments.NextPositional());
            case "mv":
                arguments.Validate("mv", 2, 2);
                return new Command.RenameBucket(arguments.NextPositional(), arguments.NextPositional());
            case "del":
                arguments.Validate("del", 1, 1, "--yes");
                return new Command.DeleteBucket(arguments.NextPositional(), arguments.HasFlag("--yes"));
            case "encrypt":
                arguments.Validate("encrypt", 1, 1);
                return new Command.EncryptBucket(arguments.NextPositional());
            case "edit":
                arguments.Validate("edit", 1, 1);
                return new Command.EditBucket(arguments.NextPositional());
            case "show":
                arguments.Validate("show", 1, 1, "--reveal");
                return new Command.ShowSecrets(arguments.NextPositional(), arguments.HasFlag("--reveal"));
            case "get":
            {
                arguments.Validate("get", 2, 2, "--copy");
                SecretDestination destination = arguments.HasFlag("--copy")
                    ? new SecretDestination.Pasteboard(arguments.ClearDelay())
                    : new SecretDestination.StandardOutput();
                return new Command.GetSecret(arguments.NextPositional(), arguments.NextPositional(), destination);
            }
            case "set":
            {
                arguments.Validate("set", 2, 3, "--dialog");
                bool fromDialog = arguments.HasFlag("--dialog");
                if (fromDialog && arguments.Positional.Count == 3)
                {
                    throw SboxError.DialogWithValue();
                }
                string bucket = arguments.NextPositional();
                string key = arguments.NextPositional();
                ValueSource source = arguments.Positional.TryDequeue(out string? value)
                    ? new ValueSource.Argument(value)
                    : fromDialog ? new ValueSource.Dialog() : new ValueSource.TerminalOrStandardInput();
                return new Command.SetSecret(bucket, key, source);
            }
            case "rm":
                arguments.Validate("rm", 2, 2, "--yes");
                return new Command.RemoveSecret(
                    arguments.NextPositional(), arguments.NextPositional(), arguments.HasFlag("--yes"));
            case "run":
                arguments.Validate("run", 1, 1);
                if (arguments.AfterSeparator.Count == 0)
                {
                    throw SboxError.MissingCommandToRun();
                }
                return new Command.RunWithSecrets(arguments.NextPositional(), arguments.AfterSeparator);
            default:
                throw SboxError.UnknownCommand(name);
        }
    }
}

// MARK: - Argumentos crus

/// `argv` separado em posicionais, flags (`-r`, `--reveal`), opções com valor
/// (`--dir x`, `--dir=x`) e o que vem depois de `--`.
internal sealed class RawArguments
{
    public Queue<string> Positional { get; } = new();
    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);
    public List<string> UnknownFlags { get; } = new();
    public Dictionary<string, string> Options { get; } = new(StringComparer.Ordinal);
    public List<string> AfterSeparator { get; } = new();

    public static RawArguments Parse(IEnumerable<string> argv)
    {
        RawArguments arguments = new();
        using IEnumerator<string> enumerator = argv.GetEnumerator();
        while (enumerator.MoveNext())
        {
            string argument = enumerator.Current;
            if (argument == "--")
            {
                while (enumerator.MoveNext())
                {
                    arguments.AfterSeparator.Add(enumerator.Current);
                }
                break;
            }

            int equals = argument.IndexOf('=', StringComparison.Ordinal);
            if (argument.StartsWith("--", StringComparison.Ordinal) && equals > 0
                && CanonicalOption(argument[..equals]) is string inlineOption)
            {
                arguments.Options[inlineOption] = argument[(equals + 1)..];
            }
            else if (CanonicalOption(argument) is string option)
            {
                if (!enumerator.MoveNext())
                {
                    throw SboxError.MissingOptionValue(argument);
                }
                arguments.Options[option] = enumerator.Current;
            }
            else if (argument.StartsWith('-') && argument.Length > 1)
            {
                if (CanonicalFlag(argument) is string flag)
                {
                    arguments.Flags.Add(flag);
                }
                else
                {
                    arguments.UnknownFlags.Add(argument);
                }
            }
            else
            {
                arguments.Positional.Enqueue(argument);
            }
        }
        return arguments;
    }

    public bool HasFlag(string flag) => Flags.Contains(flag);

    /// Confere as flags aceitas e a quantidade de posicionais de um comando.
    public void Validate(string command, int minimumPositional, int maximumPositional, params string[] acceptedFlags)
    {
        string? unexpected = UnknownFlags.FirstOrDefault()
            ?? Flags.Order(StringComparer.Ordinal).FirstOrDefault(flag => !acceptedFlags.Contains(flag));
        if (unexpected is not null)
        {
            throw SboxError.UnknownOption(command, unexpected);
        }
        if (Positional.Count < minimumPositional || Positional.Count > maximumPositional)
        {
            throw SboxError.WrongArgumentCount(command);
        }
    }

    /// Só chame depois de `Validate`, que garante que o posicional existe.
    public string NextPositional() => Positional.Dequeue();

    /// `--clear <segundos>`; 0 desliga a limpeza (nulo).
    public TimeSpan? ClearDelay()
    {
        if (!Options.TryGetValue("--clear", out string? text))
        {
            return CommandLine.DefaultClearDelay;
        }
        // Teto de um ano: acima disso o TimeSpan estoura, e ninguém espera tanto pelo clipboard.
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seconds)
            || seconds > 365UL * 24 * 60 * 60)
        {
            throw SboxError.InvalidClearDelay();
        }
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static string? CanonicalOption(string argument) => argument switch
    {
        "-d" or "--dir" => "--dir",
        "--clear" => "--clear",
        _ => null,
    };

    private static string? CanonicalFlag(string argument) => argument switch
    {
        "-a" or "--all" => "--all",
        "-r" or "--reveal" => "--reveal",
        "-c" or "--copy" => "--copy",
        "--dialog" => "--dialog",
        "-y" or "--yes" => "--yes",
        "-h" or "--help" => "--help",
        "-V" or "--version" => "--version",
        _ => null,
    };
}
