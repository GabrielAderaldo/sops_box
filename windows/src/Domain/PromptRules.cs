// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// As regras das perguntas no console (a s/N do `del`/`rm` e a escondida do `set`), em
// funções puras: quando dá para perguntar e como ler a resposta. Ficam fora do adaptador de
// console para poderem ser conferidas sem um console de verdade (o adaptador só junta os
// fatos: stdin redirecionado? CONIN$ abriu? o que foi digitado?).

namespace Sbox.Domain;

internal static class PromptRules
{
    /// Só pergunta quando o próprio stdin é o console e o console abriu. Com o stdin vindo
    /// de pipe ou arquivo (script, agente de IA, a suíte de testes), não há a quem perguntar,
    /// mesmo que exista um console por perto: a s/N responde não e o `set` lê o stdin.
    public static bool CanAskInConsole(bool standardInputIsRedirected, bool consoleOpened) =>
        !standardInputIsRedirected && consoleOpened;

    /// `s`, `sim`, `y` ou `yes`, sem diferença de maiúsculas e ignorando espaços nas pontas.
    /// Qualquer outra coisa é não: `n`, Enter vazio, texto qualquer e nulo (sem terminal,
    /// fim da entrada ou Ctrl+C no meio da leitura). O padrão é não, como no `[s/N]`.
    public static bool IsAffirmative(string? reply) =>
        reply?.Trim().ToLowerInvariant() is "s" or "sim" or "y" or "yes";
}
