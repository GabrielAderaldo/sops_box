// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox get <bucket> <CHAVE>`: decifra e devolve o valor; onde ele vai parar (stdout, com
// ou sem `\n`) é decisão do presenter.

using System.Linq;
using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed class GetSecret(BucketStore store, ISops sops)
{
    public SecretValue Execute(string bucketInput, string key)
    {
        BucketName name = store.ExistingName(bucketInput);
        store.Read(name).RequiringEncryption();
        Secret? secret = sops.Decrypt(name).FirstOrDefault(secret => secret.Key == key);
        return secret?.Value ?? throw SboxError.SecretNotFound(key, name.Name);
    }
}
