// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O fluxo de uma execução (o `run` do src/main.rs e o despacho do src/sbox.rs): argv →
// comando tipado → caso de uso → presenter → código de saída. --help e --version saem antes
// de tocar no cofre.
//
// Os comandos chegam um a um, cada um com o arquivo da suíte (tests/*.t) que ele deixa verde
// (ver windows/tests.txt). Os que ainda não vieram saem com "ainda não implementado no Windows".

using System;
using System.Collections.Generic;
using Sbox.Application;
using Sbox.Application.Ports;
using Sbox.Domain;

namespace Sbox.Presentation;

internal sealed class Cli(IEnvironment environment, IFileSystem fileSystem, ISops sops, ITerminal terminal)
{
    private readonly Presenter presenter = new(terminal);

    public int Run(IEnumerable<string> arguments)
    {
        try
        {
            Invocation invocation = Invocation.Parse(arguments);
            switch (invocation.Command)
            {
                case Command.ShowHelp:
                    presenter.Help();
                    break;
                case Command.ShowVersion:
                    presenter.Version();
                    break;
                default:
                    Vault vault = new OpenVault(environment, fileSystem).Execute(invocation.VaultDirectory);
                    Dispatch(vault, invocation.Command);
                    break;
            }
            return Presenter.SuccessStatus;
        }
        catch (SboxError error)
        {
            return presenter.Error(error);
        }
    }

    private void Dispatch(Vault vault, Command command)
    {
        BucketStore store = new(fileSystem);
        switch (command)
        {
            case Command.ListBuckets list:
                presenter.BucketList(vault, new ListBuckets(store).Execute(list.IncludingHidden));
                break;
            case Command.CreateBucket create:
                presenter.BucketCreated(new CreateBucket(store, sops, fileSystem).Execute(create.Bucket));
                break;
            case Command.RenameBucket rename:
                presenter.BucketMoved(new MoveBucket(store, fileSystem).Execute(rename.Bucket, rename.NewName));
                break;
            case Command.DeleteBucket delete:
                presenter.BucketDeleted(
                    new DeleteBucket(store, fileSystem, new TerminalConfirmation(terminal))
                        .Execute(delete.Bucket, delete.SkipConfirmation));
                break;
            case Command.EncryptBucket encrypt:
                presenter.BucketEncrypted(new EncryptBucket(store, sops).Execute(encrypt.Bucket));
                break;
            case Command.ShowSecrets { RevealValues: false } show:
                presenter.BucketKeys(new ShowBucket(store).Execute(show.Bucket));
                break;
            case Command.ShowSecrets reveal:
                presenter.BucketSecrets(new RevealBucket(store, sops).Execute(reveal.Bucket));
                break;
            case Command.GetSecret { Destination: SecretDestination.StandardOutput } get:
                presenter.SecretValueOutput(new GetSecret(store, sops).Execute(get.Bucket, get.Key));
                break;
            case Command.SetSecret { Source: not ValueSource.Dialog } set:
                ISecretInput input = set.Source is ValueSource.Argument argument
                    ? new ArgumentSecretInput(argument.Value)
                    : new TerminalSecretInput(terminal);
                presenter.SecretWritten(new SetSecret(store, sops).Execute(set.Bucket, set.Key, input));
                break;
            case Command.RemoveSecret remove:
                presenter.SecretRemoved(
                    new RemoveSecret(store, sops, new TerminalConfirmation(terminal))
                        .Execute(remove.Bucket, remove.Key, remove.SkipConfirmation));
                break;

            case Command.GetSecret:
                throw SboxError.NotYetImplemented("get -c");
            case Command.SetSecret:
                throw SboxError.NotYetImplemented("set --dialog");
            case Command.EditBucket:
                throw SboxError.NotYetImplemented("edit");
            case Command.RunWithSecrets:
                throw SboxError.NotYetImplemented("run");
            default:
                throw new InvalidOperationException($"{command} é tratado no Run, antes de abrir o cofre");
        }
    }
}
