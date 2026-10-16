// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// Área de transferência via wl-copy (Wayland), xclip (X11) ou pbcopy (macOS),
// com limpeza automática — no espírito do `UIPasteboard.general`.

use std::time::Duration;

use crate::error::{Result, SboxError};
use crate::process::{Process, ProcessInfo, StandardInput, StandardOutput};

pub enum Pasteboard {
    Wayland,
    X11,
    MacOs,
}

impl Pasteboard {
    /// A área de transferência da sessão gráfica atual.
    pub fn general() -> Pasteboard {
        // No macOS a plataforma já decide em tempo de compilação: não há Wayland a considerar.
        if cfg!(target_os = "macos") {
            return Pasteboard::MacOs;
        }
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
        // O pbcopy entrega ao pasteboard do sistema e termina; descartar não o afeta.
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
        // `shasum -a 256` no macOS: o `sha256sum` só existe nas versões mais recentes.
        let (paste, clear, hash) = match self {
            Pasteboard::Wayland => ("wl-paste -n", "wl-copy --clear", "sha256sum"),
            Pasteboard::X11 => {
                ("xclip -selection clipboard -o", "printf '' | xclip -selection clipboard -i", "sha256sum")
            }
            Pasteboard::MacOs => ("pbpaste", "printf '' | pbcopy", "shasum -a 256"),
        };
        let seconds = delay.as_secs();
        let script = format!(
            "trap '' HUP INT; h=$({hash}); sleep {seconds}\n\
             [ \"$({paste} 2>/dev/null | {hash})\" = \"$h\" ] && {clear}"
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
            Pasteboard::MacOs => ("pbcopy", &[]),
        }
    }
}
