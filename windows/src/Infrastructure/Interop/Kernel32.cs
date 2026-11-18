// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Chamadas ao console do Windows. [LibraryImport] gera o marshalling em tempo de compilação
// (compatível com NativeAOT); nada de DllImport com marshalling em tempo de execução.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sbox.Infrastructure.Interop;

internal static partial class Kernel32
{
    internal const int StandardInputHandle = -10;
    internal const int StandardOutputHandle = -11;
    internal const int StandardErrorHandle = -12;

    internal const uint EnableVirtualTerminalProcessing = 0x0004; // modo de saída
    internal const uint EnableEchoInput = 0x0004;                 // modo de entrada

    internal const uint GenericRead = 0x8000_0000;
    internal const uint GenericWrite = 0x4000_0000;
    internal const uint FileShareRead = 0x1;
    internal const uint FileShareWrite = 0x2;
    internal const uint OpenExisting = 3;

    /// O handle devolvido é do processo e não pode ser fechado: por isso `nint`, e não SafeHandle.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GetStdHandle(int standardHandle);

    /// Só funciona em handle de console: é assim que se distingue console de pipe ou arquivo.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetConsoleMode(nint consoleHandle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetConsoleMode(nint consoleHandle, uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetConsoleMode(SafeFileHandle consoleHandle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetConsoleMode(SafeFileHandle consoleHandle, uint mode);

    /// Escreve UTF-16 direto no console, sem passar pela página de código (850, 1252…) dele.
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteConsoleW(
        SafeFileHandle consoleOutput, string buffer, uint charactersToWrite, out uint charactersWritten, nint reserved);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteConsoleW(
        nint consoleOutput, string buffer, uint charactersToWrite, out uint charactersWritten, nint reserved);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool ReadConsoleW(
        SafeFileHandle consoleInput, char* buffer, uint charactersToRead, out uint charactersRead, nint inputControl);

    /// Abre `CONIN$`/`CONOUT$`, o equivalente ao `/dev/tty` do Unix.
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);
}
