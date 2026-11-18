// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Processos filhos (espelho de src/process.rs, com outro nome para não colidir com
// System.Diagnostics.Process): monta-se o processo (executável, argumentos, entrada) e
// depois roda. Argumentos sempre por ArgumentList, nunca por string montada à mão, e
// segredo nunca em argumento: vai pelo stdin.

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Sbox.Domain;

namespace Sbox.Infrastructure;

internal abstract record StandardInput
{
    /// Nada: o filho recebe EOF logo de cara (o `/dev/null` do Rust).
    public sealed record Nothing : StandardInput;

    /// Bytes escritos num pipe. É assim que segredos chegam aos filhos, nunca como argumento
    /// (a linha de comando de qualquer processo aparece no Gerenciador de Tarefas e no WMI).
    public sealed record Data(byte[] Bytes) : StandardInput;
}

internal sealed record ProcessResult(int TerminationStatus, byte[] StandardOutput, byte[] StandardError)
{
    public bool Succeeded => TerminationStatus == 0;

    public string StandardErrorText() =>
        Encoding.UTF8.GetString(StandardError).ReplaceLineEndings("\n").TrimEnd();
}

internal sealed class ProcessRunner(string executable)
{
    private readonly List<string> arguments = new();
    private StandardInput standardInput = new StandardInput.Nothing();

    public ProcessRunner WithArguments(params string[] newArguments)
    {
        arguments.Clear();
        arguments.AddRange(newArguments);
        return this;
    }

    public ProcessRunner WithStandardInput(StandardInput input)
    {
        standardInput = input;
        return this;
    }

    /// Roda e espera terminar, com stdout e stderr capturados.
    public ProcessResult RunUntilExit()
    {
        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Launch(startInfo);
        // stdout e stderr lidos em paralelo: se o filho enchesse o buffer de um enquanto
        // esperamos o outro, os dois ficariam parados para sempre.
        Task<byte[]> standardOutput = ReadAllAsync(process.StandardOutput.BaseStream);
        Task<byte[]> standardError = ReadAllAsync(process.StandardError.BaseStream);

        try
        {
            if (standardInput is StandardInput.Data data)
            {
                process.StandardInput.BaseStream.Write(data.Bytes);
            }
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // O filho saiu sem ler o stdin (ex.: um sops que falha logo). Não é a falha a
            // relatar: o código de saída dele, conferido abaixo, é.
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult());
    }

    private Process Launch(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw SboxError.LaunchFailed(executable, "processo não iniciado");
        }
        catch (Win32Exception exception)
        {
            throw SboxError.LaunchFailed(executable, exception.Message);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
