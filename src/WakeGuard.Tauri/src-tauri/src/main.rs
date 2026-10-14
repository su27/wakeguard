#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![allow(non_snake_case)]
mod engine;
mod ipc;
mod native;
mod placement;
mod settings;

use engine::{AppState, ViewState};
use ipc::Mode;
use std::sync::atomic::Ordering;
use tauri::{Emitter, Manager, WebviewUrl, WebviewWindowBuilder, WindowEvent};

fn icon(mode: Mode) -> tauri::image::Image<'static> {
    let bytes: &[u8] = match mode {
        Mode::KeepAwake => include_bytes!("../../../WakeGuard.Tray/Assets/TrayKeepAwake.ico"),
        Mode::KeepAwakeAndDisplayOn => {
            include_bytes!("../../../WakeGuard.Tray/Assets/TrayDisplayOn.ico")
        }
        Mode::Inactive => include_bytes!("../../../WakeGuard.Tray/Assets/TrayInactive.ico"),
    };
    tauri::image::Image::from_bytes(bytes).expect("Embedded icon is valid")
}

fn publish(app: &tauri::AppHandle, view: &ViewState) {
    let _ = app.emit("wakeguard-state", view);
    if let Some(tray) = app.tray_by_id("wakeguard") {
        let _ = tray.set_icon(Some(icon(view.mode)));
        let label = if !view.connected {
            "Service unavailable / 服务未连接"
        } else {
            match view.mode {
                Mode::Inactive => "System default / 未保持唤醒",
                Mode::KeepAwake => "Keep awake / 保持唤醒",
                Mode::KeepAwakeAndDisplayOn => "Keep screen on / 屏幕常亮",
            }
        };
        let _ = tray.set_tooltip(Some(format!("WakeGuard - {label}")));
    }
}

#[tauri::command]
async fn get_state(app: tauri::AppHandle) -> Result<ViewState, String> {
    Ok(app.state::<AppState>().runtime.lock().await.view.clone())
}

#[tauri::command]
async fn refresh(app: tauri::AppHandle) -> Result<ViewState, String> {
    let state = app.state::<AppState>();
    let mut runtime = state.runtime.lock().await;
    let result = runtime.refresh().await;
    let view = runtime.view.clone();
    drop(runtime);
    publish(&app, &view);
    result.map(|_| view)
}

#[tauri::command]
async fn set_mode(app: tauri::AppHandle, mode: Mode) -> Result<ViewState, String> {
    let state = app.state::<AppState>();
    let mut runtime = state.runtime.lock().await;
    runtime.expire();
    let (stop, minutes) = if mode != Mode::Inactive && runtime.view.mode != Mode::Inactive {
        (runtime.view.stop_at_utc, runtime.view.selected_minutes)
    } else {
        (None, None)
    };
    let result = runtime.change(mode, stop, minutes).await;
    let view = runtime.view.clone();
    drop(runtime);
    publish(&app, &view);
    result.map(|_| view)
}

#[tauri::command]
async fn set_duration(app: tauri::AppHandle, minutes: Option<u32>) -> Result<ViewState, String> {
    let stop = engine::deadline(minutes)?;
    let state = app.state::<AppState>();
    let mut runtime = state.runtime.lock().await;
    runtime.expire();
    if runtime.view.mode == Mode::Inactive {
        return Err("Select an awake mode first / 请先启用唤醒模式".into());
    }
    let mode = runtime.view.mode;
    let result = runtime.change(mode, stop, minutes).await;
    let view = runtime.view.clone();
    drop(runtime);
    publish(&app, &view);
    result.map(|_| view)
}

#[tauri::command]
async fn save_settings(
    app: tauri::AppHandle,
    settings: settings::Settings,
) -> Result<ViewState, String> {
    if settings.language > 1 {
        return Err("Invalid language".into());
    }
    let state = app.state::<AppState>();
    let mut runtime = state.runtime.lock().await;
    let old = runtime.view.settings.clone();
    if runtime.view.preview {
        runtime.view.settings = settings;
        let view = runtime.view.clone();
        drop(runtime);
        publish(&app, &view);
        return Ok(view);
    }
    let startup_changed = old.start_with_windows != settings.start_with_windows;
    if startup_changed {
        settings::startup(settings.start_with_windows)?;
    }
    if let Err(error) = settings::save(&settings) {
        if startup_changed {
            let _ = settings::startup(old.start_with_windows);
        }
        return Err(error);
    }
    runtime.view.settings = settings;
    let view = runtime.view.clone();
    drop(runtime);
    publish(&app, &view);
    Ok(view)
}

fn generation(app: &tauri::AppHandle, label: &str) -> u64 {
    let state = app.state::<AppState>();
    let mut windows = state.windows.lock().unwrap();
    let value = windows.entry(label.into()).or_default();
    *value += 1;
    *value
}

