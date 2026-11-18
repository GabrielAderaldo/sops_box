// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox del <bucket> [-y]`: apaga depois de um "sim", ou direto com `-y`. Sem terminal, a
// confirmação responde não e nada é apagado.

using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed class DeleteBucket(BucketStore store, IFileSystem fileSystem, IConfirmation confirmation)
{
    public BucketName Execute(string input, bool skipConfirmation)
    {
        Bucket bucket = store.Read(store.ExistingName(input));
        ConfirmationRequest request = new ConfirmationRequest.DeleteBucket(bucket.Name, bucket.Summary.Keys.Count);
        if (!skipConfirmation && !confirmation.Confirm(request))
        {
            throw SboxError.Cancelled();
        }
        fileSystem.RemoveItem(bucket.Name.FileName);
        return bucket.Name;
    }
}
