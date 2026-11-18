// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Esqueleto do sbox para Windows: só --version e --help. Os comandos chegam um a um,
// cada um junto com o arquivo da suíte de contrato (tests/*.t) que ele deixa verde
// (ver windows/tests.txt).

using System;
using System.IO;
using System.Text;

namespace Sbox;

internal static class Program
{
    private const string Usage = """
        — interface para o cofre sops (versão Windows, em construção)

        Uso: sbox [--dir <cofre>] <comando> [argumentos]

        Esta versão ainda é um esqueleto: só --version e --help funcionam.
        Os comandos (ls, new, show, get, set, run…) estão descritos no
        docs/MANUAL.md e chegam aqui à medida que o port avança.
        """;

    // UTF-8 sem BOM e "\n" puro, como o Rust: a suíte compara bytes, e o
    // Console.Out do Windows usaria a página de código do console e "\r\n".
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static int Main(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] is "--version" or "-V")
        {
            Write(Console.OpenStandardOutput(), $"sbox {BuildInfo.Version}\n");
            return 0;
        }
        if (arguments.Length == 0 || (arguments.Length == 1 && arguments[0] is "--help" or "-h" or "help"))
        {
            Write(Console.OpenStandardOutput(), $"sbox {BuildInfo.Version} {Usage.ReplaceLineEndings("\n")}\n");
            return 0;
        }

        Write(Console.OpenStandardError(), $"erro: `{arguments[0]}` ainda não implementado no Windows\n");
        return 1;
    }

    private static void Write(Stream stream, string text)
    {
        using (stream)
        {
            stream.Write(Utf8.GetBytes(text));
        }
    }
}
