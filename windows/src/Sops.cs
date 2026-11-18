// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O binário `sops` (espelho de src/sops.rs). Todo comando roda com o diretório atual = cofre,
// porque o sops procura o `.sops.yaml` subindo a partir do arquivo. Todo código de saída é
// conferido: falha silenciosa aqui significa secret perdido.

using System.Text;

namespace Sbox;

internal sealed class Sops(string executable)
{
    /// `%SOPS_BIN%`, ou `sops` do PATH (o Windows acrescenta o `.exe` na busca).
    public static Sops FromEnvironment() => new(ProcessInfo.Environment("SOPS_BIN") ?? "sops");

    /// Encripta YAML vindo da memória e devolve o ciphertext — o plaintext nunca toca o disco.
    ///
    /// No Unix o Rust passa `/dev/stdin` como arquivo. No Windows não há `/dev/stdin` (o sops
    /// tenta abrir `C:\dev\stdin`), então o arquivo é omitido: sem argumento posicional, o
    /// `sops encrypt` (3.10+) lê o stdin inteiro na memória (`io.ReadAll(os.Stdin)` em
    /// cmd/sops/encrypt.go) e exige o `--filename-override`, que já usamos para o nome casar
    /// com o `path_regex` do `.sops.yaml`.
    public byte[] Encrypt(string yaml, Bucket bucket)
    {
        byte[] ciphertext = Run(
            new StandardInput.Data(Encoding.UTF8.GetBytes(yaml)),
            "encrypt", "--input-type", "yaml", "--output-type", "yaml", "--filename-override", bucket.FileName);
        // Código 0 não basta: sem o bloco `sops:` a saída não é um bucket encriptado.
        string text = Encoding.UTF8.GetString(ciphertext);
        if (!text.StartsWith("sops:", System.StringComparison.Ordinal)
            && !text.Contains("\nsops:", System.StringComparison.Ordinal))
        {
            throw SboxError.SopsReturnedPlaintext();
        }
        return ciphertext;
    }

    /// Roda `sops <arguments>` e devolve o stdout; código ≠ 0 vira erro com o stderr do sops.
    private byte[] Run(StandardInput input, params string[] arguments)
    {
        ProcessResult result = new ProcessRunner(executable)
            .WithArguments(arguments)
            .WithStandardInput(input)
            .RunUntilExit();
        if (!result.Succeeded)
        {
            throw SboxError.SopsFailed(arguments[0], result.TerminationStatus, result.StandardErrorText());
        }
        return result.StandardOutput;
    }
}
