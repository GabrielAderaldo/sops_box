// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Pede um "sim" antes de uma ação destrutiva. O caso de uso diz o que vai acontecer (um
// pedido tipado, sem texto); quem implementa decide como perguntar e, sem terminal,
// responde não.

using Sbox.Domain;

namespace Sbox.Application.Ports;

internal interface IConfirmation
{
    public bool Confirm(ConfirmationRequest request);
}

internal abstract record ConfirmationRequest
{
    public sealed record DeleteBucket(BucketName Bucket, int KeyCount) : ConfirmationRequest;
}
