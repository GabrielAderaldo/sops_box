// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox set <bucket> <CHAVE> [valor]`: cria ou atualiza. Bucket em texto puro é recusado
// (garantia 4), e o valor só é pedido depois das conferências.

using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed record WrittenSecret(SecretKey Key, BucketName Bucket, bool WasCreated);

internal sealed class SetSecret(BucketStore store, ISops sops)
{
    public WrittenSecret Execute(string bucketInput, string keyInput, ISecretInput input)
    {
        BucketName name = store.ExistingName(bucketInput);
        SecretKey key = SecretKey.Parse(keyInput);
        Bucket bucket = store.Read(name).RequiringEncryption();

        SecretValue value = input.Read(key, name);
        bool wasCreated = !bucket.Summary.ContainsKey(key.Text);
        sops.Set(key, value, name);
        return new WrittenSecret(key, name, wasCreated);
    }
}
