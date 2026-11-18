// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Variáveis de ambiente (o `ProcessInfo` do Rust).

namespace Sbox.Application.Ports;

internal interface IEnvironment
{
    /// A variável, ou nulo se não existir.
    public string? Variable(string name);
}
