// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox mv <bucket> <novo-nome>`: renomeia sem sobrescrever o destino (garantia 7).

using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed record MovedBucket(BucketName From, BucketName To);

internal sealed class MoveBucket(BucketStore store, IFileSystem fileSystem)
{
    public MovedBucket Execute(string name, string newName)
    {
        BucketName from = store.ExistingName(name);
        BucketName to = BucketName.Parse(newName);
        fileSystem.MoveItemWithoutOverwriting(from.FileName, to.FileName);
        return new MovedBucket(from, to);
    }
}
