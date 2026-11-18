// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// A porta IConfirmation pelo terminal: o presenter formata a pergunta s/N, o terminal
// pergunta, e sem terminal (resposta nula) é não.

using Sbox.Application.Ports;

namespace Sbox.Presentation;

internal sealed class TerminalConfirmation(ITerminal terminal) : IConfirmation
{
    public bool Confirm(ConfirmationRequest request)
    {
        string? reply = terminal.AskLine(Presenter.Question(request));
        return reply?.Trim().ToLowerInvariant() is "s" or "sim" or "y" or "yes";
    }
}
