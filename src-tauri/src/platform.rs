//! Windows-only adapters. All unsafe calls and handle lifetimes are contained here.
use crate::{
    engine::{Input, PhysicalKey},
    model::*,
    runtime::{Detection, Runtime},
};
use std::{cell::RefCell, path::Path, sync::mpsc, thread};
use windows::{
    Win32::{
        Foundation::*,
        Security::Authorization::ConvertSidToStringSidW,
        Security::*,
        Storage::FileSystem::*,
        System::{
            Diagnostics::ToolHelp::*, LibraryLoader::GetModuleHandleW, Registry::*, Threading::*,
        },
        UI::{Input::KeyboardAndMouse::*, Shell::*, WindowsAndMessaging::*},
    },
    core::{PCWSTR, PWSTR, w},
};

fn wide(value: impl AsRef<std::ffi::OsStr>) -> Vec<u16> {
    use std::os::windows::ffi::OsStrExt;
    value.as_ref().encode_wide().chain(Some(0)).collect()
}
pub fn message(text: &str, error: bool) {
    let text = wide(text);
    // SAFETY: NUL-terminated buffers live through the synchronous call.
    unsafe {
        MessageBoxW(
            None,
            PCWSTR(text.as_ptr()),
            w!("Keyboard Debounce"),
            MB_OK
                | if error {
                    MB_ICONERROR
                } else {
                    MB_ICONINFORMATION
                },
        );
    }
}
pub struct OwnedHandle(HANDLE);
impl Drop for OwnedHandle {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}
pub fn acquire_instance() -> Result<OwnedHandle, String> {
    unsafe {
        let mut token = HANDLE::default();
        OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token)
            .map_err(|e| e.to_string())?;
        let token = OwnedHandle(token);
        let mut needed = 0;
        let _ = GetTokenInformation(token.0, TokenUser, None, 0, &mut needed);
        let mut storage = vec![0usize; (needed as usize).div_ceil(std::mem::size_of::<usize>())];
        GetTokenInformation(
            token.0,
            TokenUser,
            Some(storage.as_mut_ptr().cast()),
            needed,
            &mut needed,
        )
        .map_err(|e| e.to_string())?;
        let user = &*storage.as_ptr().cast::<TOKEN_USER>();
        let mut sid = PWSTR::null();
        ConvertSidToStringSidW(user.User.Sid, &mut sid).map_err(|e| e.to_string())?;
        let identity = sid.to_string().map_err(|e| e.to_string());
        LocalFree(Some(HLOCAL(sid.0.cast())));
        let name = wide(format!("Local\\KeyboardDebounce-{}", identity?));
        let handle = CreateMutexW(None, false, PCWSTR(name.as_ptr())).map_err(|e| e.to_string())?;
        let already_exists = GetLastError() == ERROR_ALREADY_EXISTS;
        let owned = OwnedHandle(handle);
        if already_exists {
            Err(
                "Keyboard Debounce 已经在当前用户会话中运行（可能是旧版）。请先退出已有实例。"
                    .into(),
            )
        } else {
            Ok(owned)
        }
    }
}
pub fn atomic_replace(temp: &Path, target: &Path, backup: Option<&Path>) -> Result<(), String> {
    let source_w = wide(temp);
    let target_w = wide(target);
    let backup_w = backup.map(wide);
    let result = unsafe {
        if target.exists() && backup_w.is_some() {
            ReplaceFileW(
                PCWSTR(target_w.as_ptr()),
                PCWSTR(source_w.as_ptr()),
                backup_w
                    .as_ref()
                    .map_or(PCWSTR::null(), |b| PCWSTR(b.as_ptr())),
                REPLACE_FILE_FLAGS(0),
                None,
                None,
            )
        } else {
            MoveFileExW(
                PCWSTR(source_w.as_ptr()),
                PCWSTR(target_w.as_ptr()),
                MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH,
            )
        }
    };
    result.map_err(|e| format!("原子替换 {} → {}：{e}", temp.display(), target.display()))
}

