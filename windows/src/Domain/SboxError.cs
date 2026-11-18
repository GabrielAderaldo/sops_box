// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Tudo que pode dar errado no sbox, cada caso com o seu nome, espelhando o `enum SboxError`
// do Rust (src/error.rs). A mensagem que o usuário vê fica aqui, num lugar só, com o mesmo
// texto do Rust: a suíte de contrato confere trechos como "já existe" e "texto puro".
//
// Em C# o "enum com dados" vira uma exceção com construtores nomeados: quem lança escreve
// `throw SboxError.BucketNotFound(nome)`, e o presenter só imprime `erro: <mensagem>`. Fica no
// Domain porque os value objects lançam estes erros ao validar; as outras camadas também os usam.

using System;

namespace Sbox.Domain;

internal sealed class SboxError : Exception
{
    private SboxError(string message) : base(message)
    {
    }

    // MARK: - Linha de comando

    public static SboxError UnknownCommand(string command) =>
        new($"comando desconhecido: {command} — veja `sbox --help`");

    public static SboxError UnknownOption(string command, string option) =>
        new($"{command}: opção desconhecida {option}");

    public static SboxError WrongArgumentCount(string command) =>
        new($"{command}: argumentos errados — veja `sbox --help`");

    public static SboxError MissingOptionValue(string option) => new($"{option} precisa de um valor");

    public static SboxError InvalidClearDelay() => new("--clear espera segundos (ex.: 30)");

    public static SboxError MissingCommandToRun() =>
        new("run: faltou o comando — sbox run <bucket> -- <cmd...>");

    // MARK: - Cofre e buckets

    public static SboxError VaultLocationUnknown() =>
        new("não sei onde fica o cofre: use --dir ou SOPS_BOX_DIR");

    public static SboxError NotAVault(string directory) =>
        new($"{directory} não parece um cofre sops (falta o .sops.yaml)");

    public static SboxError InvalidBucketName(string name) => new($"nome de bucket inválido: '{name}'");

    public static SboxError MissingEncSuffix(string name) =>
        new($"'{name}': todo bucket precisa terminar em .enc.yaml (ou passe só o nome)");

    public static SboxError BucketNotFound(string bucket) => new($"bucket '{bucket}' não existe — veja `sbox ls`");

    public static SboxError BucketAlreadyExists(string bucket) => new($"bucket '{bucket}' já existe");

    public static SboxError BucketIsPlaintext(string bucket) =>
        new($"'{bucket}' está em TEXTO PURO — encripte antes: sbox encrypt {bucket}");

    // MARK: - Secrets

    public static SboxError InvalidKey() => new("nome de chave inválido");

    public static SboxError SecretNotFound(string key, string bucket) =>
        new($"'{key}' não existe em {bucket} — veja `sbox show {bucket}`");

    public static SboxError InvalidEnvironmentName(string key) => new($"'{key}' não serve como variável de ambiente");

    public static SboxError EmptyValue() => new("valor vazio, nada foi gravado");

    public static SboxError ValuesDoNotMatch() => new("os valores não batem, nada foi gravado");

    public static SboxError Cancelled() => new("cancelado");

    public static SboxError NoTerminal() =>
        new("sem terminal para perguntar (passe o valor pelo stdin ou use --dialog)");

    public static SboxError EmptyStandardInput() =>
        new("stdin vazio, nada foi gravado — sem terminal, use --dialog para perguntar numa janela "
            + "(valor vazio de propósito: sbox set <bucket> <CHAVE> \"\")");

    public static SboxError DialogWithValue() =>
        new("set: use --dialog ou o valor na linha de comando, não os dois");

    // MARK: - sops

    public static SboxError SopsFailed(string subcommand, int status, string standardError) =>
        new(standardError.Length == 0
            ? $"sops {subcommand} falhou (código {status})"
            : $"sops {subcommand} falhou (código {status}):\n{standardError}");

    public static SboxError SopsReturnedPlaintext() =>
        new("o sops terminou sem erro mas não devolveu um arquivo encriptado — nada foi gravado");

    public static SboxError InvalidJson(string reason, int offset) =>
        new($"JSON inválido vindo do sops ({reason}, byte {offset})");

    public static SboxError NestedValuesUnsupported() =>
        new("valores aninhados (mapas/listas) não são suportados — só CHAVE: valor");

    // MARK: - Sistema

    public static SboxError LaunchFailed(string program, string reason) =>
        new($"não consegui executar `{program}`: {reason}");

    public static SboxError FileAlreadyExists(string path) => new($"{path} já existe");

    public static SboxError FileOperationFailed(string action, string path, string reason) =>
        new($"não consegui {action} {path}: {reason}");

    // MARK: - Só no Windows

    /// Comando reconhecido pelo parser, mas que o port ainda não trouxe do Rust.
    public static SboxError NotYetImplemented(string command) =>
        new($"`{command}` ainda não implementado no Windows");
}
