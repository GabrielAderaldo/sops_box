// SPDX-FileCopyrightText: 2026 Gabriel Vieira Soriano Aderaldo
// SPDX-License-Identifier: AGPL-3.0-or-later

// O `NSPasteboard` do AppKit, chamado direto pelo runtime do Objective-C, sem
// crate: cada mensagem é um `objc_msgSend` convertido para a assinatura exata
// dela (no arm64, chamar o `objc_msgSend` como variádica quebra a ABI). Só o
// macOS compila a implementação; nos outros sistemas, escrever sempre falha.

use crate::error::{Result, SboxError};

/// Marcas do nspasteboard.org que gerenciadores de histórico de clipboard
/// (Maccy, Raycast, Alfred, Paste…) respeitam para não guardar o conteúdo.
const CONCEALED_TYPE: &str = "org.nspasteboard.ConcealedType";
const TRANSIENT_TYPE: &str = "org.nspasteboard.TransientType";

pub struct GeneralPasteboard;

impl GeneralPasteboard {
    /// Escreve `text` como string e marca o conteúdo como sensível. Com
    /// `transient`, avisa também que ele vai sumir logo (a limpeza agendada).
    pub fn write_concealed(text: &str, transient: bool) -> Result<()> {
        let markers: &[&str] = if transient { &[CONCEALED_TYPE, TRANSIENT_TYPE] } else { &[CONCEALED_TYPE] };
        write_string(text, markers)
    }
}

/// Fora do macOS não há `NSPasteboard` (e o `Pasteboard::general()` nunca chega aqui).
#[cfg(not(target_os = "macos"))]
fn write_string(_text: &str, _markers: &[&str]) -> Result<()> {
    Err(SboxError::PasteboardFailed)
}

#[cfg(target_os = "macos")]
fn write_string(text: &str, markers: &[&str]) -> Result<()> {
    const APPKIT: &std::ffi::CStr = c"/System/Library/Frameworks/AppKit.framework/AppKit";

    // Carrega o AppKit só aqui: os outros comandos não pagam o custo.
    let appkit = unsafe { libc::dlopen(APPKIT.as_ptr(), libc::RTLD_LAZY | libc::RTLD_LOCAL) };
    guard!(!appkit.is_null(), else: SboxError::PasteboardFailed);

    let pool = unsafe { objc::objc_autoreleasePoolPush() };
    let written = unsafe { objc::write_string_in_pool(text, markers) };
    unsafe { objc::objc_autoreleasePoolPop(pool) };
    guard!(written, else: SboxError::PasteboardFailed);
    Ok(())
}

#[cfg(target_os = "macos")]
mod objc {
    use std::ffi::{CStr, c_char, c_void};
    use std::mem::transmute;

    type Id = *mut c_void;
    type Sel = *const c_void;
    type Message = unsafe extern "C" fn();

    /// `NSUTF8StringEncoding`.
    const UTF8_ENCODING: usize = 4;
    /// `NSPasteboardTypeString`.
    const STRING_TYPE: &str = "public.utf8-plain-text";

    #[link(name = "objc")]
    unsafe extern "C" {
        fn objc_getClass(name: *const c_char) -> Id;
        fn sel_registerName(name: *const c_char) -> Sel;
        fn objc_msgSend();
        pub fn objc_autoreleasePoolPush() -> *mut c_void;
        pub fn objc_autoreleasePoolPop(pool: *mut c_void);
    }

    /// `[NSPasteboard generalPasteboard]`, `clearContents` e um `setString:forType:`
    /// para o texto e outro (vazio) para cada marca. Precisa de um autorelease pool.
    pub unsafe fn write_string_in_pool(text: &str, markers: &[&str]) -> bool {
        unsafe {
            let class = objc_getClass(c"NSPasteboard".as_ptr());
            if class.is_null() {
                return false;
            }
            let pasteboard = send(class, c"generalPasteboard");
            if pasteboard.is_null() {
                return false;
            }
            send_returning_integer(pasteboard, c"clearContents");

            let mut written = set_string(pasteboard, text, STRING_TYPE);
            for marker in markers {
                written &= set_string(pasteboard, "", marker);
            }
            written
        }
    }

    unsafe fn set_string(pasteboard: Id, text: &str, pasteboard_type: &str) -> bool {
        unsafe {
            let set_string_for_type = transmute::<Message, unsafe extern "C" fn(Id, Sel, Id, Id) -> i8>(objc_msgSend);
            let (string, kind) = (ns_string(text), ns_string(pasteboard_type));
            !string.is_null()
                && !kind.is_null()
                && set_string_for_type(pasteboard, selector(c"setString:forType:"), string, kind) != 0
        }
    }

    /// Um `NSString` autoreleased com os bytes UTF-8 de `text` (aceita `\0` no meio).
    unsafe fn ns_string(text: &str) -> Id {
        unsafe {
            let init_with_bytes =
                transmute::<Message, unsafe extern "C" fn(Id, Sel, *const c_void, usize, usize) -> Id>(objc_msgSend);
            let allocated = send(objc_getClass(c"NSString".as_ptr()), c"alloc");
            let string = init_with_bytes(
                allocated,
                selector(c"initWithBytes:length:encoding:"),
                text.as_ptr().cast(),
                text.len(),
                UTF8_ENCODING,
            );
            if string.is_null() { string } else { send(string, c"autorelease") }
        }
    }

    unsafe fn send(receiver: Id, name: &CStr) -> Id {
        unsafe { transmute::<Message, unsafe extern "C" fn(Id, Sel) -> Id>(objc_msgSend)(receiver, selector(name)) }
    }

    unsafe fn send_returning_integer(receiver: Id, name: &CStr) -> isize {
        unsafe { transmute::<Message, unsafe extern "C" fn(Id, Sel) -> isize>(objc_msgSend)(receiver, selector(name)) }
    }

    unsafe fn selector(name: &CStr) -> Sel {
        unsafe { sel_registerName(name.as_ptr()) }
    }
}
