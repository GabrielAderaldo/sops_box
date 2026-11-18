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
            case Command.ShowSecrets { RevealValues: false } show:
                presenter.BucketKeys(new ShowBucket(store).Execute(show.Bucket));
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
                throw new InvalidOperationException($"{command} é tratado no Run, antes de abrir o cofre");
        }
    }
}
