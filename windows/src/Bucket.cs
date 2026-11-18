// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Bucket = um arquivo `*.enc.yaml` do cofre. Secret = uma linha `CHAVE: valor` dentro dele
// (espelho de src/bucket.rs).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sbox;

internal sealed record Secret(string Key, string Value);

/// Sempre relativo ao diretório do cofre (o sbox roda de dentro dele).
internal sealed record Bucket(string FileName)
{
    public static readonly string[] Extensions = [".enc.yaml", ".enc.yml"];

    private static readonly string[] OtherFormats = [".yaml", ".yml", ".json", ".env"];

    // Nomes que o Windows reserva para dispositivos, com ou sem extensão: `con.enc.yaml`
    // abriria o console, não um arquivo.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// `gitlab` → `gitlab.enc.yaml`; `gitlab.enc.yaml` fica como está.
    /// Não confere se o arquivo existe — para isso, `Vault.ExistingBucket`.
    public static Bucket Named(string input)
    {
        if (!IsValidName(input))
        {
            throw SboxError.InvalidBucketName(input);
        }
        if (IsBucketFileName(input))
        {
            return new Bucket(input);
        }
        if (OtherFormats.Any(extension => input.EndsWith(extension, StringComparison.Ordinal)))
        {
            throw SboxError.MissingEncSuffix(input);
        }
        return new Bucket($"{input}.enc.yaml");
    }

    public static bool IsBucketFileName(string fileName) =>
        Extensions.Any(extension => fileName.EndsWith(extension, StringComparison.Ordinal));

    /// O nome sem a extensão, como o usuário digita: `gitlab`.
    public string Name =>
        Extensions.FirstOrDefault(extension => FileName.EndsWith(extension, StringComparison.Ordinal)) is string extension
            ? FileName[..^extension.Length]
            : FileName;

    public bool IsHidden => FileName.StartsWith('.');

    public bool Exists => FileManager.FileExists(FileName);

    /// Lê os nomes das chaves direto do YAML, sem decriptar: o sops só encripta os valores,
    /// os nomes ficam em texto claro. Barato e não precisa da chave age. Aceita CRLF, caso o
    /// arquivo tenha passado pelo Bloco de Notas ou por um git com autocrlf.
    public BucketSummary Summarize()
    {
        string contents = Encoding.UTF8.GetString(FileManager.Contents(FileName));
        List<string> keys = new();
        bool isEncrypted = false;

        foreach (string rawLine in contents.Split('\n'))
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

    /// Como `Summarize`, mas recusa bucket em texto puro.
    public BucketSummary SummarizeRequiringEncryption()
    {
        BucketSummary summary = Summarize();
        return summary.IsEncrypted ? summary : throw SboxError.BucketIsPlaintext(Name);
    }

    /// O Rust recusa vazio, `/` e `-` no começo. No Windows também `\` (separador de
    /// diretório), `:` (abriria um fluxo alternativo do NTFS: `a:b` grava dentro de `a`), os
    /// outros caracteres que o Windows não aceita em nome de arquivo e os nomes de dispositivo.
    private static bool IsValidName(string input)
    {
        if (input.Length == 0 || input.StartsWith('-') || input.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }
        string stem = input.Split('.')[0].TrimEnd(' ');
        return !ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
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

internal sealed record BucketSummary(IReadOnlyList<string> Keys, bool IsEncrypted)
{
    public bool ContainsKey(string key) => Keys.Contains(key, StringComparer.Ordinal);
}