struct RegistryKey(HKEY);
impl Drop for RegistryKey {
    fn drop(&mut self) {
        unsafe {
            let _ = RegCloseKey(self.0);
        }
    }
}
fn open_run(write: bool) -> Result<RegistryKey, String> {
    let mut key = HKEY::default();
    let result = unsafe {
        if write {
            RegCreateKeyExW(
                HKEY_CURRENT_USER,
                w!("Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
                None,
                None,
                REG_OPTION_NON_VOLATILE,
                KEY_QUERY_VALUE | KEY_SET_VALUE,
                None,
                &mut key,
                None,
            )
        } else {
            RegOpenKeyExW(
                HKEY_CURRENT_USER,
                w!("Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
                None,
                KEY_QUERY_VALUE,
                &mut key,
            )
        }
    };
    result.ok().map_err(|e| format!("打开自启注册表项：{e}"))?;
    Ok(RegistryKey(key))
}
pub fn startup_enabled() -> Result<bool, String> {
    let key = open_run(false)?;
    let mut size = 0;
    let mut kind = REG_VALUE_TYPE::default();
    let status = unsafe {
        RegQueryValueExW(
            key.0,
            w!("KeyboardDebounce"),
            None,
            Some(&mut kind),
            None,
            Some(&mut size),
        )
    };
    if status == ERROR_FILE_NOT_FOUND {
        return Ok(false);
    }
    status.ok().map_err(|e| e.to_string())?;
    if kind != REG_SZ {
        return Ok(false);
    }
    let mut bytes = vec![0u8; size as usize];
    unsafe {
        RegQueryValueExW(
            key.0,
            w!("KeyboardDebounce"),
            None,
            None,
            Some(bytes.as_mut_ptr()),
            Some(&mut size),
        )
        .ok()
        .map_err(|e| e.to_string())?;
    }
    let units: Vec<u16> = bytes
        .as_chunks::<2>()
        .0
        .iter()
        .map(|b| u16::from_le_bytes([b[0], b[1]]))
        .take_while(|u| *u != 0)
        .collect();
    let actual = String::from_utf16(&units).map_err(|e| e.to_string())?;
    let exe = std::env::current_exe().map_err(|e| e.to_string())?;
    Ok(actual
        .trim()
        .eq_ignore_ascii_case(&format!("\"{}\"", exe.display())))
}
pub fn set_startup(enabled: bool) -> Result<(), String> {
    let key = open_run(true)?;
    let result = unsafe {
        if enabled {
            let exe = std::env::current_exe().map_err(|e| e.to_string())?;
            let data: Vec<u8> = wide(format!("\"{}\"", exe.display()))
                .iter()
                .flat_map(|v| v.to_le_bytes())
                .collect();
            RegSetValueExW(key.0, w!("KeyboardDebounce"), None, REG_SZ, Some(&data))
        } else {
            RegDeleteValueW(key.0, w!("KeyboardDebounce"))
        }
    };
    if !enabled && result == ERROR_FILE_NOT_FOUND {
        return Ok(());
    }
    result.ok().map_err(|e| format!("写入自启注册表项：{e}"))
}
pub fn open_directory(path: &Path) -> Result<(), String> {
    let path = wide(path);
    let result = unsafe {
        ShellExecuteW(
            None,
            w!("open"),
            PCWSTR(path.as_ptr()),
            None,
            None,
            SW_SHOWNORMAL,
        )
    };
    if result.0 as isize <= 32 {
        Err(format!(
            "打开配置目录失败：Windows 错误 {}",
            result.0 as isize
        ))
    } else {
        Ok(())
    }
}
pub fn is_admin() -> bool {
    unsafe { IsUserAnAdmin().as_bool() }
}
pub fn enumerate_processes() -> Detection {
    let result = (|| -> Result<Detection, String> {
        let snapshot = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) }
            .map_err(|e| e.to_string())?;
        let snapshot = OwnedHandle(snapshot);
        let mut entry = PROCESSENTRY32W {
            dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
            ..Default::default()
        };
        let mut names = vec![];
        let first = unsafe { Process32FirstW(snapshot.0, &mut entry) };
        if let Err(e) = first {
            return Err(format!("Process32FirstW：{e}"));
        }
        loop {
            let len = entry
                .szExeFile
                .iter()
                .position(|u| *u == 0)
                .unwrap_or(entry.szExeFile.len());
            let filename = String::from_utf16_lossy(&entry.szExeFile[..len]);
            if entry.th32ProcessID != unsafe { GetCurrentProcessId() }
                && filename.to_ascii_lowercase().ends_with(".exe")
                && let Ok(name) = normalize_exe(&filename)
            {
                names.push(name);
            }
            if let Err(e) = unsafe { Process32NextW(snapshot.0, &mut entry) } {
                let complete = unsafe { GetLastError() } == ERROR_NO_MORE_FILES;
                names.sort();
                names.dedup();
                return Ok(Detection {
                    names,
                    complete,
                    error: (!complete).then(|| format!("进程枚举未完成：{e}")),
                });
            }
        }
    })();
    result.unwrap_or_else(|e| Detection {
        names: vec![],
        complete: false,
        error: Some(format!("进程检测失败：{e}")),
    })
}
pub fn key_name(vk: u16) -> String {
    let scan = unsafe { MapVirtualKeyW(u32::from(vk), MAPVK_VK_TO_VSC) };
    let extended = matches!(vk, 0x21..=0x2E | 0x6F | 0x90 | 0xA3 | 0xA5);
    let flags = ((scan << 16) | if extended { 1 << 24 } else { 0 }) as i32;
    let mut name = [0u16; 128];
    let length = unsafe { GetKeyNameTextW(flags, &mut name) };
    if length > 0 {
        String::from_utf16_lossy(&name[..length as usize])
    } else {
        format!("VK {vk}")
    }
}

