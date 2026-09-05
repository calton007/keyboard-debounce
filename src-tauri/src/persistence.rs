use crate::model::{LearningFile, Settings};
use serde::{Serialize, de::DeserializeOwned};
use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
    time::{Duration, Instant},
};

pub struct Store {
    pub directory: PathBuf,
}
pub struct Loaded {
    pub settings: Settings,
    pub learning: LearningFile,
    pub errors: Vec<String>,
}
impl Store {
    pub fn new(directory: PathBuf) -> Result<Self, String> {
        fs::create_dir_all(&directory)
            .map_err(|e| format!("创建配置目录 {}：{e}", directory.display()))?;
        Ok(Self { directory })
    }
    pub fn load(&self) -> Loaded {
        let mut errors = vec![];
        let settings = self
            .load_file("settings.json", Settings::validate, &mut errors)
            .unwrap_or_else(|| Settings {
                enabled: false,
                ..Settings::default()
            });
        let learning = self
            .load_file("learning-state.json", LearningFile::validate, &mut errors)
            .unwrap_or_default();
        Loaded {
            settings,
            learning,
            errors,
        }
    }
    fn load_file<T: DeserializeOwned + Default>(
        &self,
        name: &str,
        validate: fn(&T) -> Result<(), String>,
        errors: &mut Vec<String>,
    ) -> Option<T> {
        let path = self.directory.join(name);
        let backup = self.directory.join(format!("{name}.bak"));
        if !path.exists() && !backup.exists() {
            return Some(T::default());
        }
        match read_valid(&path, validate) {
            Ok(value) => Some(value),
            Err(error) => {
                errors.push(error);
                if path.exists() {
                    let quarantine = self.directory.join(format!(
                        "{name}.corrupt-{}",
                        chrono::Utc::now().timestamp_nanos_opt().unwrap_or_default()
                    ));
                    if let Err(e) = fs::copy(&path, &quarantine) {
                        errors.push(format!(
                            "隔离 {} 到 {} 失败：{e}",
                            path.display(),
                            quarantine.display()
                        ));
                        return None;
                    }
                }
                match read_valid(&backup, validate) {
                    Ok(value) => {
                        errors.push(format!(
                            "已从 {} 恢复；原文件将在保存时修复",
                            backup.display()
                        ));
                        Some(value)
                    }
                    Err(e) => {
                        errors.push(e);
                        None
                    }
                }
            }
        }
    }
    pub fn save<T: Serialize>(&self, name: &str, value: &T) -> Result<(), String> {
        let path = self.directory.join(name);
        let temp = self.directory.join(format!("{name}.tmp"));
        let backup = self.directory.join(format!("{name}.bak"));
        let data = serde_json::to_vec_pretty(value)
            .map_err(|e| format!("序列化 {}：{e}", path.display()))?;
        let result = (|| -> Result<(), String> {
            let mut file =
                fs::File::create(&temp).map_err(|e| format!("创建 {}：{e}", temp.display()))?;
            file.write_all(&data)
                .map_err(|e| format!("写入 {}：{e}", temp.display()))?;
            file.sync_all()
                .map_err(|e| format!("同步 {}：{e}", temp.display()))?;
            drop(file);
            // Never replace a valid backup with a corrupt primary.
            let valid_primary = match fs::read(&path) {
                Ok(bytes) => {
                    if name == "settings.json" {
                        serde_json::from_slice::<Settings>(&bytes)
                            .is_ok_and(|s| s.validate().is_ok())
                    } else {
                        serde_json::from_slice::<LearningFile>(&bytes)
                            .is_ok_and(|s| s.validate().is_ok())
                    }
                }
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => false,
                Err(error) => return Err(format!("保存前读取 {}：{error}", path.display())),
            };
            #[cfg(windows)]
            crate::platform::atomic_replace(
                &temp,
                &path,
                valid_primary.then_some(backup.as_path()),
            )?;
            #[cfg(not(windows))]
            {
                if valid_primary {
                    fs::copy(&path, &backup).map_err(|e| e.to_string())?;
                }
                fs::rename(&temp, &path).map_err(|e| e.to_string())?;
            }
            Ok(())
        })();
        // A failed temp remains available for diagnosis, and the next serialized attempt replaces it.
        result
    }
}
fn read_valid<T: DeserializeOwned>(
    path: &Path,
    validate: fn(&T) -> Result<(), String>,
) -> Result<T, String> {
    let bytes = fs::read(path).map_err(|e| format!("读取 {}：{e}", path.display()))?;
    let value =
        serde_json::from_slice(&bytes).map_err(|e| format!("解析 {}：{e}", path.display()))?;
    validate(&value).map_err(|e| format!("校验 {}：{e}", path.display()))?;
    Ok(value)
}

