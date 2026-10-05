// Processos filhos com a cara do `Process` da Foundation: monta-se o
// processo (executável, argumentos, entrada, saída) e depois roda.

use std::io::Write;
use std::os::unix::process::ExitStatusExt;
use std::process::{Command, Stdio};

use crate::error::{Result, SboxError};

pub struct ProcessInfo;

impl ProcessInfo {
    /// `ProcessInfo.processInfo.environment[name]`.
    pub fn environment(name: &str) -> Option<String> {
        std::env::var(name).ok()
    }
}

pub enum StandardInput {
    /// `/dev/null`.
    Nothing,
    /// O mesmo stdin do sbox (para comandos interativos, como o `sops edit`).
    Inherited,
    /// Bytes escritos num pipe — é assim que secrets chegam aos filhos, nunca
    /// como argumento (argumentos aparecem em `/proc/<pid>/cmdline`).
    Data(Vec<u8>),
}

pub enum StandardOutput {
    /// stdout e stderr vão para a memória (`ProcessResult`).
    Captured,
    /// `/dev/null`.
    Discarded,
    Inherited,
}

pub struct ProcessResult {
    pub termination_status: i32,
    pub standard_output: Vec<u8>,
    pub standard_error: Vec<u8>,
}

impl ProcessResult {
    pub fn succeeded(&self) -> bool {
        self.termination_status == 0
    }

    pub fn standard_error_text(&self) -> String {
        String::from_utf8_lossy(&self.standard_error).trim_end().to_owned()
    }
}

pub struct Process {
    executable: String,
    arguments: Vec<String>,
    standard_input: StandardInput,
    standard_output: StandardOutput,
}

impl Process {
    /// O executável é procurado no PATH.
    pub fn new(executable: &str) -> Process {
        Process {
            executable: executable.into(),
            arguments: Vec::new(),
            standard_input: StandardInput::Nothing,
            standard_output: StandardOutput::Captured,
        }
    }

    pub fn arguments<Argument: AsRef<str>>(mut self, arguments: impl IntoIterator<Item = Argument>) -> Process {
        self.arguments = arguments.into_iter().map(|argument| argument.as_ref().to_owned()).collect();
        self
    }

    pub fn standard_input(mut self, input: StandardInput) -> Process {
        self.standard_input = input;
        self
    }

    pub fn standard_output(mut self, output: StandardOutput) -> Process {
        self.standard_output = output;
        self
    }

    /// Roda e espera terminar.
    pub fn run_until_exit(self) -> Result<ProcessResult> {
        let executable = self.executable.clone();
        let child = self.launch()?;
        let output = child
            .wait_with_output()
            .map_err(|reason| SboxError::LaunchFailed { program: executable, reason })?;
        let termination_status = output
            .status
            .code()
            .unwrap_or_else(|| 128 + output.status.signal().unwrap_or(0));
        Ok(ProcessResult {
            termination_status,
            standard_output: output.stdout,
            standard_error: output.stderr,
        })
    }

    /// Roda e deixa o processo solto, sem esperar.
    pub fn run_detached(self) -> Result<()> {
        self.launch().map(drop)
    }

    fn launch(self) -> Result<std::process::Child> {
        let (stdout, stderr) = match self.standard_output {
            StandardOutput::Captured => (Stdio::piped(), Stdio::piped()),
            StandardOutput::Discarded => (Stdio::null(), Stdio::null()),
            StandardOutput::Inherited => (Stdio::inherit(), Stdio::inherit()),
        };
        let stdin = match self.standard_input {
            StandardInput::Nothing => Stdio::null(),
            StandardInput::Inherited => Stdio::inherit(),
            StandardInput::Data(_) => Stdio::piped(),
        };

        let launch_failed = |reason| SboxError::LaunchFailed { program: self.executable.clone(), reason };
        let mut child = Command::new(&self.executable)
            .args(&self.arguments)
            .stdin(stdin)
            .stdout(stdout)
            .stderr(stderr)
            .spawn()
            .map_err(launch_failed)?;

        if let StandardInput::Data(bytes) = &self.standard_input {
            // Os comandos que usamos leem o stdin inteiro antes de escrever, então
            // escrever tudo antes de ler a saída não trava. O pipe fecha ao sair
            // do escopo, e o filho recebe EOF.
            let mut pipe = child.stdin.take().expect("stdin foi configurado como pipe");
            pipe.write_all(bytes).map_err(launch_failed)?;
        }
        Ok(child)
    }
}