#[derive(Clone, Debug, PartialEq)]
struct Binding {
    modifiers: HOT_KEY_MODIFIERS,
    vk: u32,
    text: String,
}
fn parse_hotkey(text: &str) -> Result<Binding, String> {
    let tokens: Vec<_> = text.split('+').map(str::trim).collect();
    let mut modifiers = MOD_NOREPEAT;
    let mut labels = vec![];
    let mut vk = None;
    for token in tokens {
        let lower = token.to_ascii_lowercase();
        let modifier = match lower.as_str() {
            "ctrl" | "control" => Some((MOD_CONTROL, "Ctrl")),
            "alt" => Some((MOD_ALT, "Alt")),
            "shift" => Some((MOD_SHIFT, "Shift")),
            "win" | "windows" => Some((MOD_WIN, "Win")),
            _ => None,
        };
        if let Some((flag, name)) = modifier {
            if modifiers.contains(flag) {
                return Err("热键包含重复修饰键".into());
            }
            modifiers |= flag;
            labels.push(name.to_string());
        } else {
            if vk.is_some() {
                return Err("热键只能包含一个主键".into());
            }
            let key =
                if let Some(number) = lower.strip_prefix('f').and_then(|v| v.parse::<u32>().ok()) {
                    if !(1..=24).contains(&number) {
                        return Err("功能键应为 F1–F24".into());
                    }
                    0x70 + number - 1
                } else if lower.len() == 1 && lower.as_bytes()[0].is_ascii_alphanumeric() {
                    u32::from(lower.to_ascii_uppercase().as_bytes()[0])
                } else {
                    match lower.as_str() {
                    "back" | "backspace" => 0x08,
                    "tab" => 0x09,
                    "clear" => 0x0C,
                    "return" | "enter" => 0x0D,
                    "escape" | "esc" => 0x1B,
                    "space" => 0x20,
                    "pause" => 0x13,
                    "capslock" | "capital" => 0x14,
                    "pageup" | "prior" => 0x21,
                    "pagedown" | "next" => 0x22,
                    "left" => 0x25,
                    "up" => 0x26,
                    "right" => 0x27,
                    "down" => 0x28,
                    "printscreen" | "snapshot" => 0x2C,
                    "insert" => 0x2D,
                    "home" => 0x24,
                    "end" => 0x23,
                    "delete" => 0x2E,
                    "numpad0" => 0x60,
                    "numpad1" => 0x61,
                    "numpad2" => 0x62,
                    "numpad3" => 0x63,
                    "numpad4" => 0x64,
                    "numpad5" => 0x65,
                    "numpad6" => 0x66,
                    "numpad7" => 0x67,
                    "numpad8" => 0x68,
                    "numpad9" => 0x69,
                    "multiply" => 0x6A,
                    "add" => 0x6B,
                    "separator" => 0x6C,
                    "subtract" => 0x6D,
                    "decimal" => 0x6E,
                    "divide" => 0x6F,
                    "numlock" => 0x90,
                    "scroll" | "scrolllock" => 0x91,
                    "oemsemicolon" | "oem1" => 0xBA,
                    "oemplus" => 0xBB,
                    "oemcomma" => 0xBC,
                    "oemminus" => 0xBD,
                    "oemperiod" => 0xBE,
                    "oemquestion" | "oem2" => 0xBF,
                    "oemtilde" | "oem3" => 0xC0,
                    "oemopenbrackets" | "oem4" => 0xDB,
                    "oempipe" | "oem5" => 0xDC,
                    "oemclosebrackets" | "oem6" => 0xDD,
                    "oemquotes" | "oem7" => 0xDE,
                    "oembackslash" | "oem102" => 0xE2,
                    _ if lower.len() == 2
                        && lower.starts_with('d')
                        && lower.as_bytes()[1].is_ascii_digit() => u32::from(lower.as_bytes()[1]),
                    _ => return Err(
                        "热键主键支持字母、数字、F1–F24、Space、Pause、Insert、Home、End、Delete"
                            .into(),
                    ),
                }
                };
            if key == 0x7B {
                return Err("F12 被 Windows 调试器保留，请使用其他键".into());
            }
            vk = Some(key);
            labels.push(token.to_ascii_uppercase());
        }
    }
    if modifiers == MOD_NOREPEAT || vk.is_none() {
        return Err("热键至少需要一个修饰键和一个主键".into());
    }
    Ok(Binding {
        modifiers,
        vk: vk.unwrap_or_default(),
        text: labels.join("+"),
    })
}
struct Hotkey {
    active: Option<(i32, Binding)>,
}
impl Hotkey {
    fn rebind(&mut self, text: &str) -> Result<String, String> {
        let next = parse_hotkey(text)?;
        if let Some((_, old)) = &self.active
            && old.modifiers == next.modifiers
            && old.vk == next.vk
        {
            return Ok(old.text.clone());
        }
        let next_id = if self.active.as_ref().is_some_and(|(id, _)| *id == 1) {
            2
        } else {
            1
        };
        unsafe { RegisterHotKey(None, next_id, next.modifiers, next.vk) }
            .map_err(|e| format!("注册热键 {} 失败：{e}", next.text))?;
        if let Some((id, _)) = &self.active
            && let Err(error) = unsafe { UnregisterHotKey(None, *id) }
        {
            let rollback = unsafe { UnregisterHotKey(None, next_id) };
            return Err(format!("注销旧热键失败：{error}；撤销新绑定：{rollback:?}"));
        }
        let text = next.text.clone();
        self.active = Some((next_id, next));
        Ok(text)
    }
}
impl Drop for Hotkey {
    fn drop(&mut self) {
        if let Some((id, _)) = &self.active {
            unsafe {
                let _ = UnregisterHotKey(None, *id);
            }
        }
    }
}

