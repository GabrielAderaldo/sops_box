// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Arquivos e diretórios, com os nomes do `FileManager` da Foundation (espelho de
// src/file_manager.rs). Regra da casa: nunca sobrescrever um arquivo.
//
// Publicação: o Rust grava num temporário e publica com link(), que falha se o destino
// existe. Aqui a publicação é File.Move(temporário, destino, overwrite: false), que vira
// MoveFileExW sem MOVEFILE_REPLACE_EXISTING: falha com ERROR_ALREADY_EXISTS se o destino
// existe, e no mesmo volume é um rename atômico (o destino aparece inteiro ou não aparece).
// Preferido ao CreateHardLinkW porque hard link não existe em FAT/exFAT (pendrives) nem
// em alguns compartilhamentos de rede, e não precisa de P/Invoke.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sbox;

internal static class FileManager
{
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;

    public static bool FileExists(string atPath) => Path.Exists(atPath);

    public static byte[] Contents(string atPath)
    {
        try
        {
            return File.ReadAllBytes(atPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("abrir", atPath, exception);
        }
    }

    /// Nomes das entradas do diretório, em ordem (ordinal, como a ordenação de String do Rust).
    public static List<string> ContentsOfDirectory(string atPath)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(atPath)
                .Select(path => Path.GetFileName(path))
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("ler o diretório", atPath, exception);
        }
    }

    /// Cria `atPath` com `contents` sem nunca sobrescrever nada: grava num temporário
    /// (CreateNew), força o disco (Flush(true), o fsync do Windows) e só então publica com
    /// File.Move sem sobrescrever. Se qualquer passo falhar, `atPath` não é criado e o
    /// temporário é removido: nada de arquivo vazio ou pela metade.
    public static void CreateFileWithoutOverwriting(string atPath, byte[] contents)
    {
        string temporaryPath = $".sbox-tmp-{Environment.ProcessId}";
        try
        {
            WriteSynchronously(temporaryPath, contents);
            MoveWithoutOverwriting(temporaryPath, atPath);
        }
        finally
        {
            // Depois de publicado, o temporário já não existe e o Delete não faz nada.
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Sem como limpar; o erro que importa é o de cima, se houve.
            }
        }
    }

    /// Renomeia sem sobrescrever o destino.
    public static void MoveItemWithoutOverwriting(string atPath, string toPath) =>
        MoveWithoutOverwriting(atPath, toPath);

    public static void RemoveItem(string atPath)
    {
        try
        {
            File.Delete(atPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("apagar", atPath, exception);
        }
    }

    public static string CurrentDirectoryPath() => Environment.CurrentDirectory;

    public static void ChangeCurrentDirectoryPath(string toPath)
    {
        try
        {
            Environment.CurrentDirectory = toPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Failure("entrar em", toPath, exception);
        }
    }

    private static void MoveWithoutOverwriting(string atPath, string toPath)
    {
        try
        {
            File.Move(atPath, toPath, overwrite: false);
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) is ErrorFileExists or ErrorAlreadyExists)
        {
            throw SboxError.FileAlreadyExists(toPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("criar", toPath, exception);
        }
    }

    private static void WriteSynchronously(string path, byte[] contents)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("criar", path, exception);
        }
        using (file)
        {
            try
            {
                file.Write(contents);
            }
            catch (IOException exception)
            {
                throw Failure("gravar", path, exception);
            }
            try
            {
                file.Flush(flushToDisk: true);
            }
            catch (IOException exception)
            {
                throw Failure("sincronizar (FlushFileBuffers)", path, exception);
            }
        }
    }

    private static SboxError Failure(string action, string path, Exception reason) =>
        SboxError.FileOperationFailed(action, path, reason.Message);
}
