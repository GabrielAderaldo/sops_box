// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Transforma `argv` num `Command` tipado. Depois daqui ninguém mais olha
// string de flag: cada comando já chega com os campos que precisa.

use std::collections::{HashMap, HashSet, VecDeque};
use std::ops::RangeInclusive;
use std::time::Duration;

use crate::error::{Result, SboxError};

pub const VERSION: &str = env!("CARGO_PKG_VERSION");

pub const USAGE: &str = concat!(
    "sbox ",
    env!("CARGO_PKG_VERSION"),
    " — interface para o cofre sops

Uso: sbox [--dir <cofre>] <comando> [argumentos]

Buckets
  ls [-a]                       lista os buckets (-a inclui os ocultos)
  new <bucket>                  cria um bucket vazio
  mv <bucket> <novo-nome>       renomeia
  del <bucket> [-y]             apaga (pede confirmação)
  encrypt <bucket>              encripta um bucket que está em texto puro
  edit <bucket>                 abre no $EDITOR via `sops edit`

Secrets
  show <bucket> [-r]            lista as chaves (-r revela os valores)
  get <bucket> <CHAVE> [-c]     imprime o valor (-c copia; limpa em 45s,
                                ajuste com --clear <seg>, 0 desliga)
  set <bucket> <CHAVE> [valor]  cria/atualiza; sem valor, pergunta escondido
                                ou lê do stdin (prefira: valor na linha de
                                comando vai pro histórico do shell)
  rm <bucket> <CHAVE> [-y]      remove a chave
  run <bucket> -- <cmd...>      roda o comando com os secrets como variáveis
                                de ambiente

<bucket> pode ser o nome (gitlab) ou o arquivo (gitlab.enc.yaml).
Cofre: --dir, $SOPS_BOX_DIR ou ~/secrets. Binário do sops: $SOPS_BIN."
);

const DEFAULT_CLEAR_DELAY: Duration = Duration::from_secs(45);

pub enum Command {
    ShowHelp,
    ShowVersion,

    ListBuckets { including_hidden: bool },
    CreateBucket { bucket: String },
    RenameBucket { bucket: String, new_name: String },
    DeleteBucket { bucket: String, skip_confirmation: bool },
    EncryptBucket { bucket: String },
    EditBucket { bucket: String },

    ShowSecrets { bucket: String, reveal_values: bool },
    GetSecret { bucket: String, key: String, destination: SecretDestination },
    SetSecret { bucket: String, key: String, value: Option<String> },
    RemoveSecret { bucket: String, key: String, skip_confirmation: bool },
    RunWithSecrets { bucket: String, command: Vec<String> },
}

/// Para onde vai o valor do `get`.
pub enum SecretDestination {
    StandardOutput,
    Pasteboard { clearing_after: Option<Duration> },
}

pub struct Invocation {
    /// O `--dir`, se veio.
    pub vault_directory: Option<String>,
    pub command: Command,
}

impl Invocation {
    pub fn parse(argv: impl Iterator<Item = String>) -> Result<Invocation> {
        let mut arguments = RawArguments::parse(argv)?;
        let vault_directory = arguments.options.remove("--dir");
        let command = Command::from(&mut arguments)?;
        Ok(Invocation { vault_directory, command })
    }
}

impl Command {
    fn from(arguments: &mut RawArguments) -> Result<Command> {
        if arguments.has_flag("--version") {
            return Ok(Command::ShowVersion);
        }
        let Some(name) = arguments.positional.pop_front().filter(|name| name != "help") else {
            return Ok(Command::ShowHelp);
        };
        if arguments.has_flag("--help") {
            return Ok(Command::ShowHelp);
        }

        let command = match name.as_str() {
            "ls" | "list" => {
                arguments.validate("ls", 0..=0, &["--all"])?;
                Command::ListBuckets { including_hidden: arguments.has_flag("--all") }
            }
            "new" => {
                arguments.validate("new", 1..=1, &[])?;
                Command::CreateBucket { bucket: arguments.next_positional() }
            }
            "mv" => {
                arguments.validate("mv", 2..=2, &[])?;
                Command::RenameBucket { bucket: arguments.next_positional(), new_name: arguments.next_positional() }
            }
            "del" => {
                arguments.validate("del", 1..=1, &["--yes"])?;
                Command::DeleteBucket {
                    bucket: arguments.next_positional(),
                    skip_confirmation: arguments.has_flag("--yes"),
                }
            }
            "encrypt" => {
                arguments.validate("encrypt", 1..=1, &[])?;
                Command::EncryptBucket { bucket: arguments.next_positional() }
            }
            "edit" => {
                arguments.validate("edit", 1..=1, &[])?;
                Command::EditBucket { bucket: arguments.next_positional() }
            }
            "show" => {
                arguments.validate("show", 1..=1, &["--reveal"])?;
                Command::ShowSecrets {
                    bucket: arguments.next_positional(),
                    reveal_values: arguments.has_flag("--reveal"),
                }
            }
            "get" => {
                arguments.validate("get", 2..=2, &["--copy"])?;
                let destination = if arguments.has_flag("--copy") {
                    SecretDestination::Pasteboard { clearing_after: arguments.clear_delay()? }
                } else {
                    SecretDestination::StandardOutput
                };
                Command::GetSecret {
                    bucket: arguments.next_positional(),
                    key: arguments.next_positional(),
                    destination,
                }
            }
            "set" => {
                arguments.validate("set", 2..=3, &[])?;
                Command::SetSecret {
                    bucket: arguments.next_positional(),
                    key: arguments.next_positional(),
                    value: arguments.positional.pop_front(),
                }
            }
            "rm" => {
                arguments.validate("rm", 2..=2, &["--yes"])?;
                Command::RemoveSecret {
                    bucket: arguments.next_positional(),
                    key: arguments.next_positional(),
                    skip_confirmation: arguments.has_flag("--yes"),
                }
            }
            "run" => {
                arguments.validate("run", 1..=1, &[])?;
                guard!(!arguments.after_separator.is_empty(), else: SboxError::MissingCommandToRun);
                Command::RunWithSecrets {
                    bucket: arguments.next_positional(),
                    command: std::mem::take(&mut arguments.after_separator),
                }
            }
            _ => return Err(SboxError::UnknownCommand(name)),
        };
        Ok(command)
    }
}

