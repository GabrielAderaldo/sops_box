// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// A "View" da CLI: transforma o resultado de um caso de uso em texto (os `println!`/`Console`
// do src/sbox.rs e do src/console.rs). Única parte do programa que formata texto: dados no
// stdout, mensagens no stderr, cores suaves só quando o canal é um console.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sbox.Application;
using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Presentation;

/// Tons do Catppuccin Mocha / Tokyo Night: contraste baixo, sem branco nem preto puros.
internal enum Style
{
    Secondary,
    Accent,
    Success,
    Warning,
    Failure,
}

internal sealed class Presenter(ITerminal terminal)
{
    private const string MaskedValue = "••••••••";

    public const int SuccessStatus = 0;
    public const int FailureStatus = 1;

    // MARK: - Resultados

    public void Help() => PrintLine(CommandLine.Usage);

    public void Version() => PrintLine($"sbox {BuildInfo.Version}");

    public void BucketList(Vault vault, IReadOnlyList<Bucket> buckets)
    {
        if (buckets.Count == 0)
        {
            Note($"nenhum bucket em {vault.Directory} — crie um com: sbox new <nome>");
            return;
        }
        int nameWidth = buckets.Max(bucket => CharacterCount(bucket.Name.Name));
        foreach (Bucket bucket in buckets)
        {
            int count = bucket.Summary.Keys.Count;
            string keyCount = count == 1 ? "1 chave" : string.Create(CultureInfo.InvariantCulture, $"{count} chaves");
            string line = $"  {Paint(Style.Accent, PadRight(bucket.Name.Name, nameWidth))}  "
                + Paint(Style.Secondary, keyCount);
            if (!bucket.Summary.IsEncrypted)
            {
                line += "  " + Paint(Style.Warning, $"⚠ texto puro — sbox encrypt {bucket.Name.Name}");
            }
            PrintLine(line);
        }
    }

    /// `show` sem `-r`: as chaves com o valor mascarado.
    public void BucketKeys(Bucket bucket)
    {
        if (!bucket.Summary.IsEncrypted)
        {
            Warning($"{bucket.Name.Name} está em texto puro");
        }
        PrintTable(bucket.Summary.Keys.Select(key => (key, Paint(Style.Secondary, MaskedValue))).ToList());
    }

    public void BucketCreated(BucketName bucket) =>
        SuccessWithHint($"bucket {bucket.Name} criado", $" — adicione com: sbox set {bucket.Name} <CHAVE>");

    public void BucketMoved(MovedBucket moved) => Success($"{moved.From.Name} → {moved.To.Name}");

    public void BucketDeleted(BucketName bucket) =>
        SuccessWithHint($"bucket {bucket.Name} apagado", " (se já estava commitado, o git ainda tem)");

    /// `erro: <mensagem>` no stderr; devolve o código de saída.
    public int Error(SboxError error)
    {
        terminal.Write(TerminalStream.StandardError, PaintError(Style.Failure, "erro: ") + error.Message + "\n");
        return FailureStatus;
    }

    // MARK: - Perguntas

    public static string Question(ConfirmationRequest request) => request switch
    {
        ConfirmationRequest.DeleteBucket delete => string.Create(
            CultureInfo.InvariantCulture,
            $"Apagar o bucket {delete.Bucket.Name} com {delete.KeyCount} chave(s)? [s/N] "),
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    // MARK: - Blocos

    /// Imprime `chave  valor` com as chaves alinhadas.
    private void PrintTable(List<(string Key, string Value)> rows)
    {
        if (rows.Count == 0)
        {
            Note("(bucket vazio)");
            return;
        }
        int keyWidth = rows.Max(row => CharacterCount(row.Key));
        foreach ((string key, string value) in rows)
        {
            PrintLine($"  {Paint(Style.Accent, PadRight(key, keyWidth))}  {value}");
        }
    }

    private void PrintLine(string line) => terminal.Write(TerminalStream.StandardOutput, line + "\n");

    /// `✓ mensagem` em verde.
    private void Success(string message) =>
        terminal.Write(TerminalStream.StandardError, PaintError(Style.Success, $"✓ {message}") + "\n");

    /// `✓ mensagem` em verde, seguida de uma dica discreta.
    private void SuccessWithHint(string message, string hint) =>
        terminal.Write(
            TerminalStream.StandardError,
            PaintError(Style.Success, $"✓ {message}") + PaintError(Style.Secondary, hint) + "\n");

    private void Warning(string message) =>
        terminal.Write(TerminalStream.StandardError, PaintError(Style.Warning, $"⚠ {message}") + "\n");

    private void Note(string message) => terminal.Write(TerminalStream.StandardError, message + "\n");

    // MARK: - Cores e alinhamento

    private string Paint(Style style, string text) => PaintFor(TerminalStream.StandardOutput, style, text);

    private string PaintError(Style style, string text) => PaintFor(TerminalStream.StandardError, style, text);

    private string PaintFor(TerminalStream stream, Style style, string text) =>
        terminal.SupportsColor(stream) ? $"\x1b[38;2;{Rgb(style)}m{text}\x1b[0m" : text;

    private static string Rgb(Style style) => style switch
    {
        Style.Secondary => "108;112;134",
        Style.Accent => "137;180;250",
        Style.Success => "166;227;161",
        Style.Warning => "249;226;175",
        Style.Failure => "243;139;168",
        _ => "205;214;244",
    };

    /// Caracteres como o `chars().count()` do Rust (code points, não unidades UTF-16): um
    /// emoji conta 1, e o alinhamento fica igual ao da versão Rust.
    private static int CharacterCount(string text) => text.EnumerateRunes().Count();

    private static string PadRight(string text, int width) =>
        text + new string(' ', Math.Max(0, width - CharacterCount(text)));
}
