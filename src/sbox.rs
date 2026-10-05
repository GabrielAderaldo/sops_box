// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O que cada comando faz. `Sbox` junta o cofre, o sops e o diretório de onde
// o usuário chamou — o contexto que todos os comandos compartilham.

use std::io::{Read, Write};
use std::os::unix::process::CommandExt;

use crate::bucket::{Bucket, Secret};
use crate::command_line::{Command, SecretDestination};
use crate::console::{Console, Style};
use crate::error::{Result, SboxError};
use crate::file_manager::FileManager;
use crate::pasteboard::Pasteboard;
use crate::sops::Sops;
use crate::vault::Vault;

const MASKED_VALUE: &str = "••••••••";

pub struct Sbox {
    vault: Vault,
    sops: Sops,
    /// Onde o usuário estava; o `run` volta para cá antes de executar o comando.
    caller_directory: String,
}

impl Sbox {
    /// Entra no cofre: dali o sops acha o `.sops.yaml` e os caminhos ficam relativos.
    pub fn open(vault: Vault) -> Result<Sbox> {
        let caller_directory = FileManager::current_directory_path();
        FileManager::change_current_directory_path(&vault.directory)?;
        Ok(Sbox { vault, sops: Sops::from_environment(), caller_directory })
    }

    pub fn run(&self, command: Command) -> Result<()> {
        match command {
            Command::ShowHelp | Command::ShowVersion => unreachable!("tratados no main, antes de abrir o cofre"),

            Command::ListBuckets { including_hidden } => self.list_buckets(including_hidden),
            Command::CreateBucket { bucket } => self.create_bucket(&bucket),
            Command::RenameBucket { bucket, new_name } => self.rename_bucket(&bucket, &new_name),
            Command::DeleteBucket { bucket, skip_confirmation } => self.delete_bucket(&bucket, skip_confirmation),
            Command::EncryptBucket { bucket } => self.encrypt_bucket(&bucket),
            Command::EditBucket { bucket } => self.edit_bucket(&bucket),

            Command::ShowSecrets { bucket, reveal_values } => self.show_secrets(&bucket, reveal_values),
            Command::GetSecret { bucket, key, destination } => self.get_secret(&bucket, &key, destination),
            Command::SetSecret { bucket, key, value } => self.set_secret(&bucket, key, value),
            Command::RemoveSecret { bucket, key, skip_confirmation } => {
                self.remove_secret(&bucket, &key, skip_confirmation)
            }
            Command::RunWithSecrets { bucket, command } => self.run_with_secrets(&bucket, &command),
        }
    }

    // MARK: - Buckets

    fn list_buckets(&self, including_hidden: bool) -> Result<()> {
        let buckets = self.vault.buckets(including_hidden)?;
        if buckets.is_empty() {
            Console::note(&format!("nenhum bucket em {} — crie um com: sbox new <nome>", self.vault.directory));
            return Ok(());
        }
        let name_width = buckets.iter().map(|bucket| bucket.name().chars().count()).max().unwrap_or(0);
        for bucket in &buckets {
            let summary = bucket.summarize()?;
            let key_count = match summary.keys.len() {
                1 => "1 chave".to_owned(),
                count => format!("{count} chaves"),
            };
            let mut line = format!(
                "  {}  {}",
                Style::Accent.paint(&format!("{:<name_width$}", bucket.name())),
                Style::Secondary.paint(&key_count)
            );
            if !summary.is_encrypted {
                let warning = format!("⚠ texto puro — sbox encrypt {}", bucket.name());
                line += &format!("  {}", Style::Warning.paint(&warning));
            }
            println!("{line}");
        }
        Ok(())
    }

