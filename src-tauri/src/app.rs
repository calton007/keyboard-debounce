use crate::{
    model::*,
    persistence::{SaveSlot, Store},
    platform::{self, Command, Driver},
    runtime::Runtime,
};
use std::{
    sync::{
        atomic::{AtomicBool, Ordering},
        mpsc,
    },
    time::{Duration, Instant},
};
use tauri::{
    Emitter, Manager,
    menu::{Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
};
use tauri_plugin_dialog::DialogExt;

pub struct AppState {
    driver: Driver,
    supervisor: mpsc::Sender<SupervisorCommand>,
    exiting: AtomicBool,
}
enum SupervisorCommand {
    Refresh(mpsc::Sender<Result<(), String>>),
    Exit(mpsc::Sender<Result<(), String>>),
}

async fn dispatch(state: &AppState, command: Command) -> Result<Snapshot, String> {
    let driver = state.driver.clone();
    tauri::async_runtime::spawn_blocking(move || driver.request(command).map(|f| f.snapshot))
        .await
        .map_err(|e| e.to_string())?
}
#[tauri::command]
async fn get_snapshot(state: tauri::State<'_, AppState>) -> Result<Snapshot, String> {
    dispatch(&state, Command::Read).await
}
#[tauri::command]
async fn update_settings(
    state: tauri::State<'_, AppState>,
    patch: SettingsPatch,
) -> Result<Snapshot, String> {
    dispatch(&state, Command::Patch(patch)).await
}
#[tauri::command]
async fn set_enabled(state: tauri::State<'_, AppState>, enabled: bool) -> Result<Snapshot, String> {
    dispatch(&state, Command::Enable(enabled)).await
}
#[tauri::command]
async fn apply_hotkey(
    state: tauri::State<'_, AppState>,
    hotkey: String,
) -> Result<Snapshot, String> {
    dispatch(&state, Command::Hotkey(hotkey)).await
}
#[tauri::command]
async fn set_startup(state: tauri::State<'_, AppState>, enabled: bool) -> Result<Snapshot, String> {
    dispatch(&state, Command::Startup(enabled)).await
}
#[tauri::command]
async fn change_games(
    state: tauri::State<'_, AppState>,
    names: Vec<String>,
    add: bool,
) -> Result<Snapshot, String> {
    dispatch(&state, Command::Games { names, add }).await
}
#[tauri::command]
async fn set_key_rule(
    state: tauri::State<'_, AppState>,
    vk: u16,
    ignored: Option<bool>,
    game: Option<bool>,
) -> Result<Snapshot, String> {
    dispatch(&state, Command::Rule { vk, ignored, game }).await
}
#[tauri::command]
async fn clear_ignored(state: tauri::State<'_, AppState>) -> Result<Snapshot, String> {
    dispatch(&state, Command::ClearIgnored).await
}
#[tauri::command]
async fn reset_learning(state: tauri::State<'_, AppState>) -> Result<Snapshot, String> {
    dispatch(&state, Command::ResetLearning).await
}
#[tauri::command]
async fn refresh_admin(state: tauri::State<'_, AppState>) -> Result<Snapshot, String> {
    dispatch(&state, Command::Admin).await
}
#[tauri::command]
async fn list_processes() -> Result<Vec<String>, String> {
    tauri::async_runtime::spawn_blocking(|| {
        let result = platform::enumerate_processes();
        if let Some(error) = result.error {
            Err(error)
        } else {
            Ok(result.names)
        }
    })
    .await
    .map_err(|e| e.to_string())?
}
#[tauri::command]
async fn browse_executables(app: tauri::AppHandle) -> Result<Vec<String>, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let files = app
            .dialog()
            .file()
            .set_title("选择游戏 EXE（不会运行所选文件）")
            .add_filter("Windows 程序", &["exe"])
            .blocking_pick_files();
        files
            .unwrap_or_default()
            .into_iter()
            .map(|file| {
                let path = file.into_path().map_err(|e| e.to_string())?;
                let name = path
                    .file_name()
                    .ok_or("无法获取 EXE 文件名")?
                    .to_string_lossy();
                normalize_exe(&name)
            })
            .collect()
    })
    .await
    .map_err(|e| e.to_string())?
}
#[tauri::command]
async fn open_config_directory(state: tauri::State<'_, AppState>) -> Result<(), String> {
    let snapshot = dispatch(&state, Command::Read).await?;
    platform::open_directory(std::path::Path::new(&snapshot.config_directory))
}
#[tauri::command]
async fn refresh_games(state: tauri::State<'_, AppState>) -> Result<Snapshot, String> {
    let supervisor = state.supervisor.clone();
    tauri::async_runtime::spawn_blocking(move || {
        let (tx, rx) = mpsc::channel();
        supervisor
            .send(SupervisorCommand::Refresh(tx))
            .map_err(|e| e.to_string())?;
        rx.recv().map_err(|e| e.to_string())?
    })
    .await
    .map_err(|e| e.to_string())??;
    dispatch(&state, Command::Read).await
}
fn show_window(app: &tauri::AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("main") {
        window.unminimize().map_err(|e| e.to_string())?;
        window.show().map_err(|e| e.to_string())?;
        window.set_focus().map_err(|e| e.to_string())?;
    } else {
        // WebView is created only on demand and destroyed by the native Close action.
        tauri::WebviewWindowBuilder::new(app, "main", tauri::WebviewUrl::App("index.html".into()))
            .title("Keyboard Debounce")
            .inner_size(1160.0, 760.0)
            .min_inner_size(960.0, 640.0)
            .theme(Some(tauri::Theme::Light))
            .build()
            .map_err(|e| e.to_string())?;
    }
    Ok(())
}
fn request_exit(app: tauri::AppHandle) {
    let state = app.state::<AppState>();
    if state.exiting.swap(true, Ordering::SeqCst) {
        return;
    }
    let tx = state.supervisor.clone();
    std::thread::spawn(move || {
        let (reply, received) = mpsc::channel();
        let result = tx
            .send(SupervisorCommand::Exit(reply))
            .map_err(|e| e.to_string())
            .and_then(|_| received.recv().map_err(|e| e.to_string()))
            .and_then(|r| r);
        if let Err(error) = result {
            platform::message(&format!("退出时未能完整保存：{error}"), true);
        }
        app.exit(0);
    });
}
fn supervise(
    app: tauri::AppHandle,
    driver: Driver,
    store: Store,
    receiver: mpsc::Receiver<SupervisorCommand>,
) {
    let mut settings_save = SaveSlot::default();
    let mut learning_save = SaveSlot::default();
    let mut next_detection = Instant::now();
    let mut last_configuration = 0;
    let mut last_emit = Instant::now();
    let mut last_revision = 0;
    let mut last_important = String::new();
    let mut had_window = false;
    loop {
        let pending = match receiver.recv_timeout(Duration::from_millis(50)) {
            Ok(command) => Some(command),
            Err(mpsc::RecvTimeoutError::Timeout) => None,
            Err(mpsc::RecvTimeoutError::Disconnected) => return,
        };
        let mut frame = match driver.request(Command::Read) {
            Ok(frame) => frame,
            Err(error) => {
                platform::message(&format!("键盘核心故障，程序将退出：{error}"), true);
                app.state::<AppState>()
                    .exiting
                    .store(true, Ordering::SeqCst);
                app.exit(1);
                return;
            }
        };
        if let Some(SupervisorCommand::Exit(reply)) = pending {
            // Stop input first. The returned final frame retains the user's enabled preference.
            let stopped = driver.request(Command::Shutdown);
            let result = stopped.and_then(|f| {
                let settings = store.save("settings.json", &f.snapshot.settings);
                let learning = store.save("learning-state.json", &f.learning);
                match (settings, learning) {
                    (Ok(()), Ok(())) => Ok(()),
                    (a, b) => Err(format!("设置：{a:?}；学习：{b:?}")),
                }
            });
            let _ = reply.send(result);
            return;
        }
        let manual = matches!(pending, Some(SupervisorCommand::Refresh(_)));
        let now = Instant::now();
        if manual
            || now >= next_detection
            || last_configuration != frame.game_configuration_revision
        {
            let submitted_configuration = frame.game_configuration_revision;
            let result = driver.request(Command::Detection(
                submitted_configuration,
                platform::enumerate_processes(),
            ));
            if let Some(SupervisorCommand::Refresh(reply)) = pending {
                let _ = reply.send(result.as_ref().map(|_| ()).map_err(Clone::clone));
            }
            if let Ok(updated) = result {
                frame = updated;
            }
            last_configuration = submitted_configuration;
            next_detection = Instant::now() + Duration::from_secs(1);
        }
        if settings_save.ready(frame.settings_revision, true, now) {
            settings_save.finish(
                frame.settings_revision,
                store.save("settings.json", &frame.snapshot.settings),
                now,
                Duration::ZERO,
            );
        }
        if learning_save.ready(
            frame.learning_revision,
            frame.urgent_learning_revision > learning_save.saved_revision,
            now,
        ) {
            learning_save.finish(
                frame.learning_revision,
                store.save("learning-state.json", &frame.learning),
                now,
                Duration::from_secs(5),
            );
        }
        if let Ok(updated) = driver.request(Command::Saved {
            settings: settings_save.saved_revision,
            learning: learning_save.saved_revision,
            settings_error: settings_save.error.clone(),
            learning_error: learning_save.error.clone(),
        }) {
            frame = updated;
        }
        let snapshot = &frame.snapshot;
        let important = format!(
            "{:?}{:?}{:?}{:?}{}{}",
            snapshot.game,
            snapshot.hook_error,
            snapshot.hotkey_error,
            snapshot.persistence,
            snapshot.settings.enabled,
            snapshot.effective_enabled
        );
        let changed = important != last_important;
        if changed && let Some(tray) = app.tray_by_id("main-tray") {
            let status = if snapshot.hook_error.is_some() {
                "保护故障"
            } else if snapshot.hotkey_error.is_some() && snapshot.registered_hotkey.is_none() {
                "热键不可用"
            } else if !snapshot.settings.enabled {
                "已暂停"
            } else if snapshot.startup_remaining_ms > 0 {
                "启动延迟"
            } else if snapshot.game.active {
                "游戏模式"
            } else {
                "普通防抖"
            };
            let error = snapshot
                .persistence
                .settings_error
                .as_ref()
                .or(snapshot.persistence.learning_error.as_ref())
                .or(snapshot.game.error.as_ref());
            let _ = tray.set_tooltip(Some(format!(
                "Keyboard Debounce · {status}{}",
                if error.is_some() {
                    " · 存在错误，请打开设置"
                } else {
                    ""
                }
            )));
        }
        let has_window = app.get_webview_window("main").is_some();
        if has_window
            && (!had_window
                || changed
                || (snapshot.revision != last_revision
                    && now.duration_since(last_emit) >= Duration::from_millis(250)))
        {
            if let Err(error) = app.emit_to("main", "state-changed", snapshot) {
                eprintln!("状态推送失败：{error}");
            }
            last_emit = now;
            last_revision = snapshot.revision;
        }
        last_important = important;
        had_window = has_window;
    }
}
pub fn run() {
    let _instance = match platform::acquire_instance() {
        Ok(instance) => instance,
        Err(e) => {
            platform::message(&e, false);
            return;
        }
    };
    if let Err(error) = tauri::webview_version() {
        platform::message(
            &format!(
                "缺少 Microsoft Edge WebView2 Runtime。请使用安装包补齐运行时后重试。\n{error}"
            ),
            true,
        );
        return;
    }
    let directory = match std::env::var_os("APPDATA") {
        Some(path) => std::path::PathBuf::from(path).join("KeyboardDebounceTauri"),
        None => {
            platform::message("无法确定 APPDATA 配置目录", true);
            return;
        }
    };
    let store = match Store::new(directory.clone()) {
        Ok(store) => store,
        Err(e) => {
            platform::message(&e, true);
            return;
        }
    };
    let loaded = store.load();
    let silent = loaded.settings.silent_run;
    let runtime = Runtime::new(
        loaded.settings,
        loaded.learning,
        directory.display().to_string(),
        loaded.errors,
    );
    let driver = match Driver::start(runtime) {
        Ok(driver) => driver,
        Err(e) => {
            platform::message(&e, true);
            return;
        }
    };
    let (sender, receiver) = mpsc::channel();
    let setup_driver = driver.clone();
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .manage(AppState {
            driver,
            supervisor: sender,
            exiting: AtomicBool::new(false),
        })
        .invoke_handler(tauri::generate_handler![
            get_snapshot,
            update_settings,
            set_enabled,
            apply_hotkey,
            set_startup,
            change_games,
            set_key_rule,
            clear_ignored,
            reset_learning,
            refresh_admin,
            list_processes,
            browse_executables,
            open_config_directory,
            refresh_games
        ])
        .setup(move |app| {
            let show = MenuItem::with_id(app, "show", "显示主界面", true, None::<&str>)?;
            let toggle = MenuItem::with_id(app, "toggle", "启用 / 暂停", true, None::<&str>)?;
            let exit = MenuItem::with_id(app, "exit", "退出", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show, &toggle, &exit])?;
            let mut tray = TrayIconBuilder::with_id("main-tray")
                .menu(&menu)
                .show_menu_on_left_click(false)
                .tooltip("Keyboard Debounce")
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "show" => {
                        if let Err(e) = show_window(app) {
                            platform::message(&e, true);
                        }
                    }
                    "toggle" => {
                        let driver = app.state::<AppState>().driver.clone();
                        std::thread::spawn(move || {
                            if let Err(e) = driver.request(Command::Toggle) {
                                platform::message(&e, true);
                            }
                        });
                    }
                    "exit" => request_exit(app.clone()),
                    _ => {}
                })
                .on_tray_icon_event(|tray, event| {
                    if matches!(
                        event,
                        TrayIconEvent::Click {
                            button: MouseButton::Left,
                            button_state: MouseButtonState::Up,
                            ..
                        }
                    ) && let Err(e) = show_window(tray.app_handle())
                    {
                        platform::message(&e, true);
                    }
                });
            if let Some(icon) = app.default_window_icon() {
                tray = tray.icon(icon.clone());
            }
            tray.build(app)?;
            let handle = app.handle().clone();
            std::thread::Builder::new()
                .name("background-coordinator".into())
                .spawn(move || supervise(handle, setup_driver, store, receiver))?;
            if !silent {
                show_window(app.handle()).map_err(std::io::Error::other)?;
            }
            Ok(())
        })
        .build(tauri::generate_context!());
    match app {
        Ok(app) => app.run(|app, event| {
            if let tauri::RunEvent::ExitRequested { api, .. } = event
                && !app.state::<AppState>().exiting.load(Ordering::SeqCst)
            {
                api.prevent_exit();
            }
        }),
        Err(error) => platform::message(&format!("应用启动失败：{error}"), true),
    }
}
