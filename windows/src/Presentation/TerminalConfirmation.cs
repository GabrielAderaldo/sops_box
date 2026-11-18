// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// A porta IConfirmation pelo terminal: o presenter formata a pergunta s/N, o terminal
// pergunta (ou devolve nulo, se não há a quem perguntar) e as regras puras do Domain leem a
// resposta.

using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Presentation;

internal sealed class TerminalConfirmation(ITerminal terminal) : IConfirmation
{
    public bool Confirm(ConfirmationRequest request) =>
        ConfirmationRules.IsAffirmative(terminal.AskLine(Presenter.Question(request)));
}
