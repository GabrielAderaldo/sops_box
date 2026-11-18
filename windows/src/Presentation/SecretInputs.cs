// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// As origens do valor do `set` (a porta ISecretInput). As regras de cada origem (vazio, `\n`
// final) ficam no SecretValue; aqui só se decide de onde ler e o texto das perguntas.

using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Presentation;

/// `sbox set b K valor`: o valor veio na linha de comando (e foi para o histórico do shell;
/// o `--help` avisa).
internal sealed class ArgumentSecretInput(string value) : ISecretInput
{
    public SecretValue Read(SecretKey key, BucketName bucket) => SecretValue.FromArgument(value);
}

/// `sbox set b K` sem valor: pergunta escondida, duas vezes, quando o stdin é o console;
/// senão lê o stdin (pipe, arquivo, agente de IA). Mesma regra da s/N do `del`.
internal sealed class TerminalSecretInput(ITerminal terminal) : ISecretInput
{
    public SecretValue Read(SecretKey key, BucketName bucket)
    {
        string? first = terminal.AskHiddenLine($"Valor de {key.Text}: ");
        if (first is null)
        {
            return SecretValue.FromStandardInput(terminal.ReadStandardInput());
        }
        SecretValue value = SecretValue.FromPrompt(first);
        string second = terminal.AskHiddenLine("Repita: ") ?? throw SboxError.NoTerminal();
        return second == first ? value : throw SboxError.ValuesDoNotMatch();
    }
}
