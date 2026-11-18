// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// sbox — gerencia um cofre sops (%USERPROFILE%\secrets) sem digitar `sops` na mão
// (espelho de src/main.rs). Por enquanto só o parser: todos os comandos são reconhecidos,
// --help e --version funcionam, e o resto sai com "ainda não implementado no Windows".

namespace Sbox;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        try
        {
            Invocation invocation = Invocation.Parse(arguments);
            switch (invocation.Command)
            {
                case Command.ShowHelp:
                    Terminal.PrintLine(CommandLine.Usage);
                    return 0;
                case Command.ShowVersion:
                    Terminal.PrintLine($"sbox {BuildInfo.Version}");
                    return 0;
                default:
                    throw SboxError.NotYetImplemented(arguments.Length > 0 ? arguments[0] : "");
            }
        }
        catch (SboxError error)
        {
            Terminal.Error(error);
            return 1;
        }
        finally
        {
            Terminal.RestoreConsoleModes();
        }
    }
}
