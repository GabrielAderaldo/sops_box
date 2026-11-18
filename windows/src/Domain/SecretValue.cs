// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O valor de um secret. As regras de cada origem ficam aqui, num lugar só:
//
// - argumento: exato, inclusive vazio (`sbox set b K ""` grava vazio de propósito);
// - pergunta escondida: vazio é recusado (`EmptyValue`);
// - stdin: se for uma linha só, sai o `\n` final que o `echo` acrescenta; com várias linhas
//   (chave SSH, certificado) fica exato, porque ali o `\n` final faz parte do formato (o fix
//   #18 do Rust). Vazio é recusado (`EmptyStandardInput`): sem terminal, um stdin fechado
//   chega vazio e gravaria "".
//
// Os bytes são os UTF-8 do texto; UTF-8 inválido vira U+FFFD, como o from_utf8_lossy do Rust
// (o sops guarda strings, não bytes soltos).

using System;
using System.Linq;
using System.Text;

namespace Sbox.Domain;

internal sealed record SecretValue
{
    private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

    private SecretValue(string text)
    {
        Text = text;
    }

    public string Text { get; }

    public byte[] Bytes => Encoding.UTF8.GetBytes(Text);

    public static SecretValue FromArgument(string text) => new(text);

    /// Valor decifrado pelo sops: exato.
    public static SecretValue FromDecrypted(string text) => new(text);

    public static SecretValue FromPrompt(string text) =>
        text.Length > 0 ? new SecretValue(text) : throw SboxError.EmptyValue();

    /// Duas diferenças do Windows, as duas para não gravar um caractere invisível:
    /// - o `echo` do PowerShell e do cmd termina a linha com `\r\n`; numa linha só, o `\r`
    ///   também sai (no Rust ficaria um `\r` no fim do valor);
    /// - o PowerShell com `$OutputEncoding` em UTF-8 manda o BOM (EF BB BF) antes do texto
    ///   no pipe para um programa nativo; um BOM no começo do stdin é descartado.
    public static SecretValue FromStandardInput(byte[] bytes)
    {
        ReadOnlySpan<byte> content = bytes.AsSpan();
        if (content.StartsWith(Utf8ByteOrderMark))
        {
            content = content[Utf8ByteOrderMark.Length..];
        }
        string text = Encoding.UTF8.GetString(content);
        if (text.EndsWith('\n') && text.Count(character => character == '\n') == 1)
        {
            text = text[..^1];
            if (text.EndsWith('\r'))
            {
                text = text[..^1];
            }
        }
        return text.Length > 0 ? new SecretValue(text) : throw SboxError.EmptyStandardInput();
    }
}
