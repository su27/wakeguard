use crate::{
    ipc::{self, Mode, Request},
    native::DisplayRequest,
    settings::Settings,
};
use chrono::{DateTime, Duration, Utc};
use serde::Serialize;
use std::sync::{atomic::AtomicBool, Arc};
use tokio::sync::Mutex;

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ViewState {
    pub preview: bool,
    pub mode: Mode,
    pub effective_mode: Mode,
    pub stop_at_utc: Option<DateTime<Utc>>,
    pub selected_minutes: Option<u32>,
    pub connected: bool,
    pub settings: Settings,
    pub version: &'static str,
}
pub struct Runtime {
    pub view: ViewState,
    pub client_id: String,
    pub display: DisplayRequest,
}
pub struct AppState {
    pub runtime: Arc<Mutex<Runtime>>,
    pub exiting: AtomicBool,
    pub windows: std::sync::Mutex<std::collections::HashMap<String, u64>>,
    pub panel_anchor: std::sync::Mutex<Option<tauri::PhysicalPosition<f64>>>,
}
impl AppState {
    pub fn new(settings: Settings, preview: bool) -> Self {
        Self {
            runtime: Arc::new(Mutex::new(Runtime {
                view: ViewState {
                    preview,
                    mode: Mode::Inactive,
                    effective_mode: Mode::Inactive,
                    stop_at_utc: None,
                    selected_minutes: None,
                    connected: true,
                    settings,
                    version: env!("CARGO_PKG_VERSION"),
                },
                client_id: uuid::Uuid::new_v4().to_string(),
                display: DisplayRequest::new(),
            })),
            exiting: AtomicBool::new(false),
            windows: Default::default(),
            panel_anchor: Default::default(),
        }
    }
}
impl Runtime {
    pub fn expire(&mut self) {
        if self.view.stop_at_utc.is_some_and(|stop| stop <= Utc::now()) {
            self.view.mode = Mode::Inactive;
            self.view.stop_at_utc = None;
            self.view.selected_minutes = None;
        }
    }
    pub async fn request(
        &self,
        kind: &str,
        mode: Mode,
        stop: Option<DateTime<Utc>>,
    ) -> Result<ipc::Response, String> {
        if self.view.preview {
            return Ok(ipc::Response {
                protocol_version: 1,
                success: true,
                error_code: None,
                error_message: None,
                effective_mode: mode,
                client_mode: mode,
            });
        }
        ipc::send(&Request {
            protocol_version: 1,
            kind,
            client_id: &self.client_id,
            session_id: crate::native::session_id(),
            process_id: std::process::id(),
            requested_mode: mode,
            stop_at_utc: stop,
        })
        .await
    }
    pub async fn refresh(&mut self) -> Result<(), String> {
        self.expire();
        if !self.view.preview {
            self.display
                .set(self.view.mode == Mode::KeepAwakeAndDisplayOn)?;
        }
        let kind = if self.view.mode == Mode::Inactive {
            "QueryStatus"
        } else {
            "UpsertLease"
        };
        match self
            .request(kind, self.view.mode, self.view.stop_at_utc)
            .await
        {
            Ok(response) => {
                self.view.connected = true;
                self.view.effective_mode = response.effective_mode;
                Ok(())
            }
            Err(error) => {
                self.view.connected = false;
                Err(error)
            }
        }
    }
    pub async fn change(
        &mut self,
        mode: Mode,
        stop: Option<DateTime<Utc>>,
        minutes: Option<u32>,
    ) -> Result<(), String> {
        let previous_display = self.view.mode == Mode::KeepAwakeAndDisplayOn;
        if !self.view.preview {
            self.display.set(mode == Mode::KeepAwakeAndDisplayOn)?;
        }
        let kind = if mode == Mode::Inactive {
            "ReleaseLease"
        } else {
            "UpsertLease"
        };
        match self.request(kind, mode, stop).await {
            Ok(response) => {
                self.view.mode = mode;
                self.view.effective_mode = response.effective_mode;
                self.view.stop_at_utc = stop;
                self.view.selected_minutes = minutes;
                self.view.connected = true;
                Ok(())
            }
            Err(error) => {
                let _ = self.display.set(previous_display);
                self.view.connected = false;
                Err(error)
            }
        }
    }
}
pub fn deadline(minutes: Option<u32>) -> Result<Option<DateTime<Utc>>, String> {
    match minutes {
        None => Ok(None),
        Some(value @ (30 | 60 | 120 | 240)) => {
            Ok(Some(Utc::now() + Duration::minutes(value as i64)))
        }
        _ => Err("Invalid duration".into()),
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rejects_unbounded_duration_values() {
        assert!(deadline(Some(u32::MAX)).is_err());
        assert!(deadline(Some(0)).is_err());
        assert!(deadline(None).unwrap().is_none());
        assert!(deadline(Some(30)).unwrap().unwrap() > Utc::now());
    }
    #[test]
    fn expired_timer_clears_local_mode_and_duration() {
        let state = AppState::new(
            Settings {
                start_with_windows: false,
                language: 1,
            },
            true,
        );
        let mut runtime = state.runtime.try_lock().unwrap();
        runtime.view.mode = Mode::KeepAwake;
        runtime.view.stop_at_utc = Some(Utc::now() - Duration::seconds(1));
        runtime.view.selected_minutes = Some(30);
        runtime.expire();
        assert_eq!(runtime.view.mode, Mode::Inactive);
        assert!(runtime.view.selected_minutes.is_none());
    }
}
