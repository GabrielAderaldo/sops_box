// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O binário `sops`. Todo comando roda com o diretório atual = cofre, porque o
// sops procura o `.sops.yaml` subindo a partir do arquivo. Todo código de saída
// é conferido: falha silenciosa aqui significa secret perdido.

use crate::bucket::{Bucket, Secret};
use crate::error::{Result, SboxError};
use crate::json::{JSONDecoder, JSONEncoder};
use crate::process::{Process, ProcessInfo, StandardInput, StandardOutput};

/// `sops edit` sai com 200 quando o arquivo não foi alterado — não é erro.
const EDIT_UNCHANGED_STATUS: i32 = 200;

pub struct Sops {
    executable: String,
}

impl Sops {
    /// `$SOPS_BIN`, ou `sops` do PATH.
    pub fn from_environment() -> Sops {
        Sops { executable: ProcessInfo::environment("SOPS_BIN").unwrap_or_else(|| "sops".into()) }
    }

    pub fn decrypt(&self, bucket: &Bucket) -> Result<Vec<Secret>> {
        let json = self.run(["decrypt", "--output-type", "json", &bucket.file_name], StandardInput::Nothing)?;
        JSONDecoder::decode_secrets(&json)
    }

    /// Cria ou atualiza in-place. O valor vai pelo stdin (`--value-stdin`).
    pub fn set(&self, secret: &Secret, bucket: &Bucket) -> Result<()> {
        let key_path = JSONEncoder::encode_key_path(&secret.key);
        let value = JSONEncoder::encode_string(&secret.value).into_bytes();
        self.run(["set", "--value-stdin", &bucket.file_name, &key_path], StandardInput::Data(value))?;
        Ok(())
    }

    pub fn unset(&self, key: &str, bucket: &Bucket) -> Result<()> {
        let key_path = JSONEncoder::encode_key_path(key);
        self.run(["unset", &bucket.file_name, &key_path], StandardInput::Nothing)?;
        Ok(())
    }

    /// Encripta YAML vindo da memória e devolve o ciphertext — o plaintext nunca
    /// toca o disco. `--filename-override` faz o nome casar com o `path_regex`
    /// do `.sops.yaml`.
    pub fn encrypt(&self, yaml: &str, bucket: &Bucket) -> Result<Vec<u8>> {
        let ciphertext = self.run(
            [
                "encrypt",
                "--input-type",
                "yaml",
                "--output-type",
                "yaml",
                "--filename-override",
                &bucket.file_name,
                "/dev/stdin",
            ],
            StandardInput::Data(yaml.as_bytes().to_vec()),
        )?;
        // Código 0 não basta: sem o bloco `sops:` a saída não é um bucket encriptado.
        let text = String::from_utf8_lossy(&ciphertext);
        guard!(text.starts_with("sops:") || text.contains("\nsops:"), else: SboxError::SopsReturnedPlaintext);
        Ok(ciphertext)
    }

    pub fn encrypt_in_place(&self, bucket: &Bucket) -> Result<()> {
        self.run(["encrypt", "-i", &bucket.file_name], StandardInput::Nothing)?;
        Ok(())
    }

    /// Abre o bucket no `$EDITOR`, com o terminal do usuário.
    pub fn edit(&self, bucket: &Bucket) -> Result<()> {
        let result = Process::new(&self.executable)
            .arguments(["edit", &bucket.file_name])
            .standard_input(StandardInput::Inherited)
            .standard_output(StandardOutput::Inherited)
            .run_until_exit()?;
        guard!(
            result.succeeded() || result.termination_status == EDIT_UNCHANGED_STATUS,
            else: SboxError::SopsFailed {
                subcommand: "edit".into(),
                status: result.termination_status,
                standard_error: String::new(),
            }
        );
        Ok(())
    }

    /// Roda `sops <arguments>` e devolve o stdout; código ≠ 0 vira erro com o stderr do sops.
    fn run<const COUNT: usize>(&self, arguments: [&str; COUNT], input: StandardInput) -> Result<Vec<u8>> {
        let result = Process::new(&self.executable)
            .arguments(arguments)
            .standard_input(input)
            .standard_output(StandardOutput::Captured)
            .run_until_exit()?;
        guard!(
            result.succeeded(),
            else: SboxError::SopsFailed {
                subcommand: arguments[0].into(),
                status: result.termination_status,
                standard_error: result.standard_error_text(),
            }
        );
        Ok(result.standard_output)
    }
}