// MARK: - Argumentos crus

/// `argv` separado em posicionais, flags (`-r`, `--reveal`), opções com valor
/// (`--dir x`, `--dir=x`) e o que vem depois de `--`.
struct RawArguments {
    positional: VecDeque<String>,
    flags: HashSet<&'static str>,
    unknown_flags: Vec<String>,
    options: HashMap<&'static str, String>,
    after_separator: Vec<String>,
}

impl RawArguments {
    fn parse(argv: impl Iterator<Item = String>) -> Result<RawArguments> {
        let mut arguments = RawArguments {
            positional: VecDeque::new(),
            flags: HashSet::new(),
            unknown_flags: Vec::new(),
            options: HashMap::new(),
            after_separator: Vec::new(),
        };
        let mut argv = argv;
        while let Some(argument) = argv.next() {
            if argument == "--" {
                arguments.after_separator.extend(argv.by_ref());
                break;
            }
            if let Some((name, value)) = argument.split_once('=').filter(|_| argument.starts_with("--"))
                && let Some(option) = canonical_option(name)
            {
                arguments.options.insert(option, value.into());
            } else if let Some(option) = canonical_option(&argument) {
                let value = argv.next().ok_or(SboxError::MissingOptionValue { option: argument })?;
                arguments.options.insert(option, value);
            } else if argument.starts_with('-') && argument.len() > 1 {
                match canonical_flag(&argument) {
                    Some(flag) => _ = arguments.flags.insert(flag),
                    None => arguments.unknown_flags.push(argument),
                }
            } else {
                arguments.positional.push_back(argument);
            }
        }
        Ok(arguments)
    }

    fn has_flag(&self, flag: &str) -> bool {
        self.flags.contains(flag)
    }

    /// Confere as flags aceitas e a quantidade de posicionais de um comando.
    fn validate(
        &self,
        command: &'static str,
        positional_count: RangeInclusive<usize>,
        accepted_flags: &[&str],
    ) -> Result<()> {
        let unexpected = self
            .unknown_flags
            .first()
            .cloned()
            .or_else(|| self.flags.iter().find(|flag| !accepted_flags.contains(flag)).map(|flag| flag.to_string()));
        if let Some(option) = unexpected {
            return Err(SboxError::UnknownOption { command, option });
        }
        guard!(positional_count.contains(&self.positional.len()), else: SboxError::WrongArgumentCount { command });
        Ok(())
    }

    /// Só chame depois de `validate`, que garante que o posicional existe.
    fn next_positional(&mut self) -> String {
        self.positional.pop_front().expect("validate conferiu a quantidade de posicionais")
    }

    /// `--clear <segundos>`; 0 desliga a limpeza.
    fn clear_delay(&self) -> Result<Option<Duration>> {
        let Some(text) = self.options.get("--clear") else { return Ok(Some(DEFAULT_CLEAR_DELAY)) };
        let seconds: u64 = text.parse().map_err(|_| SboxError::InvalidClearDelay)?;
        Ok((seconds > 0).then(|| Duration::from_secs(seconds)))
    }
}

fn canonical_option(argument: &str) -> Option<&'static str> {
    match argument {
        "-d" | "--dir" => Some("--dir"),
        "--clear" => Some("--clear"),
        _ => None,
    }
}

fn canonical_flag(argument: &str) -> Option<&'static str> {
    match argument {
        "-a" | "--all" => Some("--all"),
        "-r" | "--reveal" => Some("--reveal"),
        "-c" | "--copy" => Some("--copy"),
        "-y" | "--yes" => Some("--yes"),
        "-h" | "--help" => Some("--help"),
        "-V" | "--version" => Some("--version"),
        _ => None,
    }
}
