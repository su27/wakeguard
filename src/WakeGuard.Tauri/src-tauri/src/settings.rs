use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use winreg::{enums::HKEY_CURRENT_USER, RegKey};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "PascalCase")]
pub struct Settings {
    pub start_with_windows: bool,
    pub language: u8,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            start_with_windows: true,
            language: if unsafe { windows_sys::Win32::Globalization::GetUserDefaultUILanguage() }
                & 0x3ff
                == 4
            {
                0
            } else {
                1
            },
        }
    }
}

fn path() -> Result<PathBuf, String> {
    Ok(
        PathBuf::from(std::env::var_os("LOCALAPPDATA").ok_or("LOCALAPPDATA is unavailable")?)
            .join("WakeGuard/settings.json"),
    )
}
pub fn load() -> Settings {
    path()
        .ok()
        .and_then(|p| std::fs::read(p).ok())
        .and_then(|b| serde_json::from_slice::<Settings>(&b).ok())
        .filter(|s| s.language <= 1)
        .unwrap_or_default()
}
pub fn save(settings: &Settings) -> Result<(), String> {
    if settings.language > 1 {
        return Err("Invalid language".into());
    }
    let path = path()?;
    std::fs::create_dir_all(path.parent().unwrap()).map_err(|e| e.to_string())?;
    let temp = path.with_extension("tauri.tmp");
    std::fs::write(
        &temp,
        serde_json::to_vec_pretty(settings).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    let from = crate::native::wide(temp.as_os_str());
    let to = crate::native::wide(path.as_os_str());
    if unsafe {
        windows_sys::Win32::Storage::FileSystem::MoveFileExW(from.as_ptr(), to.as_ptr(), 1 | 8)
    } == 0
    {
        return Err(std::io::Error::last_os_error().to_string());
    }
    Ok(())
}
pub fn installed() -> bool {
    std::env::current_exe()
        .ok()
        .zip(std::env::var_os("ProgramFiles"))
        .is_some_and(|(exe, root)| exe.starts_with(PathBuf::from(root)))
}
pub fn startup(enabled: bool) -> Result<(), String> {
    let user = RegKey::predef(HKEY_CURRENT_USER);
    let (key, _) = user
        .create_subkey(r"Software\Microsoft\Windows\CurrentVersion\Run")
        .map_err(|e| e.to_string())?;
    if enabled {
        let exe = std::env::current_exe().map_err(|e| e.to_string())?;
        key.set_value("WakeGuard", &format!("\"{}\"", exe.display()))
            .map_err(|e| e.to_string())
    } else {
        match key.delete_value("WakeGuard") {
            Ok(()) => Ok(()),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(e) => Err(e.to_string()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn preserves_existing_winforms_settings_format() {
        let settings: Settings =
            serde_json::from_str(r#"{"StartWithWindows":false,"Language":1}"#).unwrap();
        assert!(!settings.start_with_windows);
        assert_eq!(settings.language, 1);
        assert!(serde_json::to_string(&settings)
            .unwrap()
            .contains("StartWithWindows"));
    }
}
