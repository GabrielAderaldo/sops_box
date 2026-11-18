// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O cofre: um diretório com `.sops.yaml` e buckets `*.enc.yaml` (o dado do src/vault.rs; a
// busca no disco fica no caso de uso OpenVault).

namespace Sbox.Domain;

internal sealed record Vault(string Directory)
{
    public const string ConfigurationFileName = ".sops.yaml";
}
