// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O binário `sops` atrás da porta ISops (espelho de src/sops.rs). Todo comando roda com o
// diretório atual = cofre, porque o sops procura o `.sops.yaml` subindo a partir do arquivo.
// Todo código de saída é conferido: falha silenciosa aqui significa secret perdido.

using System.Collections.Generic;
using System.Text;
using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Infrastructure;

internal sealed class SopsProcess(string executable) : ISops
{
    /// `%SOPS_BIN%`, ou `sops` do PATH (o Windows acrescenta o `.exe` na busca).
    public static SopsProcess FromEnvironment(IEnvironment environment) =>
        new(environment.Variable("SOPS_BIN") ?? "sops");

    /// No Unix o Rust passa `/dev/stdin` como arquivo. No Windows não há `/dev/stdin` (o sops
    /// tenta abrir `C:\dev\stdin`), então o arquivo é omitido: sem argumento posicional, o
    /// `sops encrypt` (3.10+) lê o stdin inteiro na memória (`io.ReadAll(os.Stdin)` em
    /// cmd/sops/encrypt.go) e exige o `--filename-override`, que já usamos para o nome casar
    /// com o `path_regex` do `.sops.yaml`.
    public byte[] Encrypt(string yaml, BucketName bucket) =>
        Run(
            new StandardInput.Data(Encoding.UTF8.GetBytes(yaml)),
            "encrypt", "--input-type", "yaml", "--output-type", "yaml", "--filename-override", bucket.FileName);

    public IReadOnlyList<Secret> Decrypt(BucketName bucket) =>
        JsonDecoder.DecodeSecrets(
            Run(new StandardInput.Nothing(), "decrypt", "--output-type", "json", bucket.FileName));

    /// O valor vai codificado em JSON pelo stdin (`--value-stdin`); nos argumentos só o
    /// arquivo e o caminho da chave, que não são segredo.
    public void Set(SecretKey key, SecretValue value, BucketName bucket) =>
        Run(
            new StandardInput.Data(Encoding.UTF8.GetBytes(JsonEncoder.EncodeString(value.Text))),
            "set", "--value-stdin", bucket.FileName, JsonEncoder.EncodeKeyPath(key.Text));

    public void Unset(string key, BucketName bucket) =>
        Run(new StandardInput.Nothing(), "unset", bucket.FileName, JsonEncoder.EncodeKeyPath(key));

    public void EncryptInPlace(BucketName bucket) =>
        Run(new StandardInput.Nothing(), "encrypt", "-i", bucket.FileName);

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
