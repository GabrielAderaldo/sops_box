// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox encrypt <bucket>`: a única saída oferecida para um bucket em texto puro. Se já estiver
// encriptado, não faz nada (e diz isso).

using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed record EncryptedBucket(BucketName Bucket, bool WasAlreadyEncrypted);

internal sealed class EncryptBucket(BucketStore store, ISops sops)
{
    public EncryptedBucket Execute(string bucketInput)
    {
        BucketName name = store.ExistingName(bucketInput);
        if (store.Read(name).Summary.IsEncrypted)
        {
            return new EncryptedBucket(name, WasAlreadyEncrypted: true);
        }
        sops.EncryptInPlace(name);
        return new EncryptedBucket(name, WasAlreadyEncrypted: false);
    }
}
