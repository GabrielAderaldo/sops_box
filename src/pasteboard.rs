// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Área de transferência via wl-copy (Wayland) ou xclip (X11), com limpeza
// automática — no espírito do `UIPasteboard.general`.

use std::time::Duration;

use crate::error::{Result, SboxError};
use crate::process::{Process, ProcessInfo, StandardInput, StandardOutput};

pub enum Pasteboard {
    Wayland,
    X11,
}

impl Pasteboard {
    /// A área de transferência da sessão gráfica atual.
    pub fn general() -> Pasteboard {
        match ProcessInfo::environment("WAYLAND_DISPLAY") {
            Some(_) => Pasteboard::Wayland,
            None => Pasteboard::X11,
        }
    }

    /// Copia `text` e, se `clearing_after` for dado, limpa depois desse tempo —
    /// mas só se o que estiver lá ainda for o mesmo texto.
    pub fn copy(&self, text: &str, clearing_after: Option<Duration>) -> Result<()> {
        let (tool, arguments) = self.copy_command();
        // Saída descartada: o wl-copy/xclip deixa um filho vivo servindo a área de
        // transferência, e ele não pode herdar nossos pipes (a leitura nunca terminaria).
        let result = Process::new(tool)
            .arguments(arguments)
            .standard_input(StandardInput::Data(text.as_bytes().to_vec()))
            .standard_output(StandardOutput::Discarded)
            .run_until_exit()?;
        guard!(result.succeeded(), else: SboxError::ClipboardFailed { tool, status: result.termination_status });

        match clearing_after {
            Some(delay) => self.schedule_clearing(text, delay),
            None => Ok(()),
        }
    }

    /// Um `sh` solto espera e limpa, comparando hashes para não apagar algo que
    /// o usuário copiou depois. O texto entra pelo stdin, nunca como argumento.
    fn schedule_clearing(&self, text: &str, delay: Duration) -> Result<()> {
        let (paste, clear) = match self {
            Pasteboard::Wayland => ("wl-paste -n", "wl-copy --clear"),
            Pasteboard::X11 => ("xclip -selection clipboard -o", "printf '' | xclip -selection clipboard -i"),
        };
        let seconds = delay.as_secs();
        let script = format!(
            "trap '' HUP INT; h=$(sha256sum); sleep {seconds}\n\
             [ \"$({paste} 2>/dev/null | sha256sum)\" = \"$h\" ] && {clear}"
        );
        Process::new("sh")
            .arguments(["-c", &script])
            .standard_input(StandardInput::Data(text.as_bytes().to_vec()))
            .standard_output(StandardOutput::Discarded)
            .run_detached()
    }

    fn copy_command(&self) -> (&'static str, &'static [&'static str]) {
        match self {
            Pasteboard::Wayland => ("wl-copy", &[]),
            Pasteboard::X11 => ("xclip", &["-selection", "clipboard", "-i"]),
        }
    }
}
