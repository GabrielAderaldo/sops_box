// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// `sbox ls`: os buckets com as chaves lidas do YAML, sem decriptar (garantia 12).

using System.Collections.Generic;
using System.Linq;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed class ListBuckets(BucketStore store)
{
    public IReadOnlyList<Bucket> Execute(bool includingHidden) =>
        store.Names(includingHidden).Select(store.Read).ToList();
}
