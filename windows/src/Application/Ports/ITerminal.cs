// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O terminal cru: escrever nos dois canais, ler o stdin e fazer perguntas. Fica em
// Application/Ports para que a Presentation (que formata) e a Infrastructure (que fala com o
// console do Windows) dependam só para dentro. Os casos de uso nunca recebem esta porta: eles
// devolvem resultados, e quem escreve é o presenter.

namespace Sbox.Application.Ports;

internal enum TerminalStream
{
    StandardOutput,
    StandardError,
}

internal interface ITerminal
{
    public void Write(TerminalStream stream, string text);

    /// O canal é um console que aceita cores (e o usuário não pediu NO_COLOR).
    public bool SupportsColor(TerminalStream stream);

    /// Mostra `prompt` e lê uma linha do console, ou nulo se não há a quem perguntar.
    public string? AskLine(string prompt);

    /// Como `AskLine`, mas sem eco (para segredos); o eco volta mesmo com Ctrl+C.
    public string? AskHiddenLine(string prompt);

    /// O stdin inteiro, em bytes.
    public byte[] ReadStandardInput();
}
