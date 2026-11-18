// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O console do Windows atrás da porta ITerminal (a parte de E/S do src/console.rs; o texto e
// as cores são formatados pela Presentation).
//
// Saída: num console, o texto vai em UTF-16 por WriteConsoleW, porque o console do Windows
// interpretaria bytes UTF-8 na página de código dele (850, 1252…) e estragaria acentos e as
// bolinhas do `show`. Em pipe ou arquivo, vão bytes UTF-8 sem BOM e "\n" puro, como no Rust:
// a suíte compara bytes.

using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Sbox.Application.Ports;
using Sbox.Domain;
using Sbox.Infrastructure.Interop;

namespace Sbox.Infrastructure;

internal sealed class Win32Terminal : ITerminal
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly OutputChannel standardOutput = new(Kernel32.StandardOutputHandle);
    private readonly OutputChannel standardError = new(Kernel32.StandardErrorHandle);

    // O console cujo eco está desligado agora, e o modo a devolver. Estado do objeto só para o
    // handler do Ctrl+C (que roda em outra thread) conseguir religar o eco: um Ctrl+C no meio
    // da pergunta escondida não pode deixar o console do usuário mudo (garantia 6).
    private readonly Lock silencedInputLock = new();
    private SafeFileHandle? silencedInput;
    private uint originalInputMode;

    /// Ctrl+C em qualquer ponto devolve o eco e o modo original do console antes de o processo
    /// morrer: o console é do shell de quem chamou, e ele não pode ficar com o modo que o sbox
    /// mudou.
    public Win32Terminal()
    {
        Console.CancelKeyPress += (_, _) => RestoreConsoleModes();
    }

    public void Write(TerminalStream stream, string text) => Channel(stream).Write(text);

    public bool SupportsColor(TerminalStream stream) => Channel(stream).SupportsColor;

    public string? AskLine(string prompt) => Ask(prompt, hidden: false);

    public string? AskHiddenLine(string prompt) => Ask(prompt, hidden: true);

    public byte[] ReadStandardInput()
    {
        using Stream input = Console.OpenStandardInput();
        using MemoryStream buffer = new();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// Devolve o eco (se uma pergunta escondida estiver no meio) e o modo original dos consoles
    /// em que ligamos as cores (o console é compartilhado com o shell que chamou o sbox).
    public void RestoreConsoleModes()
    {
        RestoreEcho();
        standardOutput.RestoreMode();
        standardError.RestoreMode();
    }

    /// No Unix a pergunta vai pelo `/dev/tty`, mesmo com o stdin redirecionado. No Windows
    /// ela só acontece se o próprio stdin for o console: com o stdin vindo de pipe ou arquivo
    /// (um script, um agente de IA, a suíte de testes), não há a quem perguntar. Assim
    /// ninguém fica preso numa pergunta que não pediu.
    private string? Ask(string prompt, bool hidden)
    {
        bool standardInputIsRedirected = Console.IsInputRedirected;
        if (standardInputIsRedirected)
        {
            return null; // nem abre o console
        }
        using SafeFileHandle input = OpenConsole("CONIN$");
        using SafeFileHandle output = OpenConsole("CONOUT$");
        if (!PromptRules.CanAskInConsole(standardInputIsRedirected, !input.IsInvalid && !output.IsInvalid))
        {
            return null;
        }
        if (!Kernel32.WriteConsoleW(output, prompt, (uint)prompt.Length, out _, 0))
        {
            return null;
        }
        if (!hidden)
        {
            return ReadConsoleLine(input);
        }

        if (!SilenceEcho(input))
        {
            return null; // sem desligar o eco, não pergunta segredo nenhum
        }
        try
        {
            return ReadConsoleLine(input);
        }
        finally
        {
            RestoreEcho();
            // O Enter não foi ecoado: a próxima saída começa numa linha nova.
            Kernel32.WriteConsoleW(output, "\n", 1, out _, 0);
        }
    }

    /// Desliga só o eco; o modo de linha e o Ctrl+C continuam como estavam.
    private bool SilenceEcho(SafeFileHandle input)
    {
        lock (silencedInputLock)
        {
            if (!Kernel32.GetConsoleMode(input, out uint mode)
                || !Kernel32.SetConsoleMode(input, mode & ~Kernel32.EnableEchoInput))
            {
                return false;
            }
            silencedInput = input;
            originalInputMode = mode;
            return true;
        }
    }

    private void RestoreEcho()
    {
        lock (silencedInputLock)
        {
            if (silencedInput is { IsClosed: false } input)
            {
                Kernel32.SetConsoleMode(input, originalInputMode);
            }
            silencedInput = null;
        }
    }

    private OutputChannel Channel(TerminalStream stream) =>
        stream == TerminalStream.StandardError ? standardError : standardOutput;

    private static SafeFileHandle OpenConsole(string name) =>
        Kernel32.CreateFile(
            name, Kernel32.GenericRead | Kernel32.GenericWrite, Kernel32.FileShareRead | Kernel32.FileShareWrite,
            0, Kernel32.OpenExisting, 0, 0);

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

    private sealed class OutputChannel
    {
        private readonly int standardHandle;
        private readonly nint handle;
        private readonly bool isConsole;
        private readonly uint originalMode;
        private bool changedMode;
        private Stream? fileStream;

        public OutputChannel(int standardHandle)
        {
            this.standardHandle = standardHandle;
            handle = Kernel32.GetStdHandle(standardHandle);
            isConsole = handle != 0 && handle != -1 && Kernel32.GetConsoleMode(handle, out originalMode);
            SupportsColor = isConsole && Environment.GetEnvironmentVariable("NO_COLOR") is null && EnableColors();
        }

        public bool SupportsColor { get; }

        public void Write(string text)
        {
            if (isConsole && Kernel32.WriteConsoleW(handle, text, (uint)text.Length, out _, 0))
            {
                return;
            }
            try
            {
                fileStream ??= standardHandle == Kernel32.StandardErrorHandle
                    ? Console.OpenStandardError()
                    : Console.OpenStandardOutput();
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
    }
}