/// One writer per file; acknowledgements track the exact immutable revision written.
pub struct SaveSlot {
    pub saved_revision: u64,
    pub error: Option<String>,
    due: Instant,
    failures: u32,
}
impl Default for SaveSlot {
    fn default() -> Self {
        Self {
            saved_revision: 0,
            error: None,
            due: Instant::now(),
            failures: 0,
        }
    }
}
impl SaveSlot {
    pub fn ready(&self, revision: u64, urgent: bool, now: Instant) -> bool {
        revision > self.saved_revision && (now >= self.due || (urgent && self.failures == 0))
    }
    pub fn finish(
        &mut self,
        revision: u64,
        result: Result<(), String>,
        now: Instant,
        delay: Duration,
    ) {
        match result {
            Ok(()) => {
                self.saved_revision = revision;
                self.error = None;
                self.failures = 0;
                self.due = now + delay;
            }
            Err(e) => {
                self.error = Some(e);
                self.failures = (self.failures + 1).min(7);
                self.due = now + Duration::from_secs((1u64 << (self.failures - 1)).min(60));
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn clean_start_and_roundtrip() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path().into()).unwrap();
        assert!(store.load().settings.enabled);
        let s = Settings {
            silent_run: true,
            ..Settings::default()
        };
        store.save("settings.json", &s).unwrap();
        assert_eq!(store.load().settings, s);
    }
    #[test]
    fn corrupt_settings_disable_and_learning_damage_is_independent() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path().into()).unwrap();
        fs::write(dir.path().join("settings.json"), "broken").unwrap();
        let loaded = store.load();
        assert!(!loaded.settings.enabled);
        assert!(!loaded.errors.is_empty());
        assert!(fs::read_dir(dir.path()).unwrap().any(|p| {
            p.unwrap()
                .file_name()
                .to_string_lossy()
                .contains(".corrupt-")
        }));
    }
    #[test]
    fn backup_is_valid_previous_revision_and_corrupt_primary_does_not_destroy_it() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path().into()).unwrap();
        let first = Settings::default();
        store.save("settings.json", &first).unwrap();
        let second = Settings {
            silent_run: true,
            ..first.clone()
        };
        store.save("settings.json", &second).unwrap();
        fs::write(dir.path().join("settings.json"), "invalid").unwrap();
        assert_eq!(store.load().settings, first);
        store.save("settings.json", &second).unwrap();
        let backup: Settings =
            read_valid(&dir.path().join("settings.json.bak"), Settings::validate).unwrap();
        assert_eq!(backup, first);
    }
    #[test]
    fn invalid_versions_and_numbers_are_not_loaded() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path().into()).unwrap();
        fs::write(dir.path().join("settings.json"), r#"{"schemaVersion":99}"#).unwrap();
        assert!(!store.load().settings.enabled);
        fs::write(dir.path().join("settings.json"), "{}").unwrap();
        assert!(!store.load().settings.enabled);
    }
    #[cfg(windows)]
    #[test]
    fn locked_primary_keeps_last_saved_state_and_retry_writes_latest_revision() {
        use std::os::windows::fs::OpenOptionsExt;
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path().into()).unwrap();
        let first = Settings::default();
        store.save("settings.json", &first).unwrap();
        let lock = fs::OpenOptions::new()
            .read(true)
            .share_mode(1)
            .open(dir.path().join("settings.json"))
            .unwrap();
        let second = Settings {
            silent_run: true,
            ..first.clone()
        };
        let error = store.save("settings.json", &second).unwrap_err();
        assert!(error.contains("settings.json"), "{error}");
        assert_eq!(store.load().settings, first);
        drop(lock);
        let latest = Settings {
            default_threshold_ms: 100,
            ..second
        };
        store.save("settings.json", &latest).unwrap();
        assert_eq!(store.load().settings, latest);
        let backup: Settings =
            read_valid(&dir.path().join("settings.json.bak"), Settings::validate).unwrap();
        assert_eq!(backup, first);
    }
    #[cfg(windows)]
    #[test]
    fn unreadable_primary_is_not_treated_as_corrupt_or_replaced_without_backup() {
        use std::os::windows::fs::OpenOptionsExt;
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path().into()).unwrap();
        store.save("settings.json", &Settings::default()).unwrap();
        let lock = fs::OpenOptions::new()
            .read(true)
            .share_mode(0)
            .open(dir.path().join("settings.json"))
            .unwrap();
        let error = store
            .save(
                "settings.json",
                &Settings {
                    silent_run: true,
                    ..Settings::default()
                },
            )
            .unwrap_err();
        assert!(error.contains("保存前读取"), "{error}");
        drop(lock);
        assert!(!store.load().settings.silent_run);
    }
    #[test]
    fn failed_save_keeps_dirty_and_retries_with_backoff() {
        let now = Instant::now();
        let mut slot = SaveSlot::default();
        slot.finish(3, Err("denied".into()), now, Duration::ZERO);
        assert_eq!(slot.saved_revision, 0);
        assert!(!slot.ready(4, true, now));
        assert!(slot.ready(4, false, now + Duration::from_secs(1)));
        slot.finish(3, Ok(()), now, Duration::ZERO);
        assert!(slot.ready(4, false, now));
        assert!(slot.error.is_none());
    }
}
