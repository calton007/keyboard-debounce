use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use ts_rs::TS;

pub fn utc_now() -> String {
    chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis, true)
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize, TS)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
#[ts(export, export_to = "../../frontend/generated/")]
pub struct Settings {
    pub schema_version: u32,
    pub enabled: bool,
    pub start_with_windows: bool,
    pub silent_run: bool,
    pub global_sensitivity: f64,
    pub default_threshold_ms: u32,
    pub long_hold_bypass_ms: u32,
    pub startup_delay_ms: u32,
    pub pause_hotkey: String,
    pub ignored_keys: Vec<u16>,
    pub process_game_mode_enabled: bool,
    pub game_processes: Vec<String>,
    pub game_mode_threshold_ms: u32,
    pub game_mode_long_hold_bypass_ms: u32,
    pub game_mode_filtered_keys: Vec<u16>,
}
impl Default for Settings {
    fn default() -> Self {
        Self {
            schema_version: 1,
            enabled: true,
            start_with_windows: false,
            silent_run: false,
            global_sensitivity: 1.0,
            default_threshold_ms: 90,
            long_hold_bypass_ms: 500,
            startup_delay_ms: 3000,
            pause_hotkey: "Ctrl+Alt+F11".into(),
            ignored_keys: vec![],
            process_game_mode_enabled: false,
            game_processes: vec![],
            game_mode_threshold_ms: 45,
            game_mode_long_hold_bypass_ms: 250,
            game_mode_filtered_keys: vec![],
        }
    }
}
impl Settings {
    pub fn validate(&self) -> Result<(), String> {
        if self.schema_version != 1 {
            return Err("不支持的设置文件版本".into());
        }
        if !self.global_sensitivity.is_finite()
            || !(0.5..=3.0).contains(&self.global_sensitivity)
            || !(20..=250).contains(&self.default_threshold_ms)
            || !(250..=1000).contains(&self.long_hold_bypass_ms)
            || self.startup_delay_ms > 30000
            || !(20..=250).contains(&self.game_mode_threshold_ms)
            || !(50..=1000).contains(&self.game_mode_long_hold_bypass_ms)
            || self
                .ignored_keys
                .iter()
                .chain(&self.game_mode_filtered_keys)
                .any(|k| !(1..=255).contains(k))
        {
            return Err("设置数值超出允许范围".into());
        }
        for name in &self.game_processes {
            if normalize_exe(name).as_ref() != Ok(name) {
                return Err(format!("游戏进程名称格式无效：{name}"));
            }
        }
        Ok(())
    }
    pub fn threshold(&self, learned: u32, game: bool) -> u32 {
        let normal = (f64::from(learned) * self.global_sensitivity)
            .round_ties_even()
            .clamp(20.0, 250.0) as u32;
        if game {
            normal.min(self.game_mode_threshold_ms)
        } else {
            normal
        }
    }
}

#[derive(Clone, Debug, Default, Deserialize, Serialize, TS)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
#[ts(export, export_to = "../../frontend/generated/")]
pub struct SettingsPatch {
    pub silent_run: Option<bool>,
    pub global_sensitivity: Option<f64>,
    pub default_threshold_ms: Option<u32>,
    pub long_hold_bypass_ms: Option<u32>,
    pub startup_delay_ms: Option<u32>,
    pub process_game_mode_enabled: Option<bool>,
    pub game_mode_threshold_ms: Option<u32>,
    pub game_mode_long_hold_bypass_ms: Option<u32>,
}
impl SettingsPatch {
    pub fn apply(self, original: &Settings) -> Result<Settings, String> {
        let mut next = original.clone();
        macro_rules! patch { ($($field:ident),*) => { $(if let Some(value) = self.$field { next.$field = value; })* }; }
        patch!(
            silent_run,
            global_sensitivity,
            default_threshold_ms,
            long_hold_bypass_ms,
            startup_delay_ms,
            process_game_mode_enabled,
            game_mode_threshold_ms,
            game_mode_long_hold_bypass_ms
        );
        next.validate()?;
        Ok(next)
    }
}

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize, TS)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
#[ts(export, export_to = "../../frontend/generated/")]
pub struct KeyLearning {
    pub threshold_ms: u32,
    #[ts(type = "number")]
    pub accepted_count: u64,
    #[ts(type = "number")]
    pub suppressed_count: u64,
    pub last_interval_ms: u32,
    pub last_seen_utc: Option<String>,
    pub last_adjustment_ms: i32,
    pub last_adjustment_reason: String,
    pub last_adjusted_utc: Option<String>,
}
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct LearningFile {
    pub schema_version: u32,
    pub keys: BTreeMap<u16, KeyLearning>,
}
impl Default for LearningFile {
    fn default() -> Self {
        Self {
            schema_version: 1,
            keys: BTreeMap::new(),
        }
    }
}
impl LearningFile {
    pub fn validate(&self) -> Result<(), String> {
        if self.schema_version != 1
            || self
                .keys
                .iter()
                .any(|(vk, k)| !(1..=255).contains(vk) || !(20..=250).contains(&k.threshold_ms))
        {
            Err("学习数据版本或数值无效".into())
        } else {
            Ok(())
        }
    }
}

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize, TS)]
#[serde(rename_all = "camelCase")]
#[ts(export, export_to = "../../frontend/generated/")]
pub struct GameStatus {
    pub active: bool,
    pub running: Vec<String>,
    pub unknown: Vec<String>,
    pub error: Option<String>,
}

#[derive(Clone, Debug, Serialize, Deserialize, TS)]
#[serde(rename_all = "camelCase")]
#[ts(export, export_to = "../../frontend/generated/")]
pub struct KeyRow {
    pub vk: u16,
    pub name: String,
    pub learned: bool,
    pub ignored: bool,
    pub game_filtered: bool,
    pub effective_threshold_ms: u32,
    pub learning: KeyLearning,
}
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize, TS)]
#[serde(rename_all = "camelCase")]
#[ts(export, export_to = "../../frontend/generated/")]
pub struct PersistenceStatus {
    pub settings_pending: bool,
    pub learning_pending: bool,
    pub settings_error: Option<String>,
    pub learning_error: Option<String>,
    pub load_errors: Vec<String>,
}
#[derive(Clone, Debug, Serialize, Deserialize, TS)]
#[serde(rename_all = "camelCase")]
#[ts(export, export_to = "../../frontend/generated/")]
pub struct Snapshot {
    #[ts(type = "number")]
    pub revision: u64,
    pub settings: Settings,
    pub effective_enabled: bool,
    pub startup_remaining_ms: u32,
    pub hook_error: Option<String>,
    pub hotkey_error: Option<String>,
    pub registered_hotkey: Option<String>,
    pub game: GameStatus,
    pub keys: Vec<KeyRow>,
    pub recent_event: Option<String>,
    pub persistence: PersistenceStatus,
    pub config_directory: String,
    pub is_admin: bool,
    pub version: String,
}

pub fn normalize_exe(value: &str) -> Result<String, String> {
    let name = value
        .rsplit(['/', '\\'])
        .next()
        .unwrap_or("")
        .trim()
        .to_lowercase();
    let name = name.strip_suffix(".exe").unwrap_or(&name);
    if name.is_empty()
        || name.contains([':', '*', '?', '"', '<', '>', '|', '\0'])
        || name == "."
        || name == ".."
    {
        Err(format!("无效的 EXE 名称：{value}"))
    } else {
        Ok(name.to_owned())
    }
}
