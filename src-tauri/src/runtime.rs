use crate::{
    engine::{Decision, Engine, Input},
    model::*,
};
use std::{
    collections::BTreeSet,
    time::{Duration, Instant},
};

#[derive(Clone, Debug)]
pub struct Detection {
    pub names: Vec<String>,
    pub complete: bool,
    pub error: Option<String>,
}

pub struct Runtime {
    pub engine: Engine,
    pub game: GameStatus,
    pub hook_error: Option<String>,
    pub hotkey_error: Option<String>,
    pub registered_hotkey: Option<String>,
    pub revision: u64,
    pub settings_revision: u64,
    pub learning_revision: u64,
    pub urgent_learning_revision: u64,
    pub game_configuration_revision: u64,
    pub persistence: PersistenceStatus,
    pub config_directory: String,
    pub is_admin: bool,
    pub startup_deadline: Instant,
    recent: Option<(u16, Decision)>,
    pub now_utc: String,
    pub stopping: bool,
    effective_state: bool,
}
impl Runtime {
    pub fn new(
        settings: Settings,
        learning: LearningFile,
        directory: String,
        load_errors: Vec<String>,
    ) -> Self {
        let startup_deadline =
            Instant::now() + Duration::from_millis(u64::from(settings.startup_delay_ms));
        Self {
            engine: Engine::new(settings, learning),
            game: GameStatus::default(),
            hook_error: None,
            hotkey_error: None,
            registered_hotkey: None,
            revision: 1,
            settings_revision: 1,
            learning_revision: 1,
            urgent_learning_revision: 0,
            game_configuration_revision: 1,
            persistence: PersistenceStatus {
                settings_pending: true,
                learning_pending: true,
                load_errors,
                ..PersistenceStatus::default()
            },
            config_directory: directory,
            is_admin: false,
            startup_deadline,
            recent: None,
            now_utc: utc_now(),
            stopping: false,
            effective_state: false,
        }
    }
    pub fn effective_enabled(&self) -> bool {
        !self.stopping
            && self.engine.settings.enabled
            && self.hook_error.is_none()
            && self.registered_hotkey.is_some()
            && Instant::now() >= self.startup_deadline
    }
    pub fn process(&mut self, input: Input) -> bool {
        self.observe_effective_state();
        let decision = self.engine.process(
            input,
            self.effective_enabled(),
            self.game.active,
            &self.now_utc,
        );
        if decision.learning_changed {
            self.learning_revision += 1;
            self.persistence.learning_pending = true;
            if decision.adjustment != 0 {
                self.urgent_learning_revision = self.learning_revision;
            }
        }
        let suppress = decision.suppress;
        if decision.learning_changed {
            self.recent = Some((input.key.vk, decision));
            self.revision += 1;
        }
        suppress
    }
    pub fn observe_effective_state(&mut self) {
        let effective = self.effective_enabled();
        if self.effective_state != effective {
            self.effective_state = effective;
            self.revision += 1;
        }
    }
    pub fn apply_settings(&mut self, next: Settings) {
        let game_changed = next.game_processes != self.engine.settings.game_processes
            || next.process_game_mode_enabled != self.engine.settings.process_game_mode_enabled;
        let ignored_changed = next.ignored_keys != self.engine.settings.ignored_keys;
        self.engine.set_settings(next);
        self.settings_revision += 1;
        self.persistence.settings_pending = true;
        self.revision += 1;
        if ignored_changed {
            self.learning_revision += 1;
            self.urgent_learning_revision = self.learning_revision;
            self.persistence.learning_pending = true;
        }
        if game_changed {
            self.game_configuration_revision += 1;
            let settings = &self.engine.settings;
            self.game
                .running
                .retain(|name| settings.game_processes.contains(name));
            self.game
                .unknown
                .retain(|name| settings.game_processes.contains(name));
            if !settings.process_game_mode_enabled
                || settings.game_processes.is_empty()
                || self.game.running.is_empty()
            {
                self.game.active = false;
            }
            if !settings.process_game_mode_enabled || settings.game_processes.is_empty() {
                self.game = GameStatus::default();
            }
        }
    }
    pub fn apply_detection(&mut self, configuration: u64, detection: Detection) {
        if configuration != self.game_configuration_revision {
            return;
        }
        let settings = &self.engine.settings;
        if !settings.process_game_mode_enabled || settings.game_processes.is_empty() {
            return;
        }
        let mut next = self.game.clone();
        let running: Vec<_> = settings
            .game_processes
            .iter()
            .filter(|name| detection.names.contains(name))
            .cloned()
            .collect();
        next.error = detection.error;
        if detection.complete {
            next.active = !running.is_empty();
            next.running = running;
            next.unknown.clear();
        } else {
            next.unknown = settings
                .game_processes
                .iter()
                .filter(|name| !running.contains(name))
                .cloned()
                .collect();
            if !running.is_empty() {
                next.active = true;
                next.running = running;
            }
        }
        if next != self.game {
            self.game = next;
            self.revision += 1;
        }
    }
    pub fn key_rule(
        &mut self,
        vk: u16,
        ignored: Option<bool>,
        game: Option<bool>,
    ) -> Result<(), String> {
        if !(1..=255).contains(&vk) {
            return Err("VK 必须在 1–255 之间".into());
        }
        let mut next = self.engine.settings.clone();
        for (value, keys) in [
            (ignored, &mut next.ignored_keys),
            (game, &mut next.game_mode_filtered_keys),
        ] {
            if let Some(on) = value {
                keys.retain(|key| *key != vk);
                if on {
                    keys.push(vk);
                    keys.sort_unstable();
                }
            }
        }
        self.apply_settings(next);
        Ok(())
    }
    pub fn reset_learning(&mut self) {
        self.engine.reset_learning();
        self.recent = None;
        self.learning_revision += 1;
        self.urgent_learning_revision = self.learning_revision;
        self.persistence.learning_pending = true;
        self.revision += 1;
    }
    pub fn change_startup(
        &mut self,
        enabled: bool,
        write: impl FnOnce(bool) -> Result<(), String>,
    ) -> Result<(), String> {
        write(enabled)?;
        let mut next = self.engine.settings.clone();
        next.start_with_windows = enabled;
        self.apply_settings(next);
        Ok(())
    }
    pub fn snapshot(&self, key_name: impl Fn(u16) -> String) -> Snapshot {
        let settings = &self.engine.settings;
        let keys: BTreeSet<u16> = self
            .engine
            .learning
            .keys
            .keys()
            .copied()
            .chain(settings.ignored_keys.iter().copied())
            .chain(settings.game_mode_filtered_keys.iter().copied())
            .collect();
        Snapshot {
            revision: self.revision,
            settings: settings.clone(),
            effective_enabled: self.effective_state,
            startup_remaining_ms: self
                .startup_deadline
                .saturating_duration_since(Instant::now())
                .as_millis()
                .min(u128::from(u32::MAX)) as u32,
            hook_error: self.hook_error.clone(),
            hotkey_error: self.hotkey_error.clone(),
            registered_hotkey: self.registered_hotkey.clone(),
            game: self.game.clone(),
            keys: keys
                .into_iter()
                .map(|vk| {
                    let learned = self.engine.learning.keys.get(&vk);
                    let learning = learned.cloned().unwrap_or_else(|| KeyLearning {
                        threshold_ms: settings.default_threshold_ms,
                        ..KeyLearning::default()
                    });
                    KeyRow {
                        vk,
                        name: key_name(vk),
                        learned: learned.is_some(),
                        ignored: settings.ignored_keys.contains(&vk),
                        game_filtered: settings.game_mode_filtered_keys.contains(&vk),
                        effective_threshold_ms: settings
                            .threshold(learning.threshold_ms, self.game.active),
                        learning,
                    }
                })
                .collect(),
            recent_event: self.recent.as_ref().map(|(vk, d)| {
                format!(
                    "{} · {} · 间隔 {}ms · 阈值 {}ms{}",
                    key_name(*vk),
                    match d.reason {
                        "short-repeat" => "已拦截",
                        "paired-key-up" => "配对释放已拦截",
                        "long-hold" => "长按放行",
                        "key-up" => "已释放",
                        _ => "已放行",
                    },
                    d.interval,
                    d.threshold,
                    if d.adjustment == 0 {
                        String::new()
                    } else {
                        format!(
                            " · 学习 {:+}ms（{}）",
                            d.adjustment,
                            if d.adjustment > 0 {
                                "贴边拦截"
                            } else {
                                "稳定回落"
                            }
                        )
                    }
                )
            }),
            persistence: self.persistence.clone(),
            config_directory: self.config_directory.clone(),
            is_admin: self.is_admin,
            version: env!("CARGO_PKG_VERSION").into(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn runtime() -> Runtime {
        Runtime::new(
            Settings::default(),
            LearningFile::default(),
            "test".into(),
            vec![],
        )
    }
    #[test]
    fn stale_detection_is_rejected_and_failure_keeps_confirmed_mode() {
        let mut r = runtime();
        let mut s = r.engine.settings.clone();
        s.process_game_mode_enabled = true;
        s.game_processes = vec!["game".into()];
        r.apply_settings(s);
        r.apply_detection(
            1,
            Detection {
                names: vec!["game".into()],
                complete: true,
                error: None,
            },
        );
        assert!(!r.game.active);
        r.apply_detection(
            2,
            Detection {
                names: vec!["game".into()],
                complete: true,
                error: None,
            },
        );
        assert!(r.game.active);
        r.apply_detection(
            2,
            Detection {
                names: vec![],
                complete: false,
                error: Some("denied".into()),
            },
        );
        assert!(r.game.active);
        assert_eq!(r.game.unknown, vec!["game"]);
        r.apply_detection(
            2,
            Detection {
                names: vec![],
                complete: true,
                error: None,
            },
        );
        assert!(!r.game.active);
        assert!(r.game.error.is_none());
    }
    #[test]
    fn removing_active_game_immediately_exits_mode() {
        let mut r = runtime();
        let mut s = r.engine.settings.clone();
        s.process_game_mode_enabled = true;
        s.game_processes = vec!["game".into()];
        r.apply_settings(s);
        r.apply_detection(
            2,
            Detection {
                names: vec!["game".into()],
                complete: true,
                error: None,
            },
        );
        let mut s = r.engine.settings.clone();
        s.game_processes.clear();
        r.apply_settings(s);
        assert!(!r.game.active);
    }
    #[test]
    fn unregistered_hotkey_never_claims_enabled() {
        let r = runtime();
        assert!(!r.effective_enabled());
    }
    #[test]
    fn invalid_patch_does_not_change_state() {
        let r = runtime();
        let patch = SettingsPatch {
            default_threshold_ms: Some(0),
            ..SettingsPatch::default()
        };
        assert!(patch.apply(&r.engine.settings).is_err());
        assert_eq!(r.engine.settings.default_threshold_ms, 90);
    }
    #[test]
    fn effective_transitions_are_versioned_and_shutdown_keeps_saved_preference() {
        let mut r = runtime();
        r.registered_hotkey = Some("Ctrl+Alt+F11".into());
        r.startup_deadline = Instant::now() + Duration::from_secs(1);
        r.observe_effective_state();
        let waiting = r.snapshot(|vk| vk.to_string());
        assert!(!waiting.effective_enabled);
        r.startup_deadline = Instant::now();
        r.observe_effective_state();
        let active = r.snapshot(|vk| vk.to_string());
        assert!(active.effective_enabled);
        assert!(active.revision > waiting.revision);
        r.stopping = true;
        r.observe_effective_state();
        let stopped = r.snapshot(|vk| vk.to_string());
        assert!(!stopped.effective_enabled);
        assert!(stopped.settings.enabled);
        assert!(stopped.revision > active.revision);
    }
    #[test]
    fn startup_write_failure_preserves_state_and_original_error() {
        let mut r = runtime();
        let revision = r.settings_revision;
        let error = r.change_startup(true, |_| Err("RegSetValueExW: access denied (5)".into()));
        assert_eq!(error.unwrap_err(), "RegSetValueExW: access denied (5)");
        assert!(!r.engine.settings.start_with_windows);
        assert_eq!(r.settings_revision, revision);
        r.change_startup(true, |enabled| {
            assert!(enabled);
            Ok(())
        })
        .unwrap();
        assert!(r.engine.settings.start_with_windows);
        assert!(r.settings_revision > revision);
    }
}
