// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O que vários casos de uso repetem: achar um bucket que precisa existir e ler o resumo dele.
// Os caminhos são relativos porque o OpenVault já entrou no cofre.

using System.Collections.Generic;
using System.Linq;
using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed class BucketStore(IFileSystem fileSystem)
{
    /// O bucket com esse nome, que precisa já existir (o `Vault::existing_bucket` do Rust).
    public BucketName ExistingName(string input)
    {
        BucketName name = BucketName.Parse(input);
        return fileSystem.Exists(name.FileName) ? name : throw SboxError.BucketNotFound(name.Name);
    }

    public bool Exists(BucketName name) => fileSystem.Exists(name.FileName);

    public Bucket Read(BucketName name) => new(name, BucketSummary.Parse(fileSystem.Contents(name.FileName)));

    /// Os buckets do cofre em ordem alfabética. Ocultos (`.algo.enc.yaml`) só se pedidos.
    public IReadOnlyList<BucketName> Names(bool includingHidden) =>
        fileSystem.ContentsOfDirectory(".")
            .Select(BucketName.FromDirectoryEntry)
            .OfType<BucketName>()
            .Where(name => includingHidden || !name.IsHidden)
            .ToList();
}
