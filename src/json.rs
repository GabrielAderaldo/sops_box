// JSON mínimo — só o que trocamos com o sops: strings soltas na ida e um
// objeto plano `{"CHAVE": "valor"}` na volta. Sem serde, para manter o
// binário pequeno.

use crate::bucket::Secret;
use crate::error::{Result, SboxError};

pub struct JSONEncoder;

impl JSONEncoder {
    /// `valor` → `"valor"`, com aspas e escapes.
    pub fn encode_string(text: &str) -> String {
        let mut encoded = String::with_capacity(text.len() + 2);
        encoded.push('"');
        for character in text.chars() {
            match character {
                '"' => encoded.push_str("\\\""),
                '\\' => encoded.push_str("\\\\"),
                '\n' => encoded.push_str("\\n"),
                '\r' => encoded.push_str("\\r"),
                '\t' => encoded.push_str("\\t"),
                control if (control as u32) < 0x20 => encoded.push_str(&format!("\\u{:04x}", control as u32)),
                other => encoded.push(other),
            }
        }
        encoded.push('"');
        encoded
    }

    /// Caminho de uma chave de primeiro nível, no formato do `sops set`: `["CHAVE"]`.
    pub fn encode_key_path(key: &str) -> String {
        format!("[{}]", Self::encode_string(key))
    }
}

pub struct JSONDecoder;

impl JSONDecoder {
    /// Lê `{"CHAVE": "valor", ...}` preservando a ordem do arquivo.
    /// Números e booleanos voltam como texto; objetos e listas aninhados são erro.
    pub fn decode_secrets(bytes: &[u8]) -> Result<Vec<Secret>> {
        let mut reader = Reader { bytes, offset: 0 };
        let secrets = reader.read_object()?;
        reader.skip_whitespace();
        guard!(reader.is_at_end(), else: reader.invalid("lixo após o objeto"));
        Ok(secrets)
    }
}

/// Cursor sobre os bytes do JSON.
struct Reader<'json> {
    bytes: &'json [u8],
    offset: usize,
}

impl Reader<'_> {
    fn invalid(&self, reason: &'static str) -> SboxError {
        SboxError::InvalidJSON { reason, offset: self.offset }
    }

    fn is_at_end(&self) -> bool {
        self.offset == self.bytes.len()
    }

    fn peek(&self) -> Option<u8> {
        self.bytes.get(self.offset).copied()
    }

    fn advance(&mut self) {
        self.offset += 1;
    }

    fn skip_whitespace(&mut self) {
        while matches!(self.peek(), Some(b' ' | b'\n' | b'\r' | b'\t')) {
            self.advance();
        }
    }

    fn consume(&mut self, expected: u8, reason: &'static str) -> Result<()> {
        self.skip_whitespace();
        guard!(self.peek() == Some(expected), else: self.invalid(reason));
        self.advance();
        Ok(())
    }

    fn read_object(&mut self) -> Result<Vec<Secret>> {
        self.consume(b'{', "esperava '{'")?;
        let mut secrets = Vec::new();
        self.skip_whitespace();
        if self.peek() == Some(b'}') {
            self.advance();
            return Ok(secrets);
        }
        loop {
            let key = self.read_string()?;
            self.consume(b':', "esperava ':'")?;
            let value = self.read_scalar()?;
            secrets.push(Secret { key, value });

            self.skip_whitespace();
            match self.peek() {
                Some(b',') => self.advance(),
                Some(b'}') => {
                    self.advance();
                    return Ok(secrets);
                }
                None => return Err(self.invalid("objeto não fechado")),
                Some(_) => return Err(self.invalid("esperava ',' ou '}'")),
            }
        }
    }

    /// String, número, booleano ou null — tudo vira texto.
    fn read_scalar(&mut self) -> Result<String> {
        self.skip_whitespace();
        match self.peek() {
            None => Err(self.invalid("valor ausente")),
            Some(b'"') => self.read_string(),
            Some(b'{' | b'[') => Err(SboxError::NestedValuesUnsupported),
            Some(_) => {
                let start = self.offset;
                while !matches!(self.peek(), None | Some(b',' | b'}' | b' ' | b'\n' | b'\r' | b'\t')) {
                    self.advance();
                }
                guard!(self.offset > start, else: self.invalid("valor vazio"));
                Ok(String::from_utf8_lossy(&self.bytes[start..self.offset]).into_owned())
            }
        }
    }

    fn read_string(&mut self) -> Result<String> {
        self.consume(b'"', "esperava '\"'")?;
        let mut decoded = Vec::new();
        while let Some(byte) = self.peek() {
            self.advance();
            match byte {
                b'"' => return Ok(String::from_utf8_lossy(&decoded).into_owned()),
                b'\\' => self.read_escape(&mut decoded)?,
                _ => decoded.push(byte),
            }
        }
        Err(self.invalid("string não fechada"))
    }

    /// O que vem depois de uma `\` dentro de uma string.
    fn read_escape(&mut self, decoded: &mut Vec<u8>) -> Result<()> {
        let Some(escape) = self.peek() else { return Err(self.invalid("string não fechada")) };
        self.advance();
        match escape {
            b'"' | b'\\' | b'/' => decoded.push(escape),
            b'b' => decoded.push(0x08),
            b'f' => decoded.push(0x0c),
            b'n' => decoded.push(b'\n'),
            b'r' => decoded.push(b'\r'),
            b't' => decoded.push(b'\t'),
            b'u' => {
                let character = self.read_unicode_escape()?;
                decoded.extend_from_slice(character.encode_utf8(&mut [0; 4]).as_bytes());
            }
            _ => return Err(self.invalid("escape desconhecido")),
        }
        Ok(())
    }

    /// `\uXXXX`, incluindo pares substitutos (`😀` → 😀).
    fn read_unicode_escape(&mut self) -> Result<char> {
        let mut code_point = self.read_hex_quad()?;
        if (0xD800..=0xDBFF).contains(&code_point) {
            guard!(self.bytes.get(self.offset..self.offset + 2) == Some(b"\\u"), else: self.invalid("surrogate incompleto"));
            self.offset += 2;
            let low_surrogate = self.read_hex_quad()?;
            guard!((0xDC00..=0xDFFF).contains(&low_surrogate), else: self.invalid("surrogate inválido"));
            code_point = 0x10000 + ((code_point - 0xD800) << 10) + (low_surrogate - 0xDC00);
        }
        char::from_u32(code_point).ok_or_else(|| self.invalid("\\u inválido"))
    }

    fn read_hex_quad(&mut self) -> Result<u32> {
        let value = self
            .bytes
            .get(self.offset..self.offset + 4)
            .and_then(|digits| std::str::from_utf8(digits).ok())
            .and_then(|digits| u32::from_str_radix(digits, 16).ok())
            .ok_or_else(|| self.invalid("\\u inválido"))?;
        self.offset += 4;
        Ok(value)
    }
}
