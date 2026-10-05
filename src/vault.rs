// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O cofre: um diretório com `.sops.yaml` e buckets `*.enc.yaml`.

use crate::bucket::Bucket;
use crate::error::{Result, SboxError};
use crate::file_manager::FileManager;
use crate::process::ProcessInfo;

pub struct Vault {
    pub directory: String,
}

impl Vault {
    /// `--dir`, depois `$SOPS_BOX_DIR`, depois `~/secrets`.
    pub fn locate(explicit_directory: Option<&str>) -> Result<Vault> {
        let directory = explicit_directory
            .map(String::from)
            .or_else(|| ProcessInfo::environment("SOPS_BOX_DIR"))
            .or_else(|| ProcessInfo::environment("HOME").map(|home| home + "/secrets"))
            .ok_or(SboxError::VaultLocationUnknown)?;
        guard!(
            FileManager::file_exists(&format!("{directory}/.sops.yaml")),
            else: SboxError::NotAVault { directory }
        );
        Ok(Vault { directory })
    }

    /// Os buckets em ordem alfabética. Ocultos (`.algo.enc.yaml`) só se pedidos.
    pub fn buckets(&self, including_hidden: bool) -> Result<Vec<Bucket>> {
        Ok(FileManager::contents_of_directory(".")?
            .into_iter()
            .filter(|file_name| Bucket::is_bucket_file_name(file_name))
            .map(|file_name| Bucket { file_name })
            .filter(|bucket| including_hidden || !bucket.is_hidden())
            .collect())
    }

    /// O bucket com esse nome, que precisa já existir.
    pub fn existing_bucket(&self, named: &str) -> Result<Bucket> {
        let bucket = Bucket::named(named)?;
        guard!(bucket.exists(), else: SboxError::BucketNotFound { bucket: bucket.name().into() });
        Ok(bucket)
    }
}
