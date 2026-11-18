// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O cofre: um diretório com `.sops.yaml` e buckets `*.enc.yaml` (espelho de src/vault.rs).

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sbox;

internal sealed record Vault(string Directory)
{
    /// `--dir`, depois `%SOPS_BOX_DIR%`, depois `%USERPROFILE%\secrets` (o `~/secrets` do Unix).
    public static Vault Locate(string? explicitDirectory)
    {
        string directory = explicitDirectory
            ?? ProcessInfo.Environment("SOPS_BOX_DIR")
            ?? (ProcessInfo.Environment("USERPROFILE") is string profile ? Path.Combine(profile, "secrets") : null)
            ?? throw SboxError.VaultLocationUnknown();
        if (!FileManager.FileExists(Path.Combine(directory, ".sops.yaml")))
        {
            throw SboxError.NotAVault(directory);
        }
        return new Vault(directory);
    }

    /// Os buckets em ordem alfabética. Ocultos (`.algo.enc.yaml`) só se pedidos.
    /// Relativo ao diretório atual, que o `SboxCommands` já trocou para o cofre.
    public static List<Bucket> Buckets(bool includingHidden) =>
        FileManager.ContentsOfDirectory(".")
            .Where(Bucket.IsBucketFileName)
            .Select(fileName => new Bucket(fileName))
            .Where(bucket => includingHidden || !bucket.IsHidden)
            .ToList();

    /// O bucket com esse nome, que precisa já existir.
    public static Bucket ExistingBucket(string named)
    {
        Bucket bucket = Bucket.Named(named);
        return bucket.Exists ? bucket : throw SboxError.BucketNotFound(bucket.Name);
    }
}
