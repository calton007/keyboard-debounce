//! Pure filtering state machine. No OS calls, locks, IPC, or persistence.
use crate::model::{KeyLearning, LearningFile, Settings};
use std::collections::HashMap;

#[derive(Clone, Copy, Debug, Hash, PartialEq, Eq)]
pub struct PhysicalKey {
    pub vk: u16,
    pub scan: u32,
    pub extended: bool,
}
#[derive(Clone, Copy, Debug)]
pub struct Input {
    pub key: PhysicalKey,
    pub down: bool,
    pub injected: bool,
    pub time: u32,
}
#[derive(Clone, Debug, Default)]
pub struct Decision {
    pub suppress: bool,
    pub reason: &'static str,
    pub threshold: u32,
    pub interval: u32,
    pub adjustment: i32,
    pub learning_changed: bool,
}
#[derive(Default)]
struct Timeline {
    game: bool,
    down: bool,
    first_down: Option<u32>,
    first_suppressed: Option<u32>,
    last_observed: Option<u32>,
    last_accepted: Option<u32>,
    released: bool,
    near_boundary: u8,
    stable: u8,
}
#[derive(Default)]
struct Delivered {
    down: bool,
    pending_up: bool,
    bypass_until_up: bool,
}

pub struct Engine {
    pub settings: Settings,
    pub learning: LearningFile,
    timelines: HashMap<PhysicalKey, Timeline>,
    delivered: HashMap<PhysicalKey, Delivered>,
    observed_mode: Option<(bool, bool)>,
}
impl Engine {
    pub fn new(settings: Settings, learning: LearningFile) -> Self {
        Self {
            settings,
            learning,
            timelines: HashMap::with_capacity(256),
            delivered: HashMap::with_capacity(256),
            observed_mode: None,
        }
    }
    pub fn boundary(&mut self) {
        self.delivered.retain(|_, state| {
            if state.down {
                state.bypass_until_up = true;
            }
            state.down || state.pending_up
        });
        self.timelines.retain(|key, timeline| {
            timeline.near_boundary = 0;
            timeline.stable = 0;
            self.delivered
                .get(key)
                .is_some_and(|state| !state.down && state.pending_up)
        });
    }
    pub fn set_settings(&mut self, next: Settings) {
        // Preserve downstream pairing; ignoring a key removes its ordinary learning.
        for vk in &next.ignored_keys {
            if !self.settings.ignored_keys.contains(vk) {
                self.learning.keys.remove(vk);
                for (key, timeline) in &mut self.timelines {
                    if key.vk == *vk {
                        timeline.down = false;
                        timeline.first_down = None;
                        timeline.first_suppressed = None;
                        timeline.last_accepted = None;
                        timeline.released = false;
                        timeline.near_boundary = 0;
                        timeline.stable = 0;
                    }
                }
            }
        }
        if next.enabled != self.settings.enabled {
            self.boundary();
        }
        self.settings = next;
    }
    pub fn reset_learning(&mut self) {
        self.learning.keys.clear();
        self.timelines.clear();
    }
    fn complete_up(&mut self, key: PhysicalKey) {
        if let Some(t) = self.timelines.get_mut(&key) {
            t.down = false;
            t.first_down = None;
            t.first_suppressed = None;
            t.released = true;
        }
    }
    pub fn process(&mut self, input: Input, enabled: bool, game: bool, now_utc: &str) -> Decision {
        if input.injected {
            return Decision {
                reason: "injected",
                ..Decision::default()
            };
        }
        if self.observed_mode.is_some_and(|old| old != (enabled, game)) {
            self.boundary();
        }
        self.observed_mode = Some((enabled, game));
        let vk = input.key.vk;
        if matches!(vk, 0x10..=0x12 | 0x5B..=0x5C | 0xA0..=0xA5) || !(1..=255).contains(&vk) {
            return Decision {
                reason: "modifier",
                ..Decision::default()
            };
        }
        let held_bypass = self
            .delivered
            .get(&input.key)
            .is_some_and(|s| s.bypass_until_up);
        let bypass = held_bypass
            || !enabled
            || self.settings.ignored_keys.contains(&vk)
            || (game && !self.settings.game_mode_filtered_keys.contains(&vk));
        if bypass {
            let pending = self
                .delivered
                .get(&input.key)
                .is_some_and(|s| !s.down && s.pending_up);
            if input.down {
                if pending {
                    self.complete_up(input.key);
                }
                self.delivered.insert(
                    input.key,
                    Delivered {
                        down: true,
                        pending_up: false,
                        bypass_until_up: true,
                    },
                );
                return Decision {
                    reason: "bypass",
                    ..Decision::default()
                };
            }
            self.delivered.remove(&input.key);
            self.complete_up(input.key);
            return Decision {
                suppress: pending,
                reason: if pending { "paired-key-up" } else { "bypass" },
                ..Decision::default()
            };
        }
        let mut decision = self.decide(input, game, now_utc);
        if input.down {
            let state = self.delivered.entry(input.key).or_default();
            if decision.suppress {
                if !state.down {
                    state.pending_up = true;
                }
            } else {
                state.down = true;
                state.pending_up = false;
                state.bypass_until_up = false;
            }
        } else if let Some(state) = self.delivered.remove(&input.key) {
            decision.suppress = !state.down && state.pending_up;
            if decision.suppress {
                decision.reason = "paired-key-up";
            }
        }
        decision
    }
    fn decide(&mut self, input: Input, game: bool, now: &str) -> Decision {
        let settings = &self.settings;
        let mut transient = KeyLearning {
            threshold_ms: settings.default_threshold_ms,
            ..KeyLearning::default()
        };
        let learning = if game {
            if let Some(stored) = self.learning.keys.get(&input.key.vk) {
                transient.threshold_ms = stored.threshold_ms;
            }
            &mut transient
        } else {
            self.learning
                .keys
                .entry(input.key.vk)
                .or_insert_with(|| KeyLearning {
                    threshold_ms: settings.default_threshold_ms,
                    ..KeyLearning::default()
                })
        };
        let threshold = settings.threshold(learning.threshold_ms, game);
        let long_hold = if game {
            settings.game_mode_long_hold_bypass_ms
        } else {
            settings.long_hold_bypass_ms
        };
        let t = self.timelines.entry(input.key).or_insert_with(|| Timeline {
            game,
            ..Timeline::default()
        });
        if t.game != game {
            *t = Timeline {
                game,
                ..Timeline::default()
            };
        }
        if !input.down {
            t.down = false;
            t.first_down = None;
            t.first_suppressed = None;
            t.released = true;
            if !game {
                learning.last_seen_utc = Some(now.into());
            }
            return Decision {
                reason: "key-up",
                threshold,
                learning_changed: !game,
                ..Decision::default()
            };
        }
        let interval = t.last_observed.map(|old| input.time.wrapping_sub(old));
        let accepted_interval = t.last_accepted.map(|old| input.time.wrapping_sub(old));
        let released = t.released;
        t.released = false;
        t.last_observed = Some(input.time);
        let accepted_hold = t.down
            && t.first_down
                .is_some_and(|old| input.time.wrapping_sub(old) >= long_hold);
        let suppressed_hold = !t.down
            && t.first_suppressed
                .is_some_and(|old| input.time.wrapping_sub(old) >= long_hold);
        let held = accepted_hold || suppressed_hold;
        let suppress = !held && interval.is_some_and(|value| value <= threshold);
        let before = learning.threshold_ms;
        let mut learning_reason = "";
        if suppress {
            if !t.down && t.first_suppressed.is_none() {
                t.first_suppressed = Some(input.time);
            }
            t.stable = 0;
            if !game {
                learning.suppressed_count = learning.suppressed_count.saturating_add(1);
                let interval = interval.unwrap_or(0);
                if !released || interval == 0 || interval < threshold.saturating_sub(5) {
                    t.near_boundary = 0;
                } else {
                    t.near_boundary += 1;
                    if t.near_boundary >= 2 {
                        t.near_boundary = 0;
                        let measured = ((f64::from(interval) + 5.0) / settings.global_sensitivity)
                            .ceil() as u32;
                        let target = measured.min(250.min(settings.default_threshold_ms + 40));
                        if target > before {
                            learning.threshold_ms = target;
                            learning_reason = "suppressed-near-boundary";
                        }
                    }
                }
            }
        } else {
            if !t.down {
                t.first_down = if suppressed_hold {
                    t.first_suppressed
                } else {
                    Some(input.time)
                };
            }
            t.down = true;
            t.first_suppressed = None;
            t.last_accepted = Some(input.time);
            t.near_boundary = 0;
            if !game {
                learning.accepted_count = learning.accepted_count.saturating_add(1);
                if !held
                    && before > settings.default_threshold_ms
                    && accepted_interval.is_some_and(|v| v >= 300.max(threshold * 2))
                {
                    t.stable += 1;
                    if t.stable >= 12 {
                        t.stable = 0;
                        learning.threshold_ms =
                            settings.default_threshold_ms.max(before.saturating_sub(5));
                        learning_reason = "stable-decay";
                    }
                } else {
                    t.stable = 0;
                }
            } else {
                t.stable = 0;
            }
        }
        let adjustment = learning.threshold_ms as i32 - before as i32;
        if !game {
            learning.last_interval_ms = interval.unwrap_or(0);
            learning.last_seen_utc = Some(now.into());
            if adjustment != 0 {
                learning.last_adjustment_ms = adjustment;
                learning.last_adjustment_reason = learning_reason.into();
                learning.last_adjusted_utc = Some(now.into());
            }
        }
        Decision {
            suppress,
            reason: if suppress {
                "short-repeat"
            } else if held {
                "long-hold"
            } else {
                "accepted"
            },
            threshold,
            interval: interval.unwrap_or(0),
            adjustment,
            learning_changed: !game,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn engine() -> Engine {
        Engine::new(Settings::default(), LearningFile::default())
    }
    fn input(time: u32, down: bool) -> Input {
        Input {
            key: PhysicalKey {
                vk: 65,
                scan: 30,
                extended: false,
            },
            down,
            injected: false,
            time,
        }
    }
    fn p(e: &mut Engine, time: u32, down: bool) -> Decision {
        e.process(input(time, down), true, false, "2026-01-01T00:00:00Z")
    }
    #[test]
    fn chatter_window_restarts_on_every_down() {
        let mut e = engine();
        assert!(!p(&mut e, 0, true).suppress);
        p(&mut e, 1, false);
        for time in [20, 40, 80, 100, 150, 200] {
            assert!(p(&mut e, time, true).suppress);
            assert!(p(&mut e, time + 1, false).suppress);
        }
        assert!(!p(&mut e, 400, true).suppress);
    }
    #[test]
    fn pending_release_survives_pause_and_ignore() {
        for ignored in [false, true] {
            let mut e = engine();
            p(&mut e, 0, true);
            p(&mut e, 1, false);
            assert!(p(&mut e, 20, true).suppress);
            if ignored {
                let mut s = e.settings.clone();
                s.ignored_keys.push(65);
                e.set_settings(s);
            }
            assert!(e.process(input(30, false), ignored, false, "").suppress);
            assert!(!e.process(input(40, false), ignored, false, "").suppress);
        }
    }
    #[test]
    fn bypassed_down_cancels_pending_release() {
        let mut e = engine();
        p(&mut e, 0, true);
        p(&mut e, 1, false);
        p(&mut e, 20, true);
        assert!(!e.process(input(25, true), false, false, "").suppress);
        assert!(!p(&mut e, 26, true).suppress);
        assert!(!p(&mut e, 27, false).suppress);
    }
    #[test]
    fn delivered_hold_survives_mode_transition() {
        let mut e = engine();
        p(&mut e, 0, true);
        assert!(!e.process(input(10, true), false, false, "").suppress);
        assert!(!p(&mut e, 11, true).suppress);
        assert!(!p(&mut e, 12, false).suppress);
    }
    #[test]
    fn long_hold_including_initially_suppressed_hold() {
        let mut e = engine();
        p(&mut e, 0, true);
        p(&mut e, 1, false);
        p(&mut e, 20, true);
        for time in (40..520).step_by(20) {
            assert!(p(&mut e, time, true).suppress);
        }
        assert_eq!(p(&mut e, 520, true).reason, "long-hold");
        assert!(!p(&mut e, 521, false).suppress);
    }
    #[test]
    fn injected_modifier_and_pause_do_not_learn() {
        let mut e = engine();
        let mut i = input(0, true);
        i.injected = true;
        e.process(i, true, false, "");
        i.injected = false;
        i.key.vk = 0xA0;
        e.process(i, true, false, "");
        e.process(input(0, true), false, false, "");
        assert!(e.learning.keys.is_empty());
    }
    #[test]
    fn game_freezes_entire_learning_file() {
        let mut e = engine();
        p(&mut e, 0, true);
        p(&mut e, 1, false);
        let before = e.learning.clone();
        e.settings.game_mode_filtered_keys.push(65);
        for t in 2..50 {
            e.process(input(t, t % 2 == 0), true, true, "new");
        }
        assert_eq!(e.learning, before);
    }
    #[test]
    fn physical_identity_and_clock_wrap() {
        let mut e = engine();
        p(&mut e, u32::MAX - 10, true);
        p(&mut e, u32::MAX - 9, false);
        assert!(p(&mut e, 5, true).suppress);
        let mut i = input(6, true);
        i.key.extended = true;
        assert!(!e.process(i, true, false, "").suppress);
    }
    #[test]
    fn two_near_boundary_suppressions_adjust_once_to_measured_target() {
        let mut e = engine();
        p(&mut e, 0, true);
        p(&mut e, 1, false);
        assert_eq!(p(&mut e, 90, true).adjustment, 0);
        p(&mut e, 91, false);
        assert_eq!(p(&mut e, 180, true).adjustment, 5);
        p(&mut e, 181, false);
        for t in [270, 360, 450, 540] {
            assert_eq!(p(&mut e, t, true).adjustment, 0);
            p(&mut e, t + 1, false);
        }
        assert_eq!(e.learning.keys[&65].threshold_ms, 95);
    }
    #[test]
    fn accepted_events_never_raise_threshold_and_stable_decay_has_floor() {
        let mut e = engine();
        p(&mut e, 0, true);
        p(&mut e, 1, false);
        e.learning.keys.get_mut(&65).unwrap().threshold_ms = 100;
        for n in 1..=24 {
            p(&mut e, n * 400, true);
            p(&mut e, n * 400 + 1, false);
        }
        assert_eq!(e.learning.keys[&65].threshold_ms, 90);
        for n in 25..=50 {
            assert!(p(&mut e, n * 400, true).adjustment <= 0);
            p(&mut e, n * 400 + 1, false);
        }
        assert_eq!(e.learning.keys[&65].threshold_ms, 90);
    }
    #[test]
    fn threshold_uses_dotnet_ties_to_even_rounding() {
        let s = Settings {
            global_sensitivity: 0.5,
            ..Settings::default()
        };
        assert_eq!(s.threshold(91, false), 46);
        assert_eq!(s.threshold(93, false), 46);
    }
}