    fn create_bucket(&self, name: &str) -> Result<()> {
        let bucket = Bucket::named(name)?;
        guard!(!bucket.exists(), else: SboxError::BucketAlreadyExists { bucket: bucket.name().into() });
        // Encripta em memória e publica o ciphertext atomicamente: o plaintext nunca vai pro disco.
        let ciphertext = self.sops.encrypt("{}\n", &bucket)?;
        FileManager::create_file_without_overwriting(&bucket.file_name, &ciphertext)?;
        Console::success_with_hint(
            &format!("bucket {} criado", bucket.name()),
            &format!(" — adicione com: sbox set {} <CHAVE>", bucket.name()),
        );
        Ok(())
    }

    fn rename_bucket(&self, name: &str, new_name: &str) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;
        let renamed = Bucket::named(new_name)?;
        FileManager::move_item_without_overwriting(&bucket.file_name, &renamed.file_name)?;
        Console::success(&format!("{} → {}", bucket.name(), renamed.name()));
        Ok(())
    }

    fn delete_bucket(&self, name: &str, skip_confirmation: bool) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;
        let key_count = bucket.summarize()?.keys.len();
        let question = format!("Apagar o bucket {} com {key_count} chave(s)?", bucket.name());
        guard!(skip_confirmation || Console::confirm(&question), else: SboxError::Cancelled);

        FileManager::remove_item(&bucket.file_name)?;
        Console::success_with_hint(
            &format!("bucket {} apagado", bucket.name()),
            " (se já estava commitado, o git ainda tem)",
        );
        Ok(())
    }

    fn encrypt_bucket(&self, name: &str) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;
        if bucket.summarize()?.is_encrypted {
            Console::note(&format!("{} já está encriptado", bucket.name()));
            return Ok(());
        }
        self.sops.encrypt_in_place(&bucket)?;
        Console::success(&format!("{} encriptado", bucket.name()));
        Ok(())
    }

    fn edit_bucket(&self, name: &str) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;
        bucket.summarize_requiring_encryption()?;
        self.sops.edit(&bucket)
    }

    // MARK: - Secrets

    fn show_secrets(&self, name: &str, reveal_values: bool) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;

        if !reveal_values {
            // Sem revelar nem precisa decriptar: os nomes das chaves estão em texto claro.
            let summary = bucket.summarize()?;
            if !summary.is_encrypted {
                Console::warning(&format!("{} está em texto puro", bucket.name()));
            }
            let rows = summary.keys.iter().map(|key| (key.as_str(), Style::Secondary.paint(MASKED_VALUE)));
            print_table(rows.collect());
            return Ok(());
        }

        bucket.summarize_requiring_encryption()?;
        let secrets = self.sops.decrypt(&bucket)?;
        // Valor multilinha numa linha só, para não quebrar a tabela (o `get` devolve o original).
        let line_break = Style::Secondary.paint("⏎");
        let rows = secrets.iter().map(|secret| (secret.key.as_str(), secret.value.replace('\n', &line_break)));
        print_table(rows.collect());
        Ok(())
    }

    fn get_secret(&self, name: &str, key: &str, destination: SecretDestination) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;
        bucket.summarize_requiring_encryption()?;
        let secret = self
            .sops
            .decrypt(&bucket)?
            .into_iter()
            .find(|secret| secret.key == key)
            .ok_or_else(|| SboxError::SecretNotFound { key: key.into(), bucket: bucket.name().into() })?;

        match destination {
            SecretDestination::Pasteboard { clearing_after } => {
                Pasteboard::general().copy(&secret.value, clearing_after)?;
                let note = clearing_after.map(|delay| format!(" (limpa em {}s)", delay.as_secs())).unwrap_or_default();
                Console::success(&format!("{key} copiado{note}"));
            }
            SecretDestination::StandardOutput if Console::is_interactive_output() => println!("{}", secret.value),
            SecretDestination::StandardOutput => {
                // Em pipe, o valor sai exato, sem \n extra.
                print!("{}", secret.value);
                let _ = std::io::stdout().flush();
            }
        }
        Ok(())
    }

    fn set_secret(&self, name: &str, key: String, value: Option<String>) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;
        guard!(!key.is_empty() && !key.contains('\n'), else: SboxError::InvalidKey);
        let summary = bucket.summarize_requiring_encryption()?;

        let value = match value {
            Some(value) => value,
            None if Console::is_standard_input_a_terminal() => ask_for_value(&key)?,
            None => read_value_from_standard_input()?,
        };

        let verb = if summary.contains_key(&key) { "atualizado" } else { "criado" };
        self.sops.set(&Secret { key: key.clone(), value }, &bucket)?;
        Console::success(&format!("{key} {verb} em {}", bucket.name()));
        Ok(())
    }

    fn remove_secret(&self, name: &str, key: &str, skip_confirmation: bool) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;
        let summary = bucket.summarize_requiring_encryption()?;
        guard!(
            summary.contains_key(key),
            else: SboxError::SecretNotFound { key: key.into(), bucket: bucket.name().into() }
        );
        let question = format!("Remover {key} de {}?", bucket.name());
        guard!(skip_confirmation || Console::confirm(&question), else: SboxError::Cancelled);

        self.sops.unset(key, &bucket)?;
        Console::success(&format!("{key} removido de {}", bucket.name()));
        Ok(())
    }

    /// Substitui o sbox pelo comando (`exec`), com os secrets como variáveis de
    /// ambiente. Só volta daqui se não conseguiu executar.
    fn run_with_secrets(&self, name: &str, command: &[String]) -> Result<()> {
        let bucket = self.vault.existing_bucket(name)?;
        bucket.summarize_requiring_encryption()?;
        let secrets = self.sops.decrypt(&bucket)?;
        if let Some(invalid) = secrets.iter().find(|secret| !is_valid_environment_name(&secret.key)) {
            return Err(SboxError::InvalidEnvironmentName { key: invalid.key.clone() });
        }

        let (program, arguments) = command.split_first().expect("o parser exige um comando");
        let reason = std::process::Command::new(program)
            .args(arguments)
            .envs(secrets.into_iter().map(|secret| (secret.key, secret.value)))
            // O comando roda onde o usuário estava, não no cofre.
            .current_dir(&self.caller_directory)
            .exec();
        Err(SboxError::LaunchFailed { program: program.clone(), reason })
    }
}

