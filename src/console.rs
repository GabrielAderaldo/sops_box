// O terminal: cores suaves na saída e perguntas pelo `/dev/tty`.

use std::fs::{File, OpenOptions};
use std::io::{IsTerminal, Read, Write};
use std::mem::MaybeUninit;
use std::os::fd::AsRawFd;
use std::sync::LazyLock;
use std::sync::atomic::{AtomicI32, Ordering};

use crate::error::{Result, SboxError};
use crate::process::ProcessInfo;

/// Tons do Catppuccin Mocha / Tokyo Night: contraste baixo, sem branco nem
/// preto puros (cansa menos a vista).
#[derive(Clone, Copy)]
pub enum Style {
    Secondary,
    Accent,
    Success,
    Warning,
    Failure,
}

impl Style {
    fn rgb(self) -> &'static str {
        match self {
            Style::Secondary => "108;112;134",
            Style::Accent => "137;180;250",
            Style::Success => "166;227;161",
            Style::Warning => "249;226;175",
            Style::Failure => "243;139;168",
        }
    }

    /// Pinta para a saída padrão.
    pub fn paint(self, text: &str) -> String {
        self.paint_for(Stream::StandardOutput, text)
    }

    fn paint_for(self, stream: Stream, text: &str) -> String {
        if stream.supports_color() {
            format!("\x1b[38;2;{}m{text}\x1b[0m", self.rgb())
        } else {
            text.to_owned()
        }
    }
}

#[derive(Clone, Copy)]
enum Stream {
    StandardOutput,
    StandardError,
}

static STANDARD_OUTPUT_SUPPORTS_COLOR: LazyLock<bool> =
    LazyLock::new(|| std::io::stdout().is_terminal() && ProcessInfo::environment("NO_COLOR").is_none());
static STANDARD_ERROR_SUPPORTS_COLOR: LazyLock<bool> =
    LazyLock::new(|| std::io::stderr().is_terminal() && ProcessInfo::environment("NO_COLOR").is_none());

impl Stream {
    fn supports_color(self) -> bool {
        match self {
            Stream::StandardOutput => *STANDARD_OUTPUT_SUPPORTS_COLOR,
            Stream::StandardError => *STANDARD_ERROR_SUPPORTS_COLOR,
        }
    }
}

pub struct Console;

impl Console {
    // MARK: - Mensagens (sempre no stderr, para não sujar a saída de dados)

    /// `✓ mensagem` em verde.
    pub fn success(message: &str) {
        eprintln!("{}", Style::Success.paint_for(Stream::StandardError, &format!("✓ {message}")));
    }

    /// `✓ mensagem` em verde, seguida de uma dica discreta.
    pub fn success_with_hint(message: &str, hint: &str) {
        eprintln!(
            "{}{}",
            Style::Success.paint_for(Stream::StandardError, &format!("✓ {message}")),
            Style::Secondary.paint_for(Stream::StandardError, hint)
        );
    }

    pub fn warning(message: &str) {
        eprintln!("{}", Style::Warning.paint_for(Stream::StandardError, &format!("⚠ {message}")));
    }

    pub fn note(message: &str) {
        eprintln!("{message}");
    }

    pub fn error(error: &SboxError) {
        eprintln!("{}{error}", Style::Failure.paint_for(Stream::StandardError, "erro: "));
    }

    // MARK: - Estado do terminal

    /// Saída padrão é um terminal com cores (e não um pipe ou arquivo).
    pub fn is_interactive_output() -> bool {
        Stream::StandardOutput.supports_color()
    }

    pub fn is_standard_input_a_terminal() -> bool {
        std::io::stdin().is_terminal()
    }

    // MARK: - Perguntas

    /// Lê uma linha do terminal sem eco (para senhas e tokens).
    pub fn read_secure_line(prompt: &str) -> Result<String> {
        let mut terminal = open_terminal().ok_or(SboxError::NoTerminal)?;
        let _ = terminal.write_all(prompt.as_bytes());

        let echo = EchoGuard::disable(&terminal);
        let line = read_line(&mut terminal);
        drop(echo);

        let _ = terminal.write_all(b"\n");
        Ok(line)
    }

    /// Pergunta s/N. Sem terminal, a resposta é não.
    pub fn confirm(question: &str) -> bool {
        let Some(mut terminal) = open_terminal() else { return false };
        let _ = terminal.write_all(format!("{question} [s/N] ").as_bytes());
        matches!(read_line(&mut terminal).to_lowercase().as_str(), "s" | "sim" | "y" | "yes")
    }
}

fn open_terminal() -> Option<File> {
    OpenOptions::new().read(true).write(true).open("/dev/tty").ok()
}

fn read_line(terminal: &mut File) -> String {
    let mut bytes = Vec::new();
    let mut byte = [0u8; 1];
    while terminal.read(&mut byte).unwrap_or(0) == 1 && byte[0] != b'\n' {
        bytes.push(byte[0]);
    }
    String::from_utf8_lossy(&bytes).into_owned()
}

// MARK: - Eco do terminal

// Estado global só para o handler de SIGINT conseguir devolver o eco: um Ctrl-C
// no meio da pergunta não pode deixar o terminal do usuário mudo.
static mut ORIGINAL_SETTINGS: MaybeUninit<libc::termios> = MaybeUninit::uninit();
static SILENCED_TERMINAL: AtomicI32 = AtomicI32::new(-1);

/// Desliga o eco ao ser criado e religa ao sair de escopo (como um `defer`).
struct EchoGuard {
    descriptor: i32,
}

impl EchoGuard {
    fn disable(terminal: &File) -> EchoGuard {
        let descriptor = terminal.as_raw_fd();
        unsafe {
            let original = &raw mut ORIGINAL_SETTINGS;
            libc::tcgetattr(descriptor, original.cast());
            let mut silent = (*original).assume_init_read();
            silent.c_lflag &= !libc::ECHO;
            SILENCED_TERMINAL.store(descriptor, Ordering::SeqCst);
            libc::tcsetattr(descriptor, libc::TCSAFLUSH, &silent);
            libc::signal(libc::SIGINT, restore_echo_and_exit as *const () as libc::sighandler_t);
        }
        EchoGuard { descriptor }
    }
}

impl Drop for EchoGuard {
    fn drop(&mut self) {
        unsafe {
            libc::tcsetattr(self.descriptor, libc::TCSAFLUSH, (&raw const ORIGINAL_SETTINGS).cast());
            SILENCED_TERMINAL.store(-1, Ordering::SeqCst);
            libc::signal(libc::SIGINT, libc::SIG_DFL);
        }
    }
}

extern "C" fn restore_echo_and_exit(_signal: libc::c_int) {
    let descriptor = SILENCED_TERMINAL.load(Ordering::SeqCst);
    unsafe {
        if descriptor >= 0 {
            libc::tcsetattr(descriptor, libc::TCSAFLUSH, (&raw const ORIGINAL_SETTINGS).cast());
        }
        libc::_exit(130);
    }
}
