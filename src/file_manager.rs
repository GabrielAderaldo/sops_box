// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Arquivos e diretórios, com os nomes do `FileManager` da Foundation.

use std::fs::{self, OpenOptions};
use std::io::{ErrorKind, Write};
use std::os::unix::fs::OpenOptionsExt;

use crate::error::{Result, SboxError};

pub struct FileManager;

impl FileManager {
    pub fn file_exists(at_path: &str) -> bool {
        fs::symlink_metadata(at_path).is_ok()
    }

    pub fn contents(at_path: &str) -> Result<Vec<u8>> {
        fs::read(at_path).map_err(|reason| failure("abrir", at_path, reason))
    }

    /// Nomes das entradas do diretório, em ordem alfabética.
    pub fn contents_of_directory(at_path: &str) -> Result<Vec<String>> {
        let entries = fs::read_dir(at_path).map_err(|reason| failure("ler o diretório", at_path, reason))?;
        let mut names: Vec<String> = entries
            .filter_map(|entry| entry.ok())
            .map(|entry| entry.file_name().to_string_lossy().into_owned())
            .collect();
        names.sort();
        Ok(names)
    }

    /// Cria `at_path` com `contents` sem nunca sobrescrever nada: grava num
    /// temporário, faz fsync e só então publica com `link()`, que falha se o
    /// destino já existe. Se qualquer passo falhar, `at_path` não é criado —
    /// nada de arquivo vazio ou pela metade.
    pub fn create_file_without_overwriting(at_path: &str, contents: &[u8]) -> Result<()> {
        let temporary_path = format!(".sbox-tmp-{}", std::process::id());
        let outcome = write_synchronously(&temporary_path, contents)
            .and_then(|()| Self::link_without_overwriting(&temporary_path, at_path));
        let _ = fs::remove_file(&temporary_path);
        outcome
    }

    /// Renomeia sem sobrescrever o destino (link + unlink; mesmo sistema de arquivos).
    pub fn move_item_without_overwriting(at_path: &str, to_path: &str) -> Result<()> {
        Self::link_without_overwriting(at_path, to_path)?;
        Self::remove_item(at_path)
    }

    pub fn remove_item(at_path: &str) -> Result<()> {
        fs::remove_file(at_path).map_err(|reason| failure("apagar", at_path, reason))
    }

    pub fn current_directory_path() -> String {
        std::env::current_dir().map_or_else(|_| ".".into(), |path| path.to_string_lossy().into_owned())
    }

    pub fn change_current_directory_path(to_path: &str) -> Result<()> {
        std::env::set_current_dir(to_path).map_err(|reason| failure("entrar em", to_path, reason))
    }

    fn link_without_overwriting(at_path: &str, to_path: &str) -> Result<()> {
        fs::hard_link(at_path, to_path).map_err(|reason| match reason.kind() {
            ErrorKind::AlreadyExists => SboxError::FileAlreadyExists { path: to_path.into() },
            _ => failure("criar", to_path, reason),
        })
    }
}

fn write_synchronously(path: &str, contents: &[u8]) -> Result<()> {
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .mode(0o600)
        .open(path)
        .map_err(|reason| failure("criar", path, reason))?;
    file.write_all(contents).map_err(|reason| failure("gravar", path, reason))?;
    file.sync_all().map_err(|reason| failure("sincronizar (fsync)", path, reason))
}

fn failure(action: &'static str, path: &str, reason: std::io::Error) -> SboxError {
    SboxError::FileOperationFailed { action, path: path.into(), reason }
}
