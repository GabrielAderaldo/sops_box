// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Arquivos e diretórios, do jeito que os casos de uso precisam (o FileManager do Rust).
// Regra de qualquer implementação: nunca sobrescrever um arquivo, e falhas viram SboxError.

using System.Collections.Generic;

namespace Sbox.Application.Ports;

internal interface IFileSystem
{
    public bool Exists(string path);

    public byte[] Contents(string path);

    /// Nomes das entradas do diretório, em ordem ordinal (a ordenação de String do Rust).
    public IReadOnlyList<string> ContentsOfDirectory(string path);

    /// Cria `path` com `contents`; falha com `FileAlreadyExists` se ele já existir. Se qualquer
    /// passo falhar, `path` não aparece (nada de arquivo vazio ou pela metade).
    public void CreateFileWithoutOverwriting(string path, byte[] contents);

    /// Renomeia; falha com `FileAlreadyExists` se o destino já existir.
    public void MoveItemWithoutOverwriting(string path, string toPath);

    public void RemoveItem(string path);

    public void ChangeCurrentDirectory(string path);
}
