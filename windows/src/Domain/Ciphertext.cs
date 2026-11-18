// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// A saída do `sops encrypt`, já conferida. Garantia 3 do ARCHITECTURE.md: código 0 do sops
// não basta; sem o bloco `sops:` a saída não é um bucket encriptado e nada é gravado. Como
// value object, a regra vale para qualquer adaptador de sops, não só para o processo real.

using System;
using System.Text;

namespace Sbox.Domain;

internal sealed record Ciphertext
{
    private Ciphertext(byte[] bytes)
    {
        Bytes = bytes;
    }

    public byte[] Bytes { get; }

    public static Ciphertext FromSopsOutput(byte[] output)
    {
        string text = Encoding.UTF8.GetString(output);
        if (!text.StartsWith("sops:", StringComparison.Ordinal) && !text.Contains("\nsops:", StringComparison.Ordinal))
        {
            throw SboxError.SopsReturnedPlaintext();
        }
        return new Ciphertext(output);
    }
}