pub enum Command {
    Read,
    Patch(SettingsPatch),
    Enable(bool),
    Toggle,
    Hotkey(String),
    Startup(bool),
    Games {
        names: Vec<String>,
        add: bool,
    },
    Rule {
        vk: u16,
        ignored: Option<bool>,
        game: Option<bool>,
    },
    ClearIgnored,
    ResetLearning,
    Detection(u64, Detection),
    Saved {
        settings: u64,
        learning: u64,
        settings_error: Option<String>,
        learning_error: Option<String>,
    },
    Admin,
    Shutdown,
}
#[derive(Clone)]
pub struct Frame {
    pub snapshot: Snapshot,
    pub learning: LearningFile,
    pub settings_revision: u64,
    pub learning_revision: u64,
    pub urgent_learning_revision: u64,
    pub game_configuration_revision: u64,
}
type Request = (Command, mpsc::Sender<Result<Frame, String>>);
#[derive(Clone)]
pub struct Driver {
    sender: mpsc::Sender<Request>,
}
impl Driver {
    pub fn request(&self, command: Command) -> Result<Frame, String> {
        let (sender, receiver) = mpsc::channel();
        self.sender
            .send((command, sender))
            .map_err(|_| "键盘核心线程已退出".to_string())?;
        receiver
            .recv()
            .map_err(|_| "键盘核心未完成操作便已退出".to_string())?
    }
    pub fn start(runtime: Runtime) -> Result<Self, String> {
        let (sender, receiver) = mpsc::channel::<Request>();
        let (ready_tx, ready_rx) = mpsc::channel();
        thread::Builder::new()
            .name("keyboard-hook".into())
            .spawn(move || run_driver(runtime, receiver, ready_tx))
            .map_err(|e| e.to_string())?;
        ready_rx.recv().map_err(|e| e.to_string())??;
        Ok(Self { sender })
    }
}
struct HookContext {
    runtime: Runtime,
    hook: HHOOK,
    hotkey: Hotkey,
    names: Vec<String>,
}
thread_local! {
    static HOOK: RefCell<Option<HookContext>> = const { RefCell::new(None) };
    static CALLBACK_ERROR: RefCell<Option<String>> = const { RefCell::new(None) };
}
unsafe extern "system" fn keyboard_callback(code: i32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    if code < 0 || CALLBACK_ERROR.with(|cell| cell.borrow().is_some()) {
        return unsafe { CallNextHookEx(None, code, wp, lp) };
    }
    let message = wp.0 as u32;
    let down = matches!(message, WM_KEYDOWN | WM_SYSKEYDOWN);
    let up = matches!(message, WM_KEYUP | WM_SYSKEYUP);
    if !down && !up {
        return unsafe { CallNextHookEx(None, code, wp, lp) };
    }
    let outcome = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        HOOK.with(|cell| {
            let mut guard = cell.borrow_mut();
            let Some(context) = guard.as_mut() else {
                return false;
            };
            // SAFETY: Windows supplies KBDLLHOOKSTRUCT for the duration of this callback.
            let info = unsafe { &*(lp.0 as *const KBDLLHOOKSTRUCT) };
            context.runtime.process(Input {
                key: PhysicalKey {
                    vk: info.vkCode as u16,
                    scan: info.scanCode,
                    extended: info.flags.contains(LLKHF_EXTENDED),
                },
                down,
                injected: info.flags.contains(LLKHF_INJECTED)
                    || info.flags.contains(LLKHF_LOWER_IL_INJECTED),
                time: info.time,
            })
        })
    }));
    match outcome {
        Ok(true) => LRESULT(1),
        Ok(false) => unsafe { CallNextHookEx(None, code, wp, lp) },
        Err(error) => {
            let detail = error
                .downcast_ref::<String>()
                .cloned()
                .or_else(|| error.downcast_ref::<&str>().map(|s| s.to_string()))
                .unwrap_or_else(|| "未知 panic".into());
            CALLBACK_ERROR.with(|cell| {
                *cell.borrow_mut() = Some(format!("键盘回调异常，防抖已停用：{detail}"))
            });
            unsafe { CallNextHookEx(None, code, wp, lp) }
        }
    }
}
fn run_driver(
    mut runtime: Runtime,
    receiver: mpsc::Receiver<Request>,
    ready: mpsc::Sender<Result<(), String>>,
) {
    let hook = unsafe {
        GetModuleHandleW(None).and_then(|module| {
            SetWindowsHookExW(
                WH_KEYBOARD_LL,
                Some(keyboard_callback),
                Some(HINSTANCE(module.0)),
                0,
            )
        })
    };
    let hook = match hook {
        Ok(hook) => hook,
        Err(e) => {
            let _ = ready.send(Err(format!("安装键盘钩子失败：{e}")));
            return;
        }
    };
    runtime.is_admin = is_admin();
    match startup_enabled() {
        Ok(enabled) => runtime.engine.settings.start_with_windows = enabled,
        Err(e) => runtime.persistence.load_errors.push(e),
    }
    let mut hotkey = Hotkey { active: None };
    match hotkey.rebind(&runtime.engine.settings.pause_hotkey) {
        Ok(text) => runtime.registered_hotkey = Some(text),
        Err(e) => runtime.hotkey_error = Some(e),
    }
    let names = (0..=255).map(key_name).collect();
    HOOK.with(|cell| {
        *cell.borrow_mut() = Some(HookContext {
            runtime,
            hook,
            hotkey,
            names,
        })
    });
    let timer = unsafe { SetTimer(None, 0, 25, None) };
    if timer == 0 {
        unsafe {
            let _ = UnhookWindowsHookEx(hook);
        }
        HOOK.with(|cell| *cell.borrow_mut() = None);
        let _ = ready.send(Err("无法创建键盘线程定时器".into()));
        return;
    }
    let _ = ready.send(Ok(()));
    let mut message = MSG::default();
    loop {
        let status = unsafe { GetMessageW(&mut message, None, 0, 0) }.0;
        if status <= 0 {
            break;
        }
        if message.message == WM_HOTKEY {
            HOOK.with(|cell| {
                if let Some(context) = cell.borrow_mut().as_mut()
                    && context
                        .hotkey
                        .active
                        .as_ref()
                        .is_some_and(|(id, _)| *id as usize == message.wParam.0)
                {
                    let _ = execute(context, Command::Toggle);
                }
            });
        }
        if message.message == WM_TIMER {
            HOOK.with(|cell| {
                if let Some(c) = cell.borrow_mut().as_mut() {
                    c.runtime.now_utc = utc_now();
                    CALLBACK_ERROR.with(|error| {
                        if let Some(error) = error.borrow_mut().take() {
                            c.runtime.hook_error = Some(error);
                            c.runtime.revision += 1;
                        }
                    });
                    c.runtime.observe_effective_state();
                }
            });
            // Bound command work so a flooded UI cannot starve the Windows message queue.
            for _ in 0..16 {
                let Ok((command, reply)) = receiver.try_recv() else {
                    break;
                };
                let shutdown = matches!(command, Command::Shutdown);
                let result = HOOK.with(|cell| {
                    let mut context = cell.borrow_mut();
                    let c = context.as_mut().ok_or("键盘核心不可用")?;
                    execute(c, command)?;
                    Ok(frame(c))
                });
                let _ = reply.send(result);
                if shutdown {
                    unsafe {
                        PostQuitMessage(0);
                    }
                    break;
                }
            }
        }
        unsafe {
            let _ = TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    unsafe {
        let _ = KillTimer(None, timer);
    }
    HOOK.with(|cell| {
        if let Some(c) = cell.borrow_mut().take() {
            unsafe {
                if let Err(e) = UnhookWindowsHookEx(c.hook) {
                    self::message(&format!("卸载键盘钩子失败：{e}"), true);
                }
            }
        }
    });
}
fn frame(c: &HookContext) -> Frame {
    let r = &c.runtime;
    Frame {
        snapshot: r.snapshot(|vk| c.names[vk as usize].clone()),
        learning: r.engine.learning.clone(),
        settings_revision: r.settings_revision,
        learning_revision: r.learning_revision,
        urgent_learning_revision: r.urgent_learning_revision,
        game_configuration_revision: r.game_configuration_revision,
    }
}
fn execute(c: &mut HookContext, command: Command) -> Result<(), String> {
    let r = &mut c.runtime;
    match command {
        Command::Read => {}
        Command::Patch(patch) => {
            let next = patch.apply(&r.engine.settings)?;
            r.apply_settings(next);
        }
        Command::Enable(_) | Command::Toggle => {
            let enabled = if let Command::Enable(value) = command {
                value
            } else {
                !r.engine.settings.enabled
            };
            if enabled {
                if let Some(error) = &r.hook_error {
                    return Err(error.clone());
                }
                match c.hotkey.rebind(&r.engine.settings.pause_hotkey) {
                    Ok(text) => {
                        r.registered_hotkey = Some(text);
                        r.hotkey_error = None;
                    }
                    Err(e) => {
                        r.hotkey_error = Some(e.clone());
                        r.revision += 1;
                        return Err(e);
                    }
                }
            }
            let mut next = r.engine.settings.clone();
            next.enabled = enabled;
            r.apply_settings(next);
        }
        Command::Hotkey(text) => match c.hotkey.rebind(&text) {
            Ok(text) => {
                r.registered_hotkey = Some(text.clone());
                r.hotkey_error = None;
                let mut next = r.engine.settings.clone();
                next.pause_hotkey = text;
                r.apply_settings(next);
            }
            Err(e) => {
                r.hotkey_error = Some(e.clone());
                r.revision += 1;
                return Err(e);
            }
        },
        Command::Startup(enabled) => {
            r.change_startup(enabled, set_startup)?;
        }
        Command::Games { names, add } => {
            let names: Result<Vec<_>, _> = names.iter().map(|name| normalize_exe(name)).collect();
            let names = names?;
            let mut next = r.engine.settings.clone();
            if add {
                let was_empty = next.game_processes.is_empty();
                next.game_processes.extend(names);
                next.game_processes.sort();
                next.game_processes.dedup();
                if was_empty && !next.game_processes.is_empty() {
                    next.process_game_mode_enabled = true;
                }
            } else {
                next.game_processes.retain(|name| !names.contains(name));
            }
            r.apply_settings(next);
        }
        Command::Rule { vk, ignored, game } => r.key_rule(vk, ignored, game)?,
        Command::ClearIgnored => {
            let mut next = r.engine.settings.clone();
            next.ignored_keys.clear();
            r.apply_settings(next);
        }
        Command::ResetLearning => r.reset_learning(),
        Command::Detection(configuration, detection) => r.apply_detection(configuration, detection),
        Command::Saved {
            settings,
            learning,
            settings_error,
            learning_error,
        } => {
            let next = PersistenceStatus {
                settings_pending: settings < r.settings_revision,
                learning_pending: learning < r.learning_revision,
                settings_error,
                learning_error,
                load_errors: r.persistence.load_errors.clone(),
            };
            if next != r.persistence {
                r.persistence = next;
                r.revision += 1;
            }
        }
        Command::Admin => {
            r.is_admin = is_admin();
            r.revision += 1;
        }
        Command::Shutdown => {
            r.stopping = true;
            r.engine.boundary();
        }
    }
    r.observe_effective_state();
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn hotkey_parser_validates_reserved_and_duplicate_keys() {
        assert_eq!(parse_hotkey("Ctrl+Alt+F11").unwrap().vk, 0x7A);
        for value in ["F11", "Ctrl+F12", "Ctrl+Ctrl+A", "Ctrl+A+B", "Ctrl+"] {
            assert!(parse_hotkey(value).is_err(), "{value}");
        }
    }
    #[test]
    fn failed_native_hotkey_rebind_preserves_existing_registration() {
        let (ready_tx, ready_rx) = mpsc::channel();
        let (stop_tx, stop_rx) = mpsc::channel();
        let other = thread::spawn(move || {
            let mut occupied = Hotkey { active: None };
            let result = occupied.rebind("Ctrl+Alt+Shift+F24");
            ready_tx.send(result).unwrap();
            stop_rx.recv().unwrap();
        });
        ready_rx.recv().unwrap().unwrap();
        let mut hotkey = Hotkey { active: None };
        let first = hotkey.rebind("Ctrl+Alt+Shift+F23").unwrap();
        assert!(hotkey.rebind("Ctrl+Alt+Shift+F24").is_err());
        assert_eq!(hotkey.active.as_ref().unwrap().1.text, first);
        stop_tx.send(()).unwrap();
        other.join().unwrap();
        assert!(hotkey.rebind("Ctrl+Alt+Shift+F24").is_ok());
    }
    #[test]
    fn process_snapshot_is_sorted_unique_and_excludes_itself() {
        let snapshot = enumerate_processes();
        assert!(snapshot.complete, "{:?}", snapshot.error);
        assert!(snapshot.names.windows(2).all(|p| p[0] < p[1]));
        let own = std::env::current_exe().unwrap();
        let own_name = normalize_exe(&own.file_name().unwrap().to_string_lossy()).unwrap();
        assert!(!snapshot.names.contains(&own_name));
        assert!(!snapshot.names.contains(&"[system process]".to_owned()));
    }
}
