// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// sbox — gerencia um cofre sops (%USERPROFILE%\secrets) sem digitar `sops` na mão
// (espelho de src/main.rs). Só dois conceitos: bucket = arquivo `*.enc.yaml`;
// secret = uma linha `CHAVE: valor` dentro dele.
//
// Composition root: monta os adaptadores (Infrastructure) e entrega à CLI (Presentation), à
// mão. Sem container de DI: o NativeAOT não combina com reflection, e são quatro objetos.

using System.Runtime.Versioning;
using Sbox.Infrastructure;
using Sbox.Presentation;

// Este programa só existe no Windows (console por kernel32, ACL do NTFS). Declarar isso uma
// vez para o assembly satisfaz o analisador de plataforma (CA1416) sem trocar o TFM para
// net10.0-windows, que mudaria os caminhos de saída usados no README e no CI.
[assembly: SupportedOSPlatform("windows")]

namespace Sbox;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        Win32Terminal terminal = new();
        try
        {
            ProcessEnvironment environment = new();
            Cli cli = new(environment, new WindowsFileSystem(), SopsProcess.FromEnvironment(environment), terminal);
            return cli.Run(arguments);
        }
        finally
        {
            terminal.RestoreConsoleModes();
        }
    }
}