fn hide(app: &tauri::AppHandle, label: &str) {
    if let Some(window) = app.get_webview_window(label) {
        let _ = window.hide();
    }
    let token = generation(app, label);
    let app = app.clone();
    let label = label.to_string();
    tauri::async_runtime::spawn(async move {
        tokio::time::sleep(std::time::Duration::from_secs(15)).await;
        let scheduler = app.clone();
        let _ = scheduler.run_on_main_thread(move || {
            let state = app.state::<AppState>();
            let unchanged = state.windows.lock().unwrap().get(&label).copied() == Some(token);
            if unchanged {
                if let Some(window) = app.get_webview_window(&label) {
                    if !window.is_visible().unwrap_or(true) {
                        let _ = window.destroy();
                    }
                }
            }
        });
    });
}

fn position_panel(window: &tauri::WebviewWindow) -> Result<(), String> {
    use placement::{Bounds, PANEL_HEIGHT, PANEL_WIDTH};
    let app = window.app_handle();
    let anchor = *app.state::<AppState>().panel_anchor.lock().unwrap();
    let anchor = anchor.or_else(|| app.cursor_position().ok());
    let monitor = if let Some(point) = anchor {
        app.monitor_from_point(point.x, point.y)
            .map_err(|e| e.to_string())?
    } else {
        app.primary_monitor().map_err(|e| e.to_string())?
    };
    let monitor = monitor.ok_or("No display is available")?;
    let area = monitor.work_area();
    let work = Bounds {
        x: area.position.x,
        y: area.position.y,
        width: area.size.width as i32,
        height: area.size.height as i32,
    };
    let cursor = anchor
        .map(|point| (point.x as i32, point.y as i32))
        .unwrap_or((work.x + work.width - 24, work.y + work.height));
    let (x, y) = placement::panel_position(work, cursor);
    // The legacy popup is 620x490 physical pixels even at high DPI. WebView
    // logical sizing would multiply that by the desktop scale a second time.
    window
        .set_size(tauri::PhysicalSize::new(
            PANEL_WIDTH as u32,
            PANEL_HEIGHT as u32,
        ))
        .map_err(|e| e.to_string())?;
    window
        .set_position(tauri::PhysicalPosition::new(x, y))
        .map_err(|e| e.to_string())?;
    window
        .set_zoom(1.0 / monitor.scale_factor())
        .map_err(|e| e.to_string())?;
    #[cfg(debug_assertions)]
    eprintln!(
        "panel: work=({},{} {}x{}), cursor={cursor:?}, position=({x},{y}), size={:?}, dpi-scale={}",
        work.x,
        work.y,
        work.width,
        work.height,
        window.inner_size(),
        monitor.scale_factor()
    );
    Ok(())
}

fn open(app: &tauri::AppHandle, label: &str) -> Result<(), String> {
    if app.state::<AppState>().exiting.load(Ordering::SeqCst) {
        return Ok(());
    }
    if label == "panel" {
        *app.state::<AppState>().panel_anchor.lock().unwrap() = app.cursor_position().ok();
    }
    generation(app, label);
    if let Some(window) = app.get_webview_window(label) {
        if label == "panel" {
            position_panel(&window)?;
        }
        let _ = window.unminimize();
        window.show().map_err(|e| e.to_string())?;
        return window.set_focus().map_err(|e| e.to_string());
    }
    let panel = label == "panel";
    let window = WebviewWindowBuilder::new(
        app,
        label,
        WebviewUrl::App(format!("index.html?view={label}").into()),
    )
    .title(if panel {
        "WakeGuard"
    } else {
        "WakeGuard Settings"
    })
    .inner_size(
        if panel { 620.0 } else { 600.0 },
        if panel { 490.0 } else { 550.0 },
    )
    .resizable(false)
    .maximizable(false)
    .minimizable(false)
    .decorations(!panel)
    .always_on_top(panel)
    .skip_taskbar(panel)
    .visible(false)
    .shadow(true)
    .background_color(tauri::window::Color(32, 32, 32, 255))
    .build()
    .map_err(|e| e.to_string())?;
    if panel {
        position_panel(&window)?;
    } else {
        let _ = window.center();
    }
    let handle = app.clone();
    let window_label = label.to_string();
    let preview = app
        .state::<AppState>()
        .runtime
        .try_lock()
        .is_ok_and(|runtime| runtime.view.preview);
    window.on_window_event(move |event| match event {
        // Keep an explicitly requested developer preview visible for comparisons.
        WindowEvent::Focused(false) if panel && !preview => hide(&handle, &window_label),
        WindowEvent::Focused(true) => {
            generation(&handle, &window_label);
        }
        WindowEvent::CloseRequested { api, .. } => {
            api.prevent_close();
            hide(&handle, &window_label);
        }
        _ => {}
    });
    // The frontend calls ready only after localization, state, fonts and layout
    // are initialized. No partially initialized WebView is exposed on screen.
    Ok(())
}

