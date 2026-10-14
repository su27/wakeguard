use std::{ffi::OsStr, os::windows::ffi::OsStrExt, sync::OnceLock};
use windows_sys::Win32::{
    Foundation::*,
    System::{LibraryLoader::GetModuleHandleW, Power::*, Threading::*},
    UI::WindowsAndMessaging::*,
};

pub fn wide(value: impl AsRef<OsStr>) -> Vec<u16> {
    value.as_ref().encode_wide().chain(Some(0)).collect()
}

pub struct Handle(pub usize);
impl Drop for Handle {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0 as HANDLE);
        }
    }
}
pub fn single_instance(preview: bool) -> Result<Option<Handle>, String> {
    let name = wide(if preview {
        r"Local\WakeGuard.Tray.Preview"
    } else {
        r"Local\WakeGuard.Tray"
    });
    let handle = unsafe { CreateMutexW(std::ptr::null(), 0, name.as_ptr()) };
    if handle.is_null() {
        return Err(std::io::Error::last_os_error().to_string());
    }
    let owned = Handle(handle as usize);
    if unsafe { GetLastError() } == ERROR_ALREADY_EXISTS {
        drop(owned);
        Ok(None)
    } else {
        Ok(Some(owned))
    }
}

pub struct DisplayRequest {
    handle: Option<Handle>,
    active: bool,
}
impl DisplayRequest {
    pub fn new() -> Self {
        Self {
            handle: None,
            active: false,
        }
    }
    pub fn set(&mut self, active: bool) -> Result<(), String> {
        if self.active == active {
            return Ok(());
        }
        if active && self.handle.is_none() {
            let mut reason = wide("WakeGuard is keeping the display on");
            let context = REASON_CONTEXT {
                Version: 0,
                Flags: 1,
                Reason: REASON_CONTEXT_0 {
                    SimpleReasonString: reason.as_mut_ptr(),
                },
            };
            let raw = unsafe { PowerCreateRequest(&context) };
            if raw == INVALID_HANDLE_VALUE || raw.is_null() {
                return Err(std::io::Error::last_os_error().to_string());
            }
            self.handle = Some(Handle(raw as usize));
        }
        if let Some(handle) = &self.handle {
            let ok = unsafe {
                if active {
                    PowerSetRequest(handle.0 as HANDLE, PowerRequestDisplayRequired)
                } else {
                    PowerClearRequest(handle.0 as HANDLE, PowerRequestDisplayRequired)
                }
            };
            if ok == 0 {
                return Err(std::io::Error::last_os_error().to_string());
            }
        }
        self.active = active;
        if !active {
            self.handle = None;
        }
        Ok(())
    }
}

#[link(name = "user32")]
unsafe extern "system" {
    fn LockWorkStation() -> i32;
}
pub fn lock() -> Result<(), String> {
    if unsafe { LockWorkStation() } == 0 {
        Err(std::io::Error::last_os_error().to_string())
    } else {
        Ok(())
    }
}
pub fn screensaver() -> Result<(), String> {
    use winreg::{enums::HKEY_CURRENT_USER, RegKey};
    let configured: Option<String> = RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey(r"Control Panel\Desktop")
        .ok()
        .and_then(|key| key.get_value("SCRNSAVE.EXE").ok());
    let path = configured
        .and_then(|value| {
            let input = wide(value.trim().trim_matches('"'));
            let mut buffer = vec![0u16; 32768];
            let len = unsafe {
                windows_sys::Win32::System::Environment::ExpandEnvironmentStringsW(
                    input.as_ptr(),
                    buffer.as_mut_ptr(),
                    buffer.len() as u32,
                )
            };
            if len == 0 || len as usize > buffer.len() {
                return None;
            }
            Some(std::path::PathBuf::from(String::from_utf16_lossy(
                &buffer[..len as usize - 1],
            )))
        })
        .filter(|path| path.is_file())
        .unwrap_or_else(|| {
            std::path::PathBuf::from(
                std::env::var_os("SystemRoot").unwrap_or_else(|| "C:\\Windows".into()),
            )
            .join("System32/scrnsave.scr")
        });
    if !path.is_file() {
        return Err("No screen saver is configured or available / 未配置可用的屏幕保护程序".into());
    }
    let file = wide(path.as_os_str());
    let verb = wide("open");
    let arguments = wide("/s");
    let result = unsafe {
        windows_sys::Win32::UI::Shell::ShellExecuteW(
            std::ptr::null_mut(),
            verb.as_ptr(),
            file.as_ptr(),
            arguments.as_ptr(),
            std::ptr::null(),
            SW_SHOWNORMAL,
        )
    };
    if result as isize <= 32 {
        Err(format!(
            "Screen saver could not start ({})",
            result as isize
        ))
    } else {
        Ok(())
    }
}

pub fn session_id() -> u32 {
    let mut session = 0;
    unsafe {
        windows_sys::Win32::System::RemoteDesktop::ProcessIdToSessionId(
            std::process::id(),
            &mut session,
        );
    }
    session
}

static APP: OnceLock<tauri::AppHandle> = OnceLock::new();
unsafe extern "system" fn shutdown_proc(
    hwnd: HWND,
    message: u32,
    wp: WPARAM,
    lp: LPARAM,
) -> LRESULT {
    match message {
        WM_QUERYENDSESSION => return 1,
        WM_CLOSE | WM_ENDSESSION if message == WM_CLOSE || wp != 0 => {
            if let Some(app) = APP.get() {
                let app = app.clone();
                tauri::async_runtime::spawn(async move {
                    let _ = crate::quit_impl(app, true).await;
                });
            }
            return 0;
        }
        _ => {}
    }
    DefWindowProcW(hwnd, message, wp, lp)
}

// A real hidden top-level window lets MSI and session shutdown close the tray
// even when no WebView exists. It is intentionally not a message-only window.
pub fn shutdown_listener(app: tauri::AppHandle) -> Result<(), String> {
    let _ = APP.set(app);
    let (tx, rx) = std::sync::mpsc::sync_channel(1);
    std::thread::spawn(move || unsafe {
        let class = wide("WakeGuard.Tauri.Shutdown");
        let instance = GetModuleHandleW(std::ptr::null());
        let wc = WNDCLASSW {
            lpfnWndProc: Some(shutdown_proc),
            hInstance: instance,
            lpszClassName: class.as_ptr(),
            ..Default::default()
        };
        RegisterClassW(&wc);
        let title = wide("WakeGuard.Tray.Shutdown");
        let hwnd = CreateWindowExW(
            WS_EX_TOOLWINDOW,
            class.as_ptr(),
            title.as_ptr(),
            0,
            -32000,
            -32000,
            1,
            1,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            instance,
            std::ptr::null(),
        );
        let _ = tx.send(if hwnd.is_null() {
            Err(std::io::Error::last_os_error().to_string())
        } else {
            Ok(())
        });
        if hwnd.is_null() {
            return;
        }
        let mut message = MSG::default();
        while GetMessageW(&mut message, std::ptr::null_mut(), 0, 0) > 0 {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    });
    rx.recv().map_err(|e| e.to_string())?
}
