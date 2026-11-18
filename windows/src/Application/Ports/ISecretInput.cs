// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// De onde vem o valor do `set`: argumento, pergunta escondida ou stdin. O caso de uso só
// pede o valor depois de conferir bucket, chave e encriptação, como o Rust: ninguém digita
// um segredo para depois descobrir que o bucket não existe.

using Sbox.Domain;

namespace Sbox.Application.Ports;

internal interface ISecretInput
{
    public SecretValue Read(SecretKey key, BucketName bucket);
}
