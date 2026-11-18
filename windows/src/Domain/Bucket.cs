// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Bucket = um arquivo `*.enc.yaml` do cofre, com o resumo lido do YAML (espelho de
// src/bucket.rs). Entidade sem I/O: quem lê o arquivo é a Application, pela porta IFileSystem.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sbox.Domain;

internal sealed record Bucket(BucketName Name, BucketSummary Summary)
{
    /// Recusa bucket em texto puro (o `summarize_requiring_encryption` do Rust).
    public Bucket RequiringEncryption() =>
        Summary.IsEncrypted ? this : throw SboxError.BucketIsPlaintext(Name.Name);
}

internal sealed record BucketSummary(IReadOnlyList<string> Keys, bool IsEncrypted)
{
    public bool ContainsKey(string key) => Keys.Contains(key, StringComparer.Ordinal);

    /// Lê os nomes das chaves direto do YAML, sem decriptar: o sops só encripta os valores,
    /// os nomes ficam em texto claro. Barato e não precisa da chave age. Aceita CRLF, caso o
    /// arquivo tenha passado pelo Bloco de Notas ou por um git com autocrlf.
    public static BucketSummary Parse(byte[] yaml)
    {
        List<string> keys = new();
        bool isEncrypted = false;

        foreach (string rawLine in Encoding.UTF8.GetString(yaml).Split('\n'))
        {
            string line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            if (line.Length == 0)
            {
                continue;
            }
            // Indentado, comentário ou item de lista: não é chave de primeiro nível.
            if (line[0] is ' ' or '\t' or '#' or '-')
            {
                continue;
            }
            if (line.StartsWith("sops:", StringComparison.Ordinal))
            {
                isEncrypted = true;
                continue;
            }
            string? key = line[0] is '"' or '\'' ? QuotedKey(line, line[0]) : PlainKey(line);
            if (key is not null)
            {
                keys.Add(key);
            }
        }
        return new BucketSummary(keys, isEncrypted);
    }

    /// `"CHAVE": valor` ou `'CHAVE': valor`.
    private static string? QuotedKey(string line, char quote)
    {
        int closing = line.IndexOf(quote, 1);
        return closing < 0 ? null : line[1..closing];
    }

    /// `CHAVE: valor`.
    private static string? PlainKey(string line)
    {
        int colon = line.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? null : line[..colon];
    }
}
