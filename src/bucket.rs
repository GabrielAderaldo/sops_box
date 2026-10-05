// Bucket = um arquivo `*.enc.yaml` do cofre. Secret = uma linha `CHAVE: valor`
// dentro dele.

use crate::error::{Result, SboxError};
use crate::file_manager::FileManager;

pub const BUCKET_EXTENSIONS: [&str; 2] = [".enc.yaml", ".enc.yml"];

pub struct Secret {
    pub key: String,
    pub value: String,
}

/// Sempre relativo ao diretório do cofre (o sbox roda de dentro dele).
#[derive(Clone)]
pub struct Bucket {
    pub file_name: String,
}

impl Bucket {
    /// `gitlab` → `gitlab.enc.yaml`; `gitlab.enc.yaml` fica como está.
    /// Não confere se o arquivo existe — para isso, `Vault::existing_bucket`.
    pub fn named(input: &str) -> Result<Bucket> {
        guard!(
            !input.is_empty() && !input.contains('/') && !input.starts_with('-'),
            else: SboxError::InvalidBucketName(input.into())
        );
        if BUCKET_EXTENSIONS.iter().any(|extension| input.ends_with(extension)) {
            return Ok(Bucket { file_name: input.into() });
        }
        let looks_like_another_format = [".yaml", ".yml", ".json", ".env"].iter().any(|extension| input.ends_with(extension));
        guard!(!looks_like_another_format, else: SboxError::MissingEncSuffix(input.into()));
        Ok(Bucket { file_name: format!("{input}.enc.yaml") })
    }

    pub fn is_bucket_file_name(file_name: &str) -> bool {
        BUCKET_EXTENSIONS.iter().any(|extension| file_name.ends_with(extension))
    }

    /// O nome sem a extensão, como o usuário digita: `gitlab`.
    pub fn name(&self) -> &str {
        BUCKET_EXTENSIONS
            .iter()
            .find_map(|extension| self.file_name.strip_suffix(extension))
            .unwrap_or(&self.file_name)
    }

    pub fn is_hidden(&self) -> bool {
        self.file_name.starts_with('.')
    }

    pub fn exists(&self) -> bool {
        FileManager::file_exists(&self.file_name)
    }

    /// Lê os nomes das chaves direto do YAML, sem decriptar: o sops só encripta
    /// os valores, os nomes ficam em texto claro. Barato e não precisa da chave age.
    pub fn summarize(&self) -> Result<BucketSummary> {
        let contents = FileManager::contents(&self.file_name)?;
        let mut summary = BucketSummary { keys: Vec::new(), is_encrypted: false };

        for line in String::from_utf8_lossy(&contents).lines() {
            let Some(first_character) = line.chars().next() else { continue };
            // Indentado, comentário ou item de lista: não é chave de primeiro nível.
            if matches!(first_character, ' ' | '\t' | '#' | '-') {
                continue;
            }
            if line.starts_with("sops:") {
                summary.is_encrypted = true;
                continue;
            }
            let key = match first_character {
                quote @ ('"' | '\'') => quoted_key(line, quote),
                _ => plain_key(line),
            };
            summary.keys.extend(key.map(String::from));
        }
        Ok(summary)
    }

    /// Como `summarize`, mas recusa bucket em texto puro.
    pub fn summarize_requiring_encryption(&self) -> Result<BucketSummary> {
        let summary = self.summarize()?;
        guard!(summary.is_encrypted, else: SboxError::BucketIsPlaintext { bucket: self.name().into() });
        Ok(summary)
    }
}

pub struct BucketSummary {
    pub keys: Vec<String>,
    /// Tem o bloco `sops:` no final.
    pub is_encrypted: bool,
}

impl BucketSummary {
    pub fn contains_key(&self, key: &str) -> bool {
        self.keys.iter().any(|existing| existing == key)
    }
}

/// `"CHAVE": valor` ou `'CHAVE': valor`.
fn quoted_key(line: &str, quote: char) -> Option<&str> {
    let after_quote = &line[1..];
    after_quote.find(quote).map(|closing| &after_quote[..closing])
}

/// `CHAVE: valor`.
fn plain_key(line: &str) -> Option<&str> {
    line.find(':').map(|colon| &line[..colon])
}
