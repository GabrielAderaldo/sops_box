// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// As variáveis de ambiente do processo atrás da porta IEnvironment.

using System;
using Sbox.Application.Ports;

namespace Sbox.Infrastructure;

internal sealed class ProcessEnvironment : IEnvironment
{
    public string? Variable(string name) => Environment.GetEnvironmentVariable(name);
}
