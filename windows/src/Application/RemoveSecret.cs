// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox rm <bucket> <CHAVE> [-y]`: remove depois de um "sim", ou direto com `-y`. A chave
// precisa existir (conferido no YAML, sem decriptar).

using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed record RemovedSecret(string Key, BucketName Bucket);

internal sealed class RemoveSecret(BucketStore store, ISops sops, IConfirmation confirmation)
{
    public RemovedSecret Execute(string bucketInput, string key, bool skipConfirmation)
    {
        BucketName name = store.ExistingName(bucketInput);
        Bucket bucket = store.Read(name).RequiringEncryption();
        if (!bucket.Summary.ContainsKey(key))
        {
            throw SboxError.SecretNotFound(key, name.Name);
        }
        if (!skipConfirmation && !confirmation.Confirm(new ConfirmationRequest.RemoveSecret(key, name)))
        {
            throw SboxError.Cancelled();
        }
        sops.Unset(key, name);
        return new RemovedSecret(key, name);
    }
}
