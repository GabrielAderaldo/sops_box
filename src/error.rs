// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Tudo que pode dar errado no sbox, cada caso com o seu nome — como um
// `enum SboxError: Error` da Swift. A mensagem que o usuário vê fica no
// `Display`, num lugar só.

use std::fmt;
use std::io;

pub type Result<Value> = std::result::Result<Value, SboxError>;

pub enum SboxError {
    // Linha de comando
    UnknownCommand(String),
    UnknownOption { command: &'static str, option: String },
    WrongArgumentCount { command: &'static str },
    MissingOptionValue { option: String },
    InvalidClearDelay,
    MissingCommandToRun,

    // Cofre e buckets
    VaultLocationUnknown,
    NotAVault { directory: String },
    InvalidBucketName(String),
    MissingEncSuffix(String),
    BucketNotFound { bucket: String },
    BucketAlreadyExists { bucket: String },
    BucketIsPlaintext { bucket: String },

    // Secrets
    InvalidKey,
    SecretNotFound { key: String, bucket: String },
    InvalidEnvironmentName { key: String },
    EmptyValue,
    ValuesDoNotMatch,
    Cancelled,
    NoTerminal,

    // sops
    SopsFailed { subcommand: String, status: i32, standard_error: String },
    SopsReturnedPlaintext,
    InvalidJSON { reason: &'static str, offset: usize },
    NestedValuesUnsupported,

    // Sistema
    LaunchFailed { program: String, reason: io::Error },
    ClipboardFailed { tool: &'static str, status: i32 },
    FileAlreadyExists { path: String },
    FileOperationFailed { action: &'static str, path: String, reason: io::Error },
}

impl fmt::Display for SboxError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        use SboxError::*;
        match self {
            UnknownCommand(command) => write!(formatter, "comando desconhecido: {command} — veja `sbox --help`"),
            UnknownOption { command, option } => write!(formatter, "{command}: opção desconhecida {option}"),
            WrongArgumentCount { command } => write!(formatter, "{command}: argumentos errados — veja `sbox --help`"),
            MissingOptionValue { option } => write!(formatter, "{option} precisa de um valor"),
            InvalidClearDelay => write!(formatter, "--clear espera segundos (ex.: 30)"),
            MissingCommandToRun => write!(formatter, "run: faltou o comando — sbox run <bucket> -- <cmd...>"),

            VaultLocationUnknown => write!(formatter, "não sei onde fica o cofre: use --dir ou SOPS_BOX_DIR"),
            NotAVault { directory } => write!(formatter, "{directory} não parece um cofre sops (falta o .sops.yaml)"),
            InvalidBucketName(name) => write!(formatter, "nome de bucket inválido: '{name}'"),
            MissingEncSuffix(name) => {
                write!(formatter, "'{name}': todo bucket precisa terminar em .enc.yaml (ou passe só o nome)")
            }
            BucketNotFound { bucket } => write!(formatter, "bucket '{bucket}' não existe — veja `sbox ls`"),
            BucketAlreadyExists { bucket } => write!(formatter, "bucket '{bucket}' já existe"),
            BucketIsPlaintext { bucket } => {
                write!(formatter, "'{bucket}' está em TEXTO PURO — encripte antes: sbox encrypt {bucket}")
            }

            InvalidKey => write!(formatter, "nome de chave inválido"),
            SecretNotFound { key, bucket } => {
                write!(formatter, "'{key}' não existe em {bucket} — veja `sbox show {bucket}`")
            }
            InvalidEnvironmentName { key } => write!(formatter, "'{key}' não serve como variável de ambiente"),
            EmptyValue => write!(formatter, "valor vazio, nada foi gravado"),
            ValuesDoNotMatch => write!(formatter, "os valores não batem, nada foi gravado"),
            Cancelled => write!(formatter, "cancelado"),
            NoTerminal => write!(formatter, "sem terminal para perguntar (passe o valor pelo stdin)"),

            SopsFailed { subcommand, status, standard_error } => {
                write!(formatter, "sops {subcommand} falhou (código {status})")?;
                if !standard_error.is_empty() {
                    write!(formatter, ":\n{standard_error}")?;
                }
                Ok(())
            }
            SopsReturnedPlaintext => {
                write!(formatter, "o sops terminou sem erro mas não devolveu um arquivo encriptado — nada foi gravado")
            }
            InvalidJSON { reason, offset } => {
                write!(formatter, "JSON inválido vindo do sops ({reason}, byte {offset})")
            }
            NestedValuesUnsupported => {
                write!(formatter, "valores aninhados (mapas/listas) não são suportados — só CHAVE: valor")
            }

            LaunchFailed { program, reason } => write!(formatter, "não consegui executar `{program}`: {reason}"),
            ClipboardFailed { tool, status } => write!(formatter, "{tool} falhou (código {status})"),
            FileAlreadyExists { path } => write!(formatter, "{path} já existe"),
            FileOperationFailed { action, path, reason } => write!(formatter, "não consegui {action} {path}: {reason}"),
        }
    }
}

/// O `guard` da Swift: `guard!(condição, else: erro)` sai da função com
/// `Err(erro)` quando a condição é falsa. Fica visível em todos os módulos
/// porque o `main.rs` declara `#[macro_use] mod error` antes dos outros.
macro_rules! guard {
    ($condition:expr, else: $error:expr) => {
        if !$condition {
            return Err($error);
        }
    };
}
