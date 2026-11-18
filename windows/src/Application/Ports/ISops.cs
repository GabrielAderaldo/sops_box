// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O sops, visto pelos casos de uso. Toda implementação confere o código de saída do sops e
// lança `SopsFailed` com o stderr dele: falha silenciosa aqui significa secret perdido.

using System.Collections.Generic;
using Sbox.Domain;

namespace Sbox.Application.Ports;

internal interface ISops
{
    /// Encripta YAML vindo da memória e devolve a saída do sops, sem gravar nada. O texto
    /// puro nunca toca o disco; o nome do bucket serve para casar com o `path_regex`.
    public byte[] Encrypt(string yaml, BucketName bucket);

    /// Os secrets do bucket, na ordem do arquivo.
    public IReadOnlyList<Secret> Decrypt(BucketName bucket);

    /// Cria ou atualiza no próprio arquivo. O valor vai pelo stdin do sops, nunca como
    /// argumento (garantia 1).
    public void Set(SecretKey key, SecretValue value, BucketName bucket);

    public void Unset(string key, BucketName bucket);

    /// Encripta no próprio arquivo um bucket que está em texto puro.
    public void EncryptInPlace(BucketName bucket);
}