#[tauri::command]
fn ready(window: tauri::WebviewWindow) -> Result<(), String> {
    if window.label() == "panel" {
        position_panel(&window)?;
    }
    generation(window.app_handle(), window.label());
    window.show().map_err(|e| e.to_string())?;
    window.set_focus().map_err(|e| e.to_string())
}
#[tauri::command]
fn display_scale(window: tauri::WebviewWindow) -> Result<f64, String> {
    window.scale_factor().map_err(|e| e.to_string())
}
#[tauri::command]
fn hide_window(window: tauri::WebviewWindow) {
    hide(window.app_handle(), window.label());
}
#[tauri::command]
async fn open_settings(app: tauri::AppHandle) -> Result<(), String> {
    hide(&app, "panel");
    open(&app, "settings")
}
#[tauri::command]
async fn lock_computer(app: tauri::AppHandle) -> Result<(), String> {
    if !app.state::<AppState>().runtime.lock().await.view.preview {
        native::lock()?;
    }
    hide(&app, "panel");
    Ok(())
}
#[tauri::command]
async fn start_screensaver(app: tauri::AppHandle) -> Result<(), String> {
    if !app.state::<AppState>().runtime.lock().await.view.preview {
        native::screensaver()?;
    }
    hide(&app, "panel");
    Ok(())
}

pub async fn quit_impl(app: tauri::AppHandle, force: bool) -> Result<(), String> {
    let state = app.state::<AppState>();
    if state.exiting.swap(true, Ordering::SeqCst) {
        return Ok(());
    }
    let mut runtime = state.runtime.lock().await;
    if runtime.view.mode != Mode::Inactive {
        if let Err(error) = runtime.request("ReleaseLease", Mode::Inactive, None).await {
            if !force {
                state.exiting.store(false, Ordering::SeqCst);
                return Err(error);
            }
        }
    }
    let _ = runtime.display.set(false);
    drop(runtime);
    app.exit(0);
    Ok(())
}
#[tauri::command]
async fn quit(app: tauri::AppHandle, force: bool) -> Result<(), String> {
    quit_impl(app, force).await
}

fn show_error(error: &str) {
    let text = native::wide(error);
    let title = native::wide("WakeGuard");
    unsafe {
        windows_sys::Win32::UI::WindowsAndMessaging::MessageBoxW(
            std::ptr::null_mut(),
            text.as_ptr(),
            title.as_ptr(),
            0x10,
        );
    }
}

fn main() {
    let preview = std::env::args().any(|argument| argument == "--preview");
    let _instance = match native::single_instance(preview) {
        Ok(Some(handle)) => handle,
        Ok(None) => {
            show_error("WakeGuard is already running / WakeGuard 已经在运行");
            return;
        }
        Err(error) => {
            show_error(&error);
            return;
        }
    };
    let settings = settings::load();
    if !preview && settings::installed() {
        if let Err(error) = settings::startup(settings.start_with_windows) {
            show_error(&error);
        }
    }
    let app = tauri::Builder::default()
        .manage(AppState::new(settings, preview))
        .invoke_handler(tauri::generate_handler![
            get_state,
            refresh,
            set_mode,
            set_duration,
            save_settings,
            ready,
            display_scale,
            hide_window,
            open_settings,
            lock_computer,
            start_screensaver,
            quit
        ])
        .setup(move |app| {
            native::shutdown_listener(app.handle().clone()).map_err(std::io::Error::other)?;
            tauri::tray::TrayIconBuilder::with_id("wakeguard")
                .icon(icon(Mode::Inactive))
                .tooltip("WakeGuard")
                .show_menu_on_left_click(false)
                .on_tray_icon_event(|tray, event| {
                    if let tauri::tray::TrayIconEvent::Click {
                        button,
                        button_state: tauri::tray::MouseButtonState::Up,
                        ..
                    } = event
                    {
                        if matches!(
                            button,
                            tauri::tray::MouseButton::Left | tauri::tray::MouseButton::Right
                        ) {
                            let app = tray.app_handle().clone();
                            tauri::async_runtime::spawn(async move {
                                if let Err(error) = open(&app, "panel") {
                                    show_error(&error);
                                }
                            });
                        }
                    }
                })
                .build(app)?;
            if preview {
                open(app.handle(), "panel").map_err(std::io::Error::other)?;
            }
            let handle = app.handle().clone();
            tauri::async_runtime::spawn(async move {
                loop {
                    tokio::time::sleep(std::time::Duration::from_secs(20)).await;
                    let state = handle.state::<AppState>();
                    if state.exiting.load(Ordering::SeqCst) {
                        break;
                    }
                    let mut runtime = state.runtime.lock().await;
                    if runtime.view.mode != Mode::Inactive {
                        let _ = runtime.refresh().await;
                        let view = runtime.view.clone();
                        drop(runtime);
                        publish(&handle, &view);
                    }
                }
            });
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("Could not initialize WakeGuard");
    app.run(|handle, event| {
        if let tauri::RunEvent::ExitRequested { api, .. } = event {
            if !handle.state::<AppState>().exiting.load(Ordering::SeqCst) {
                api.prevent_exit();
            }
        }
    });
}
