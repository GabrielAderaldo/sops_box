// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Arquivos e diretórios atrás da porta IFileSystem (espelho de src/file_manager.rs). Regra
// da casa: nunca sobrescrever um arquivo.
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
using System.Security.AccessControl;
using System.Security.Principal;
using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Infrastructure;

internal sealed class WindowsFileSystem : IFileSystem
{
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;

    public bool Exists(string path) => Path.Exists(path);

    public byte[] Contents(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("abrir", path, exception);
        }
    }

    public IReadOnlyList<string> ContentsOfDirectory(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path)
                .Select(entry => Path.GetFileName(entry))
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("ler o diretório", path, exception);
        }
    }

    /// Grava num temporário (CreateNew), força o disco (Flush(true), o fsync do Windows) e só
    /// então publica com File.Move sem sobrescrever. Se qualquer passo falhar, `path` não é
    /// criado e o temporário é removido.
    public void CreateFileWithoutOverwriting(string path, byte[] contents)
    {
        string temporaryPath = $".sbox-tmp-{Environment.ProcessId}";
        try
        {
            WriteSynchronously(temporaryPath, contents);
            MoveWithoutOverwriting(temporaryPath, path);
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

    public void MoveItemWithoutOverwriting(string path, string toPath) => MoveWithoutOverwriting(path, toPath);

    public void RemoveItem(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("apagar", path, exception);
        }
    }

    public void ChangeCurrentDirectory(string path)
    {
        try
        {
            Environment.CurrentDirectory = path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Failure("entrar em", path, exception);
        }
    }

    private static void MoveWithoutOverwriting(string path, string toPath)
    {
        try
        {
            File.Move(path, toPath, overwrite: false);
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

    /// O `0600` do Rust: o arquivo já nasce com uma DACL protegida (sem herdar nada da pasta)
    /// que dá acesso só ao usuário atual. A ACL vai no próprio CreateFileW (pelo
    /// SECURITY_ATTRIBUTES que o FileSystemAclExtensions.Create monta), então não há instante
    /// em que o arquivo exista com a ACL herdada. O File.Move no mesmo volume preserva o
    /// descritor, e o bucket publicado fica só com o usuário.
    private static FileSecurity OwnerOnlySecurity()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User
            ?? throw new UnauthorizedAccessException("sem SID para o usuário atual");
        FileSecurity security = new();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private static void WriteSynchronously(string path, byte[] contents)
    {
        FileStream file;
        try
        {
            file = new FileInfo(path).Create(
                FileMode.CreateNew, FileSystemRights.Write, FileShare.None, bufferSize: 4096, FileOptions.None,
                OwnerOnlySecurity());
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
