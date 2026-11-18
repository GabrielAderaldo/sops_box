// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox show <bucket>` sem `-r`: nem precisa decriptar, os nomes das chaves estão em texto
// claro. O aviso de texto puro é decisão do presenter, a partir do `Summary.IsEncrypted`.

using Sbox.Domain;

namespace Sbox.Application;

internal sealed class ShowBucket(BucketStore store)
{
    public Bucket Execute(string name) => store.Read(store.ExistingName(name));
}
