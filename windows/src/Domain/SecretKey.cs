// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O nome de um secret, como o usuário digita no `set` (a checagem do `set_secret` do Rust).
// Value object: vazio ou com quebra de linha não chega a existir.

namespace Sbox.Domain;

internal sealed record SecretKey
{
    private SecretKey(string text)
    {
        Text = text;
    }

    public string Text { get; }

    public static SecretKey Parse(string text) =>
        text.Length > 0 && !text.Contains('\n') ? new SecretKey(text) : throw SboxError.InvalidKey();

    public override string ToString() => Text;
}
