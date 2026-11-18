// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O terminal (espelho de src/console.rs; o nome muda para não colidir com System.Console):
// cores suaves, mensagens no stderr, dados no stdout e a pergunta s/N pelo console.
//
// Saída: num console, o texto vai em UTF-16 por WriteConsoleW, porque o console do Windows
// interpretaria bytes UTF-8 na página de código dele (850, 1252…) e estragaria acentos e as
// bolinhas do `show`. Em pipe ou arquivo, vão bytes UTF-8 sem BOM e "\n" puro, como no Rust:
// a suíte compara bytes.

using System;
using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Sbox.Interop;

namespace Sbox;

/// Tons do Catppuccin Mocha / Tokyo Night: contraste baixo, sem branco nem preto puros.
internal enum Style
{
    Secondary,
    Accent,
    Success,
    Warning,
    Failure,
}

internal static class Terminal
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly OutputStream StandardOutput = new(Kernel32.StandardOutputHandle);
    private static readonly OutputStream StandardError = new(Kernel32.StandardErrorHandle);

    // MARK: - Dados (stdout)

    public static void PrintLine(string line) => StandardOutput.Write(line + "\n");

    /// Pinta para a saída padrão (só se ela for um console com cores).
    public static string Paint(Style style, string text) => StandardOutput.Paint(style, text);

    // MARK: - Mensagens (sempre no stderr, para não sujar a saída de dados)

    /// `✓ mensagem` em verde.
    public static void Success(string message) =>
        StandardError.Write(StandardError.Paint(Style.Success, $"✓ {message}") + "\n");

    /// `✓ mensagem` em verde, seguida de uma dica discreta.
    public static void SuccessWithHint(string message, string hint) =>
        StandardError.Write(
            StandardError.Paint(Style.Success, $"✓ {message}") + StandardError.Paint(Style.Secondary, hint) + "\n");

    public static void Warning(string message) =>
        StandardError.Write(StandardError.Paint(Style.Warning, $"⚠ {message}") + "\n");

    public static void Note(string message) => StandardError.Write(message + "\n");

    public static void Error(SboxError error) =>
        StandardError.Write(StandardError.Paint(Style.Failure, "erro: ") + error.Message + "\n");

    /// Devolve o modo original dos consoles em que ligamos as cores (o console é
    /// compartilhado com o shell que chamou o sbox).
    public static void RestoreConsoleModes()
    {
        StandardOutput.RestoreMode();
        StandardError.RestoreMode();
    }

    // MARK: - Perguntas

    /// Pergunta s/N. Sem terminal, a resposta é não.
    ///
    /// No Unix a pergunta vai pelo `/dev/tty`, mesmo com o stdin redirecionado. No Windows
    /// ela só acontece se o próprio stdin for o console: com o stdin vindo de pipe ou
    /// arquivo (um script, um agente de IA, a suíte de testes), a resposta é não, sem
    /// perguntar. Assim ninguém fica preso numa pergunta que não pediu, e quem quer apagar
    /// sem console passa `-y`.
    public static bool Confirm(string question)
    {
        if (Console.IsInputRedirected)
        {
            return false;
        }
        using SafeFileHandle input = OpenConsole("CONIN$", Kernel32.GenericRead | Kernel32.GenericWrite);
        using SafeFileHandle output = OpenConsole("CONOUT$", Kernel32.GenericRead | Kernel32.GenericWrite);
        if (input.IsInvalid || output.IsInvalid)
        {
            return false;
        }
        string prompt = $"{question} [s/N] ";
        if (!Kernel32.WriteConsoleW(output, prompt, (uint)prompt.Length, out _, 0))
        {
            return false;
        }
        string answer = ReadConsoleLine(input).Trim().ToLowerInvariant();
        return answer is "s" or "sim" or "y" or "yes";
    }

    private static SafeFileHandle OpenConsole(string name, uint access) =>
        Kernel32.CreateFile(
            name, access, Kernel32.FileShareRead | Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, 0, 0);

    /// Lê uma linha do console em UTF-16 (o modo de linha padrão já ecoa e trata o Enter).
    private static unsafe string ReadConsoleLine(SafeFileHandle input)
    {
        StringBuilder line = new();
        char* buffer = stackalloc char[256];
        while (Kernel32.ReadConsoleW(input, buffer, 256, out uint read, 0) && read > 0)
        {
            line.Append(new ReadOnlySpan<char>(buffer, (int)read));
            if (line.Length > 0 && line[^1] == '\n')
            {
                break;
            }
        }
        return line.ToString().TrimEnd('\r', '\n');
    }

    // MARK: - Um canal de saída

    private sealed class OutputStream
    {
        private readonly int standardHandle;
        private readonly nint handle;
        private readonly bool isConsole;
        private readonly uint originalMode;
        private readonly bool supportsColor;
        private bool changedMode;
        private Stream? fileStream;

        public OutputStream(int standardHandle)
        {
            this.standardHandle = standardHandle;
            handle = Kernel32.GetStdHandle(standardHandle);
            isConsole = handle != 0 && handle != -1 && Kernel32.GetConsoleMode(handle, out originalMode);
            supportsColor = isConsole && Environment.GetEnvironmentVariable("NO_COLOR") is null && EnableColors();
        }

        public string Paint(Style style, string text) =>
            supportsColor ? $"\x1b[38;2;{Rgb(style)}m{text}\x1b[0m" : text;

        public void Write(string text)
        {
            if (isConsole && Kernel32.WriteConsoleW(handle, text, (uint)text.Length, out _, 0))
            {
                return;
            }
            try
            {
                fileStream ??= OpenStandardStream();
                fileStream.Write(Utf8.GetBytes(text));
                fileStream.Flush();
            }
            catch (IOException)
            {
                // Leitor do pipe fechou (ex.: `sbox ls | Select-Object -First 1`). Como o
                // Rust com SIGPIPE, não há a quem avisar: segue em silêncio.
            }
        }

        public void RestoreMode()
        {
            if (changedMode)
            {
                Kernel32.SetConsoleMode(handle, originalMode);
                changedMode = false;
            }
        }

        /// As cores são sequências ANSI, que o console só entende com o processamento de
        /// terminal virtual ligado (o Windows Terminal já liga; o conhost antigo, não).
        private bool EnableColors()
        {
            if ((originalMode & Kernel32.EnableVirtualTerminalProcessing) != 0)
            {
                return true;
            }
            changedMode = Kernel32.SetConsoleMode(handle, originalMode | Kernel32.EnableVirtualTerminalProcessing);
            return changedMode;
        }

        private Stream OpenStandardStream() =>
            standardHandle == Kernel32.StandardErrorHandle ? Console.OpenStandardError() : Console.OpenStandardOutput();

        private static string Rgb(Style style) => style switch
        {
            Style.Secondary => "108;112;134",
            Style.Accent => "137;180;250",
            Style.Success => "166;227;161",
            Style.Warning => "249;226;175",
            Style.Failure => "243;139;168",
            _ => "205;214;244",
        };
    }
}
