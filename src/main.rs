// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

//! sbox — gerencia um cofre sops (`~/secrets`) sem digitar `sops` na mão.
//!
//! Só dois conceitos: **bucket** = arquivo `*.enc.yaml`; **secret** = uma
//! linha `CHAVE: valor` dentro dele.

#[macro_use]
mod error;

mod bucket;
mod command_line;
mod console;
mod file_manager;
mod json;
mod pasteboard;
mod process;
mod sbox;
mod sops;
mod vault;

use crate::command_line::{Command, Invocation, USAGE, VERSION};
use crate::console::Console;
use crate::error::Result;
use crate::sbox::Sbox;
use crate::vault::Vault;

fn main() {
    if let Err(error) = run() {
        Console::error(&error);
        std::process::exit(1);
    }
}

fn run() -> Result<()> {
    let invocation = Invocation::parse(std::env::args().skip(1))?;
    match invocation.command {
        Command::ShowHelp => println!("{USAGE}"),
        Command::ShowVersion => println!("sbox {VERSION}"),
        command => {
            let vault = Vault::locate(invocation.vault_directory.as_deref())?;
            Sbox::open(vault)?.run(command)?;
        }
    }
    Ok(())
}