// MARK: - Auxiliares

/// Imprime `chave  valor` com as chaves alinhadas.
fn print_table(rows: Vec<(&str, String)>) {
    if rows.is_empty() {
        Console::note("(bucket vazio)");
    }
    let key_width = rows.iter().map(|(key, _)| key.chars().count()).max().unwrap_or(0);
    for (key, value) in rows {
        println!("  {}  {value}", Style::Accent.paint(&format!("{key:<key_width$}")));
    }
}

/// Pergunta o valor sem eco, duas vezes, para pegar erro de digitação.
fn ask_for_value(key: &str) -> Result<String> {
    let value = Console::read_secure_line(&format!("Valor de {key}: "))?;
    guard!(!value.is_empty(), else: SboxError::EmptyValue);
    guard!(Console::read_secure_line("Repita: ")? == value, else: SboxError::ValuesDoNotMatch);
    Ok(value)
}

/// Lê o stdin inteiro, tirando só o `\n` final que o `echo` acrescenta.
fn read_value_from_standard_input() -> Result<String> {
    let mut bytes = Vec::new();
    std::io::stdin().read_to_end(&mut bytes).map_err(|reason| SboxError::FileOperationFailed {
        action: "ler",
        path: "o stdin".into(),
        reason,
    })?;
    let mut value = String::from_utf8_lossy(&bytes).into_owned();
    if value.ends_with('\n') {
        value.pop();
    }
    Ok(value)
}

fn is_valid_environment_name(key: &str) -> bool {
    !key.is_empty() && !key.contains(['=', '\0'])
}
