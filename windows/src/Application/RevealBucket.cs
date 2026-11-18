// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox show <bucket> -r`: decifra e devolve todos os secrets, na ordem do arquivo.

using System.Collections.Generic;
using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed class RevealBucket(BucketStore store, ISops sops)
{
    public IReadOnlyList<Secret> Execute(string bucketInput)
    {
        BucketName name = store.ExistingName(bucketInput);
        store.Read(name).RequiringEncryption();
        return sops.Decrypt(name);
    }
}
