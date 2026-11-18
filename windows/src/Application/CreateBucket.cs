// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox new <bucket>`: encripta o YAML inicial em memória e publica o ciphertext sem
// sobrescrever. O texto puro nunca vai para o disco (garantia 2), e saída do sops sem o bloco
// `sops:` é recusada antes de gravar (garantia 3, no Ciphertext).

using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed class CreateBucket(BucketStore store, ISops sops, IFileSystem fileSystem)
{
    private const string EmptyBucketYaml = "{}\n";

    public BucketName Execute(string input)
    {
        BucketName name = BucketName.Parse(input);
        if (store.Exists(name))
        {
            throw SboxError.BucketAlreadyExists(name.Name);
        }
        Ciphertext ciphertext = Ciphertext.FromSopsOutput(sops.Encrypt(EmptyBucketYaml, name));
        fileSystem.CreateFileWithoutOverwriting(name.FileName, ciphertext.Bytes);
        return name;
    }
}
