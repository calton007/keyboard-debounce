//! Golden decisions produced by the unmodified v0.2.0 C# engine/filter before migration.
//! Six seeded streams, 900 events each, including pause/game/ignore/reset and DWORD wrap.
use keyboard_debounce::{
    engine::{Engine, Input, PhysicalKey},
    model::{LearningFile, Settings},
};
use serde::Deserialize;

#[derive(Deserialize)]
struct Scenario {
    sensitivity: f64,
    events: Vec<Event>,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Event {
    vk: u16,
    scan: u32,
    flags: u32,
    down: bool,
    time: u32,
    enabled: bool,
    game: bool,
    ignored: bool,
    reset: bool,
    suppress: bool,
    threshold: u32,
    interval: u32,
    adjustment: i32,
    learning_changed: bool,
    counts: Vec<Vec<i64>>,
}
#[test]
fn matches_5400_original_csharp_filter_decisions_and_learning_snapshots() {
    let scenarios: Vec<Scenario> =
        serde_json::from_str(include_str!("fixtures/csharp-filter-v0.2.0.json")).unwrap();
    for (scenario_index, scenario) in scenarios.into_iter().enumerate() {
        let mut engine = Engine::new(
            Settings {
                global_sensitivity: scenario.sensitivity,
                game_mode_filtered_keys: vec![65],
                ..Settings::default()
            },
            LearningFile::default(),
        );
        for (index, event) in scenario.events.into_iter().enumerate() {
            if engine.settings.ignored_keys.contains(&65) != event.ignored {
                let mut settings = engine.settings.clone();
                settings.ignored_keys = if event.ignored { vec![65] } else { vec![] };
                engine.set_settings(settings);
            }
            if event.reset {
                engine.boundary();
            }
            let decision = engine.process(
                Input {
                    key: PhysicalKey {
                        vk: event.vk,
                        scan: event.scan,
                        extended: event.flags & 1 != 0,
                    },
                    down: event.down,
                    injected: event.flags & 18 != 0,
                    time: event.time,
                },
                event.enabled,
                event.game,
                "2026-01-01T00:00:00Z",
            );
            let actual = (
                decision.suppress,
                decision.threshold,
                decision.interval,
                decision.adjustment,
                decision.learning_changed,
            );
            let expected = (
                event.suppress,
                event.threshold,
                event.interval,
                event.adjustment,
                event.learning_changed,
            );
            assert_eq!(actual, expected, "scenario {scenario_index} event {index}");
            let counts: Vec<Vec<i64>> = engine
                .learning
                .keys
                .iter()
                .map(|(vk, k)| {
                    vec![
                        i64::from(*vk),
                        i64::from(k.threshold_ms),
                        k.accepted_count as i64,
                        k.suppressed_count as i64,
                        i64::from(k.last_interval_ms),
                        i64::from(k.last_adjustment_ms),
                    ]
                })
                .collect();
            assert_eq!(
                counts, event.counts,
                "learning scenario {scenario_index} event {index}"
            );
        }
    }
}
