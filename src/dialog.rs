// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Pergunta um valor numa janela do sistema, para quando não há terminal — por
// exemplo, um agente de IA rodando o sbox. osascript (macOS), zenity ou
// kdialog (Linux), sempre como programa externo.

use std::path::Path;

use crate::error::{Result, SboxError};
use crate::process::{Process, ProcessInfo, ProcessResult, StandardOutput};

/// Quanto tempo a janela espera antes de desistir (onde a ferramenta permite).
const TIMEOUT_SECONDS: u32 = 300;

pub enum Dialog {
    MacOs,
    Zenity,
    Kdialog,
}

impl Dialog {
    /// A ferramenta de diálogo da sessão gráfica atual.
    pub fn available() -> Result<Dialog> {
        if cfg!(target_os = "macos") {
            return Ok(Dialog::MacOs);
        }
        let has_graphical_session =
            ProcessInfo::environment("WAYLAND_DISPLAY").is_some() || ProcessInfo::environment("DISPLAY").is_some();
        guard!(has_graphical_session, else: SboxError::NoDialog);
        if is_in_path("zenity") {
            Ok(Dialog::Zenity)
        } else if is_in_path("kdialog") {
            Ok(Dialog::Kdialog)
        } else {
            Err(SboxError::NoDialog)
        }
    }

    /// Mostra `message` e devolve o que foi digitado, sem eco. O valor volta
    /// pelo stdout da ferramenta, num pipe; nunca aparece como argumento.
    pub fn ask_hidden(&self, message: &str) -> Result<String> {
        let (tool, arguments) = self.command(message);
        let result =
            Process::new(tool).arguments(arguments).standard_output(StandardOutput::Captured).run_until_exit()?;
        guard!(!self.was_cancelled(&result), else: SboxError::Cancelled);
        guard!(
            result.succeeded(),
            else: SboxError::DialogFailed {
                tool,
                status: result.termination_status,
                standard_error: result.standard_error_text(),
            }
        );
        let mut value = String::from_utf8_lossy(&result.standard_output).into_owned();
        if value.ends_with('\n') {
            value.pop();
        }
        Ok(value)
    }

    fn command(&self, message: &str) -> (&'static str, Vec<String>) {
        match self {
            // A mensagem entra como argumento do script (`argv`), nunca interpolada
            // no AppleScript: uma aspa no nome da chave não vira código.
            Dialog::MacOs => {
                let script = [
                    "on run argv",
                    "activate",
                    &format!(
                        "set answer to display dialog (item 1 of argv) default answer \"\" with hidden answer \
                         with title \"sbox\" buttons {{\"Cancelar\", \"Gravar\"}} default button \"Gravar\" \
                         cancel button \"Cancelar\" giving up after {TIMEOUT_SECONDS}"
                    ),
                    "if gave up of answer then error number -128",
                    "return text returned of answer",
                    "end run",
                ];
                let mut arguments: Vec<String> =
                    script.iter().flat_map(|line| ["-e".to_owned(), line.to_string()]).collect();
                arguments.push(message.into());
                ("osascript", arguments)
            }
            Dialog::Zenity => (
                "zenity",
                vec![
                    "--entry".into(),
                    "--hide-text".into(),
                    "--title=sbox".into(),
                    "--ok-label=Gravar".into(),
                    "--cancel-label=Cancelar".into(),
                    format!("--timeout={TIMEOUT_SECONDS}"),
                    format!("--text={message}"),
                ],
            ),
            Dialog::Kdialog => ("kdialog", vec!["--title".into(), "sbox".into(), "--password".into(), message.into()]),
        }
    }

    /// Cancelar e deixar o tempo esgotar não são falhas da ferramenta.
    fn was_cancelled(&self, result: &ProcessResult) -> bool {
        match self {
            // -128 é o "User canceled." do AppleScript (e o nosso `error number -128`).
            Dialog::MacOs => result.standard_error_text().contains("(-128)"),
            // 1 = Cancelar ou janela fechada; 5 = tempo esgotado.
            Dialog::Zenity => matches!(result.termination_status, 1 | 5),
            Dialog::Kdialog => result.termination_status == 1,
        }
    }
}

fn is_in_path(program: &str) -> bool {
    ProcessInfo::environment("PATH").is_some_and(|path| {
        path.split(':').any(|directory| !directory.is_empty() && Path::new(directory).join(program).is_file())
    })
}
