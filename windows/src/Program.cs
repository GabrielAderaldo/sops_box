// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// sbox — gerencia um cofre sops (%USERPROFILE%\secrets) sem digitar `sops` na mão
// (espelho de src/main.rs). Só dois conceitos: bucket = arquivo `*.enc.yaml`;
// secret = uma linha `CHAVE: valor` dentro dele.

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
                    break;
                case Command.ShowVersion:
                    Terminal.PrintLine($"sbox {BuildInfo.Version}");
                    break;
                default:
                    Vault vault = Vault.Locate(invocation.VaultDirectory);
                    SboxCommands.Open(vault).Run(invocation.Command);
                    break;
            }
            return 0;
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
