// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O nome de um bucket (a parte de `Bucket::named` do src/bucket.rs). Value object: só existe
// se for válido, então quem recebe um BucketName não precisa conferir de novo.
//
// É uma classe `sealed record` com construtor privado, e não um `record struct`, porque o
// `default` de uma struct criaria um nome vazio sem passar pela validação.

using System;
using System.IO;
using System.Linq;

namespace Sbox.Domain;

internal sealed record BucketName
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

    private BucketName(string fileName)
    {
        FileName = fileName;
    }

    /// O arquivo, relativo ao cofre: `gitlab.enc.yaml`.
    public string FileName { get; }

    /// O nome sem a extensão, como o usuário digita: `gitlab`.
    public string Name =>
        Extensions.FirstOrDefault(extension => FileName.EndsWith(extension, StringComparison.Ordinal)) is string extension
            ? FileName[..^extension.Length]
            : FileName;

    public bool IsHidden => FileName.StartsWith('.');

    /// `gitlab` → `gitlab.enc.yaml`; `gitlab.enc.yaml` fica como está.
    /// Não confere se o arquivo existe.
    public static BucketName Parse(string input)
    {
        if (!IsValidName(input))
        {
            throw SboxError.InvalidBucketName(input);
        }
        if (IsBucketFileName(input))
        {
            return new BucketName(input);
        }
        if (OtherFormats.Any(extension => input.EndsWith(extension, StringComparison.Ordinal)))
        {
            throw SboxError.MissingEncSuffix(input);
        }
        return new BucketName($"{input}.enc.yaml");
    }

    /// Uma entrada do diretório do cofre, ou nulo se não for bucket. Sem a validação do
    /// `Parse`, como no Rust: o `ls` mostra o que está na pasta, mesmo um `-x.enc.yaml` que
    /// não daria para criar pelo `sbox`.
    public static BucketName? FromDirectoryEntry(string fileName) =>
        IsBucketFileName(fileName) ? new BucketName(fileName) : null;

    private static bool IsBucketFileName(string fileName) =>
        Extensions.Any(extension => fileName.EndsWith(extension, StringComparison.Ordinal));

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
}
