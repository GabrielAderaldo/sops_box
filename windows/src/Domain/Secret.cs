// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Um secret decifrado: `CHAVE: valor`. A chave fica como texto porque vem do arquivo, não
// do usuário (um bucket editado à mão pode ter chaves que o `set` recusaria).

namespace Sbox.Domain;

internal sealed record Secret(string Key, SecretValue Value);
