// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Acha o cofre e entra nele (o `Vault::locate` + `Sbox::open` do Rust). Dali o sops acha o
// `.sops.yaml` e os caminhos dos buckets ficam relativos (garantia 10).

using System.IO;
using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Application;

internal sealed class OpenVault(IEnvironment environment, IFileSystem fileSystem)
{
    /// `--dir`, depois `%SOPS_BOX_DIR%`, depois `%USERPROFILE%\secrets` (o `~/secrets` do Unix).
    public Vault Execute(string? explicitDirectory)
    {
        string directory = explicitDirectory
            ?? environment.Variable("SOPS_BOX_DIR")
            ?? (environment.Variable("USERPROFILE") is string profile ? Path.Combine(profile, "secrets") : null)
            ?? throw SboxError.VaultLocationUnknown();
        if (!fileSystem.Exists(Path.Combine(directory, Vault.ConfigurationFileName)))
        {
            throw SboxError.NotAVault(directory);
        }
        fileSystem.ChangeCurrentDirectory(directory);
        return new Vault(directory);
    }
}
