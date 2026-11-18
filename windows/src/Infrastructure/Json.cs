// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// JSON mínimo, espelho do src/json.rs: só o que trocamos com o sops. Strings soltas na ida
// (o valor do `sops set --value-stdin` e o caminho `["CHAVE"]`) e um objeto plano
// `{"CHAVE": "valor"}` na volta (`sops decrypt --output-type json`). Escrito à mão: nada de
// System.Text.Json com reflection num binário NativeAOT que manipula segredos.

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sbox.Domain;

namespace Sbox.Infrastructure;

internal static class JsonEncoder
{
    /// `valor` → `"valor"`, com aspas e escapes. Controles viram `\u00XX`; o resto vai cru
    /// (o texto sai em UTF-8).
    public static string EncodeString(string text)
    {
        StringBuilder encoded = new(text.Length + 2);
        encoded.Append('"');
        foreach (char character in text)
        {
            switch (character)
            {
                case '"': encoded.Append("\\\""); break;
                case '\\': encoded.Append("\\\\"); break;
                case '\n': encoded.Append("\\n"); break;
                case '\r': encoded.Append("\\r"); break;
                case '\t': encoded.Append("\\t"); break;
                case < ' ':
                    encoded.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}");
                    break;
                default: encoded.Append(character); break;
            }
        }
        encoded.Append('"');
        return encoded.ToString();
    }

    /// Caminho de uma chave de primeiro nível, no formato do `sops set`/`unset`: `["CHAVE"]`.
    public static string EncodeKeyPath(string key) => $"[{EncodeString(key)}]";
}

internal static class JsonDecoder
{
    /// Lê `{"CHAVE": "valor", ...}` preservando a ordem do arquivo. Números e booleanos
    /// voltam como texto; objetos e listas aninhados são erro.
    public static List<Secret> DecodeSecrets(byte[] bytes)
    {
        Reader reader = new(bytes);
        List<Secret> secrets = reader.ReadObject();
        reader.SkipWhitespace();
        return reader.IsAtEnd ? secrets : throw reader.Invalid("lixo após o objeto");
    }

    /// Cursor sobre os bytes do JSON.
    private sealed class Reader(byte[] bytes)
    {
        private int offset;

        public bool IsAtEnd => offset == bytes.Length;

        private int? Peek => offset < bytes.Length ? bytes[offset] : null;

        public SboxError Invalid(string reason) => SboxError.InvalidJson(reason, offset);

        public void SkipWhitespace()
        {
            while (Peek is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
            {
                offset++;
            }
        }

        public List<Secret> ReadObject()
        {
            Consume((byte)'{', "esperava '{'");
            List<Secret> secrets = new();
            SkipWhitespace();
            if (Peek == '}')
            {
                offset++;
                return secrets;
            }
            while (true)
            {
                string key = ReadString();
                Consume((byte)':', "esperava ':'");
                string value = ReadScalar();
                secrets.Add(new Secret(key, SecretValue.FromDecrypted(value)));

                SkipWhitespace();
                switch (Peek)
                {
                    case (byte)',':
                        offset++;
                        break;
                    case (byte)'}':
                        offset++;
                        return secrets;
                    case null:
                        throw Invalid("objeto não fechado");
                    default:
                        throw Invalid("esperava ',' ou '}'");
                }
            }
        }

        private void Consume(byte expected, string reason)
        {
            SkipWhitespace();
            if (Peek != expected)
            {
                throw Invalid(reason);
            }
            offset++;
        }

        /// String, número, booleano ou null — tudo vira texto.
        private string ReadScalar()
        {
            SkipWhitespace();
            switch (Peek)
            {
                case null:
                    throw Invalid("valor ausente");
                case (byte)'"':
                    return ReadString();
                case (byte)'{' or (byte)'[':
                    throw SboxError.NestedValuesUnsupported();
                default:
                    int start = offset;
                    while (Peek is not (null or (byte)',' or (byte)'}' or (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t'))
                    {
                        offset++;
                    }
                    if (offset == start)
                    {
                        throw Invalid("valor vazio");
                    }
                    return Encoding.UTF8.GetString(bytes, start, offset - start);
            }
        }

        private string ReadString()
        {
            Consume((byte)'"', "esperava '\"'");
            List<byte> decoded = new();
            while (Peek is int next)
            {
                byte current = (byte)next;
                offset++;
                switch (current)
                {
                    case (byte)'"':
                        return Encoding.UTF8.GetString(decoded.ToArray());
                    case (byte)'\\':
                        ReadEscape(decoded);
                        break;
                    default:
                        decoded.Add(current);
                        break;
                }
            }
            throw Invalid("string não fechada");
        }

        /// O que vem depois de uma `\` dentro de uma string.
        private void ReadEscape(List<byte> decoded)
        {
            if (Peek is not int escape)
            {
                throw Invalid("string não fechada");
            }
            offset++;
            switch (escape)
            {
                case '"' or '\\' or '/': decoded.Add((byte)escape); break;
                case 'b': decoded.Add(0x08); break;
                case 'f': decoded.Add(0x0c); break;
                case 'n': decoded.Add((byte)'\n'); break;
                case 'r': decoded.Add((byte)'\r'); break;
                case 't': decoded.Add((byte)'\t'); break;
                case 'u':
                    decoded.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(ReadUnicodeEscape())));
                    break;
                default:
                    throw Invalid("escape desconhecido");
            }
        }

        /// `\uXXXX`, incluindo pares substitutos (`😀` → 😀).
        private int ReadUnicodeEscape()
        {
            int codePoint = ReadHexQuad();
            if (codePoint is >= 0xD800 and <= 0xDBFF)
            {
                if (offset + 2 > bytes.Length || bytes[offset] != '\\' || bytes[offset + 1] != 'u')
                {
                    throw Invalid("surrogate incompleto");
                }
                offset += 2;
                int lowSurrogate = ReadHexQuad();
                if (lowSurrogate is < 0xDC00 or > 0xDFFF)
                {
                    throw Invalid("surrogate inválido");
                }
                codePoint = 0x10000 + ((codePoint - 0xD800) << 10) + (lowSurrogate - 0xDC00);
            }
            // Substituto baixo solto não é caractere (o char::from_u32 do Rust também recusa).
            return codePoint is >= 0xD800 and <= 0xDFFF ? throw Invalid("\\u inválido") : codePoint;
        }

        private int ReadHexQuad()
        {
            if (offset + 4 > bytes.Length
                || !int.TryParse(
                    Encoding.ASCII.GetString(bytes, offset, 4), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out int value))
            {
                throw Invalid("\\u inválido");
            }
            offset += 4;
            return value;
        }
    }
}
