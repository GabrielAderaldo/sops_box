// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O que cada comando faz (espelho de src/sbox.rs; em C# a classe não pode se chamar `Sbox`,
// que é o namespace). Junta o cofre, o sops e o diretório de onde o usuário chamou — o
// contexto que todos os comandos compartilham.
//
// Os comandos chegam um a um, cada um com o arquivo da suíte (tests/*.t) que ele deixa verde
// (ver windows/tests.txt). Os que ainda não vieram saem com "ainda não implementado no Windows".

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sbox;

internal sealed class SboxCommands
{
    private const string MaskedValue = "••••••••";

    private readonly Vault vault;
    private readonly Sops sops;

    private SboxCommands(Vault vault, Sops sops)
    {
        this.vault = vault;
        this.sops = sops;
    }

    /// Entra no cofre: dali o sops acha o `.sops.yaml` e os caminhos ficam relativos.
    public static SboxCommands Open(Vault vault)
    {
        FileManager.ChangeCurrentDirectoryPath(vault.Directory);
        return new SboxCommands(vault, Sops.FromEnvironment());
    }

    public void Run(Command command)
    {
        switch (command)
        {
            case Command.ListBuckets list:
                ListBuckets(list.IncludingHidden);
                break;
            case Command.CreateBucket create:
                CreateBucket(create.Bucket);
                break;
            case Command.RenameBucket rename:
                RenameBucket(rename.Bucket, rename.NewName);
                break;
            case Command.DeleteBucket delete:
                DeleteBucket(delete.Bucket, delete.SkipConfirmation);
                break;
            case Command.ShowSecrets { RevealValues: false } show:
                ShowSecretNames(show.Bucket);
                break;

            case Command.ShowSecrets:
                throw SboxError.NotYetImplemented("show -r");
            case Command.EncryptBucket:
                throw SboxError.NotYetImplemented("encrypt");
            case Command.EditBucket:
                throw SboxError.NotYetImplemented("edit");
            case Command.GetSecret:
                throw SboxError.NotYetImplemented("get");
            case Command.SetSecret:
                throw SboxError.NotYetImplemented("set");
            case Command.RemoveSecret:
                throw SboxError.NotYetImplemented("rm");
            case Command.RunWithSecrets:
                throw SboxError.NotYetImplemented("run");
            default:
                throw new InvalidOperationException($"{command} é tratado no Program, antes de abrir o cofre");
        }
    }

    // MARK: - Buckets

    private void ListBuckets(bool includingHidden)
    {
        List<Bucket> buckets = Vault.Buckets(includingHidden);
        if (buckets.Count == 0)
        {
            Terminal.Note($"nenhum bucket em {vault.Directory} — crie um com: sbox new <nome>");
            return;
        }
        int nameWidth = buckets.Max(bucket => CharacterCount(bucket.Name));
        foreach (Bucket bucket in buckets)
        {
            BucketSummary summary = bucket.Summarize();
            string keyCount = summary.Keys.Count == 1
                ? "1 chave"
                : string.Create(CultureInfo.InvariantCulture, $"{summary.Keys.Count} chaves");
            string line = $"  {Terminal.Paint(Style.Accent, PadRight(bucket.Name, nameWidth))}  "
                + Terminal.Paint(Style.Secondary, keyCount);
            if (!summary.IsEncrypted)
            {
                line += "  " + Terminal.Paint(Style.Warning, $"⚠ texto puro — sbox encrypt {bucket.Name}");
            }
            Terminal.PrintLine(line);
        }
    }

    private void CreateBucket(string name)
    {
        Bucket bucket = Bucket.Named(name);
        if (bucket.Exists)
        {
            throw SboxError.BucketAlreadyExists(bucket.Name);
        }
        // Encripta em memória e publica o ciphertext sem sobrescrever: o plaintext nunca vai pro disco.
        byte[] ciphertext = sops.Encrypt("{}\n", bucket);
        FileManager.CreateFileWithoutOverwriting(bucket.FileName, ciphertext);
        Terminal.SuccessWithHint($"bucket {bucket.Name} criado", $" — adicione com: sbox set {bucket.Name} <CHAVE>");
    }

    private static void RenameBucket(string name, string newName)
    {
        Bucket bucket = Vault.ExistingBucket(name);
        Bucket renamed = Bucket.Named(newName);
        FileManager.MoveItemWithoutOverwriting(bucket.FileName, renamed.FileName);
        Terminal.Success($"{bucket.Name} → {renamed.Name}");
    }

    private static void DeleteBucket(string name, bool skipConfirmation)
    {
        Bucket bucket = Vault.ExistingBucket(name);
        int keyCount = bucket.Summarize().Keys.Count;
        string question = string.Create(
            CultureInfo.InvariantCulture, $"Apagar o bucket {bucket.Name} com {keyCount} chave(s)?");
        if (!skipConfirmation && !Terminal.Confirm(question))
        {
            throw SboxError.Cancelled();
        }

        FileManager.RemoveItem(bucket.FileName);
        Terminal.SuccessWithHint($"bucket {bucket.Name} apagado", " (se já estava commitado, o git ainda tem)");
    }

    // MARK: - Secrets

    /// `show` sem `-r`: nem precisa decriptar, os nomes das chaves estão em texto claro.
    private static void ShowSecretNames(string name)
    {
        Bucket bucket = Vault.ExistingBucket(name);
        BucketSummary summary = bucket.Summarize();
        if (!summary.IsEncrypted)
        {
            Terminal.Warning($"{bucket.Name} está em texto puro");
        }
        PrintTable(summary.Keys.Select(key => (key, Terminal.Paint(Style.Secondary, MaskedValue))).ToList());
    }

    // MARK: - Auxiliares

    /// Imprime `chave  valor` com as chaves alinhadas.
    private static void PrintTable(List<(string Key, string Value)> rows)
    {
        if (rows.Count == 0)
        {
            Terminal.Note("(bucket vazio)");
            return;
        }
        int keyWidth = rows.Max(row => CharacterCount(row.Key));
        foreach ((string key, string value) in rows)
        {
            Terminal.PrintLine($"  {Terminal.Paint(Style.Accent, PadRight(key, keyWidth))}  {value}");
        }
    }

    /// Caracteres como o `chars().count()` do Rust (code points, não unidades UTF-16): um
    /// emoji conta 1, e o alinhamento fica igual ao da versão Rust.
    private static int CharacterCount(string text) => text.EnumerateRunes().Count();

    private static string PadRight(string text, int width) =>
        text + new string(' ', Math.Max(0, width - CharacterCount(text)));
}
