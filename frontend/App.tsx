import {
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { command, connect, patchSettings, query, useSnapshot } from "./api";
import type { Snapshot } from "./generated/Snapshot";
import type { SettingsPatch } from "./generated/SettingsPatch";
import type { KeyRow } from "./generated/KeyRow";

const pages = ["概览", "普通防抖", "游戏模式", "按键管理", "应用设置"];
const glyphs = ["\uE80F", "\uE9E9", "\uE7FC", "\uE765", "\uE713"];
type Run = (operation: () => Promise<unknown>) => Promise<void>;
function Card({
  title,
  actions,
  children,
  className = "",
}: {
  title?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={`card ${className}`}>
      {(title || actions) && (
        <div className="card-heading">
          {title && <h2>{title}</h2>}
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}
function Toggle({
  label,
  value,
  onChange,
  disabled = false,
}: {
  label: string;
  value: boolean;
  onChange: (value: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <label className="toggle-label">
      <span>{label}</span>
      <button
        type="button"
        role="switch"
        aria-checked={value}
        aria-label={label}
        className="switch"
        disabled={disabled}
        onClick={() => onChange(!value)}
      >
        <span />
      </button>
    </label>
  );
}
export function NumberSetting({
  label,
  value,
  min,
  max,
  step = 1,
  unit = "ms",
  onSave,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  unit?: string;
  onSave: (value: number) => void;
}) {
  const id = useId();
  const [draft, setDraft] = useState(String(value));
  const [invalid, setInvalid] = useState(false);
  const focused = useRef(false);
  useEffect(() => {
    if (!focused.current) setDraft(String(value));
  }, [value]);
  const edit = (text: string) => {
    setDraft(text);
    const n = Number(text);
    const valid =
      text.trim() !== "" &&
      Number.isFinite(n) &&
      n >= min &&
      n <= max &&
      (step < 1 || Number.isInteger(n));
    setInvalid(!valid);
    if (valid) onSave(n);
  };
  return (
    <div className="number-setting">
      <label htmlFor={id}>{label}</label>
      <input
        className="range"
        type="range"
        aria-label={`${label}滑块`}
        min={min}
        max={max}
        step={step}
        value={invalid ? value : Number(draft)}
        onFocus={() => {
          focused.current = true;
        }}
        onBlur={() => {
          focused.current = false;
        }}
        onChange={(e) => edit(e.target.value)}
      />
      <div className="number-box">
        <input
          id={id}
          type="number"
          min={min}
          max={max}
          step={step}
          value={draft}
          aria-invalid={invalid}
          aria-describedby={invalid ? `${id}-error` : undefined}
          onFocus={() => {
            focused.current = true;
          }}
          onBlur={() => {
            focused.current = false;
          }}
          onChange={(e) => edit(e.target.value)}
        />
        <span>{unit}</span>
      </div>
      {invalid && (
        <p className="field-error" id={`${id}-error`}>
          请输入 {min}–{max} 范围内的{step === 1 ? "整数" : "数值"}。
        </p>
      )}
    </div>
  );
}
function Rows({ values }: { values: [string, ReactNode][] }) {
  return (
    <dl>
      {values.map(([name, value]) => (
        <div key={name}>
          <dt>{name}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}
function protection(s: Snapshot) {
  if (s.hookError) return "保护故障";
  if (!s.registeredHotkey) return "热键不可用";
  if (!s.settings.enabled) return "已暂停";
  if (s.startupRemainingMs > 0) return "启动延迟中";
  return s.effectiveEnabled ? "已启用" : "未启用";
}
function gameState(s: Snapshot) {
  if (!s.settings.processGameModeEnabled) return "自动切换已关闭";
  if (s.game.error)
    return `状态未知 · 保持${s.game.active ? "游戏" : "普通"}模式`;
  return s.game.active
    ? `游戏模式 · ${s.game.running.map((n) => `${n}.exe`).join("、")}`
    : "普通模式 · 等待游戏 EXE";
}
function saveState(s: Snapshot) {
  if (s.persistence.settingsError || s.persistence.learningError)
    return "保存失败 · 已生效";
  return s.persistence.settingsPending || s.persistence.learningPending
    ? "待保存 · 已生效"
    : "已保存";
}
function Overview({
  s,
  navigate,
}: {
  s: Snapshot;
  navigate: (page: number) => void;
}) {
  const p = s.settings;
  const learned = s.keys.filter((k) => k.learned).length;
  const activeMode = s.effectiveEnabled
    ? s.game.active
      ? "游戏模式"
      : "普通防抖"
    : protection(s);
  return (
    <>
      <div className="status-strip">
        <Rows
          values={[
            ["当前模式", activeMode],
            ["暂停热键", s.registeredHotkey ?? "未注册"],
            ["保存状态", saveState(s)],
          ]}
        />
      </div>
      <div className="two-grid overview-grid">
        <Card
          title="普通防抖"
          className={s.effectiveEnabled && !s.game.active ? "active-card" : ""}
          actions={<button onClick={() => navigate(1)}>调整参数</button>}
        >
          <Rows
            values={[
              ["敏感度", `${p.globalSensitivity.toFixed(2)}×`],
              ["默认阈值", `${p.defaultThresholdMs}ms`],
              ["长按放行", `${p.longHoldBypassMs}ms`],
              ["已学习按键", learned],
              ["始终忽略", p.ignoredKeys.length],
            ]}
          />
        </Card>
        <Card
          title="游戏模式"
          className={s.effectiveEnabled && s.game.active ? "active-card" : ""}
          actions={<button onClick={() => navigate(2)}>管理游戏</button>}
        >
          <Rows
            values={[
              ["自动切换", p.processGameModeEnabled ? "已开启" : "已关闭"],
              ["阈值上限", `${p.gameModeThresholdMs}ms`],
              ["长按放行", `${p.gameModeLongHoldBypassMs}ms`],
              ["游戏 EXE", p.gameProcesses.length],
              ["游戏防抖键", p.gameModeFilteredKeys.length],
            ]}
          />
        </Card>
      </div>
      <div className="recent-row">
        <h2>最近事件</h2>
        <p>{s.recentEvent ?? "暂无事件"}</p>
      </div>
    </>
  );
}
function Normal({ s, run }: { s: Snapshot; run: Run }) {
  const save = (field: keyof SettingsPatch) => (value: number) => {
    void run(() => patchSettings({ [field]: value }));
  };
  return (
    <Card className="normal-settings">
      <NumberSetting
        label="敏感度"
        value={s.settings.globalSensitivity}
        min={0.5}
        max={3}
        step={0.05}
        unit="×"
        onSave={save("globalSensitivity")}
      />
      <NumberSetting
        label="默认阈值"
        value={s.settings.defaultThresholdMs}
        min={20}
        max={250}
        onSave={save("defaultThresholdMs")}
      />
      <NumberSetting
        label="长按放行"
        value={s.settings.longHoldBypassMs}
        min={250}
        max={1000}
        onSave={save("longHoldBypassMs")}
      />
    </Card>
  );
}
function ProcessPicker({
  close,
  add,
}: {
  close: () => void;
  add: (names: string[]) => Promise<void>;
}) {
  const [names, setNames] = useState<string[]>([]);
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    dialog.current?.showModal();
    let live = true;
    query("list_processes")
      .then((v) => {
        if (live) setNames(v);
      })
      .catch((e) => {
        if (live) setError(String(e));
      })
      .finally(() => {
        if (live) setLoading(false);
      });
    return () => {
      live = false;
    };
  }, []);
  const visibleNames = names.filter((n) => n.includes(search.toLowerCase()));
  return (
    <dialog ref={dialog} onCancel={close} aria-labelledby="process-title">
      <h2 id="process-title">从正在运行的程序添加</h2>
      <input
        autoFocus
        aria-label="搜索运行中 EXE"
        placeholder="搜索 EXE"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <div className="process-list">
        {loading && <p role="status">正在读取运行中程序…</p>}
        {error && (
          <p role="alert" className="field-error">
            {error}
          </p>
        )}
        {!loading && !error && !visibleNames.length && <p>无匹配程序</p>}
        {visibleNames.map((name) => (
          <label key={name}>
            <input
              type="checkbox"
              checked={selected.includes(name)}
              onChange={(e) =>
                setSelected((old) =>
                  e.target.checked
                    ? [...old, name]
                    : old.filter((n) => n !== name),
                )
              }
            />
            <span className="truncate" title={`${name}.exe`}>
              {name}.exe
            </span>
          </label>
        ))}
      </div>
      <footer>
        <button disabled={busy} onClick={close}>
          取消
        </button>
        <button
          className="primary"
          disabled={!selected.length || busy}
          onClick={() => {
            setBusy(true);
            add(selected)
              .then(close)
              .catch((e) => setError(String(e)))
              .finally(() => setBusy(false));
          }}
        >
          添加所选（{selected.length}）
        </button>
      </footer>
    </dialog>
  );
}
function Game({ s, run }: { s: Snapshot; run: Run }) {
  const [picker, setPicker] = useState(false);
  const p = s.settings;
  return (
    <div className="game-grid">
      <Card title="自动切换" className="game-settings">
        <Toggle
          label="根据游戏 EXE 自动切换"
          value={p.processGameModeEnabled}
          onChange={(value) =>
            void run(() => patchSettings({ processGameModeEnabled: value }))
          }
        />
        <div
          className={`mode-box ${s.effectiveEnabled && s.game.active ? "active-card" : ""}`}
        >
          <strong>{gameState(s)}</strong>
        </div>
        <NumberSetting
          label="阈值上限"
          value={p.gameModeThresholdMs}
          min={20}
          max={250}
          onSave={(value) =>
            void run(() => patchSettings({ gameModeThresholdMs: value }))
          }
        />
        <NumberSetting
          label="长按放行"
          value={p.gameModeLongHoldBypassMs}
          min={50}
          max={1000}
          onSave={(value) =>
            void run(() => patchSettings({ gameModeLongHoldBypassMs: value }))
          }
        />
      </Card>
      <Card title="游戏 EXE" className="game-list-card">
        <div className="actions game-actions">
          <button
            className="primary"
            onClick={() =>
              void run(async () => {
                const names = await query("browse_executables");
                if (names.length)
                  await command("change_games", { names, add: true });
              })
            }
          >
            浏览选择 EXE
          </button>
          <button onClick={() => setPicker(true)}>从正在运行的程序添加</button>
          <button onClick={() => void run(() => command("refresh_games"))}>
            刷新检测状态
          </button>
        </div>
        <div className="game-list">
          {!p.gameProcesses.length && <div className="empty">未添加游戏</div>}
          {p.gameProcesses.map((name) => (
            <div className="game-row" key={name}>
              <div className="game-name">
                <strong className="truncate" title={`${name}.exe`}>
                  {name}.exe
                </strong>
                <span className="caption">
                  {!p.processGameModeEnabled
                    ? "检测已关闭"
                    : s.game.unknown.includes(name)
                      ? "状态未知"
                      : s.game.running.includes(name)
                        ? "正在运行"
                        : "未运行"}
                </span>
              </div>
              <button
                aria-label={`移除 ${name}.exe`}
                onClick={() =>
                  void run(() =>
                    command("change_games", { names: [name], add: false }),
                  )
                }
              >
                移除
              </button>
            </div>
          ))}
        </div>
      </Card>
      {picker && (
        <ProcessPicker
          close={() => setPicker(false)}
          add={async (names) => {
            await command("change_games", { names, add: true });
          }}
        />
      )}
    </div>
  );
}

type SortKey =
  | "vk"
  | "name"
  | "threshold"
  | "accepted"
  | "suppressed"
  | "seen"
  | "game"
  | "ignored";
export function filterAndSort(
  rows: KeyRow[],
  search: string,
  scope: string,
  sort: SortKey,
  ascending: boolean,
) {
  const value = (row: KeyRow): number | string =>
    ({
      vk: row.vk,
      name: row.name,
      threshold: row.effectiveThresholdMs,
      accepted: row.learning.acceptedCount,
      suppressed: row.learning.suppressedCount,
      seen: row.learning.lastSeenUtc ?? "",
      game: Number(row.gameFiltered),
      ignored: Number(row.ignored),
    })[sort];
  return rows
    .filter(
      (r) =>
        `${r.vk} ${r.name}`.toLowerCase().includes(search.toLowerCase()) &&
        (scope === "all" ||
          (scope === "learned" && r.learned) ||
          (scope === "game" && r.gameFiltered) ||
          (scope === "ignored" && r.ignored)),
    )
    .sort((a, b) => {
      if (sort !== "vk" && a.ignored !== b.ignored) return a.ignored ? 1 : -1;
      const av = value(a),
        bv = value(b);
      return (av < bv ? -1 : av > bv ? 1 : a.vk - b.vk) * (ascending ? 1 : -1);
    });
}
function Confirm({
  title,
  children,
  close,
  action,
}: {
  title: string;
  children: ReactNode;
  close: () => void;
  action: () => Promise<void>;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    ref.current?.showModal();
  }, []);
  return (
    <dialog ref={ref} onCancel={close} aria-label={title}>
      <h2>{title}</h2>
      <p>{children}</p>
      {error && <p role="alert">{error}</p>}
      <footer>
        <button autoFocus disabled={busy} onClick={close}>
          取消
        </button>
        <button
          className="primary"
          disabled={busy}
          onClick={() => {
            setBusy(true);
            action()
              .then(close)
              .catch((e) => setError(String(e)))
              .finally(() => setBusy(false));
          }}
        >
          确认
        </button>
      </footer>
    </dialog>
  );
}
function Keys({ s, run }: { s: Snapshot; run: Run }) {
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [scope, setScope] = useState("all");
  const [vk, setVk] = useState("65");
  const [sort, setSort] = useState<SortKey>("vk");
  const [ascending, setAscending] = useState(true);
  const [membership, setMembership] = useState(() => s.keys.map((k) => k.vk));
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [selected, setSelected] = useState<number | null>(null);
  const [confirm, setConfirm] = useState<
    "clear_ignored" | "reset_learning" | null
  >(null);
  const latest = useRef(s.keys);
  latest.current = s.keys;
  useEffect(() => {
    const timer = setTimeout(() => setAppliedSearch(search), 150);
    return () => clearTimeout(timer);
  }, [search]);
  // Only explicit list operations change row order/membership. Telemetry updates values in place.
  const ids = useMemo(
    () =>
      filterAndSort(
        latest.current.filter((k) => membership.includes(k.vk)),
        appliedSearch,
        scope,
        sort,
        ascending,
      ).map((k) => k.vk),
    [membership, appliedSearch, scope, sort, ascending, refreshVersion],
  );
  const rows = ids
    .map((id) => s.keys.find((k) => k.vk === id))
    .filter((r): r is KeyRow => !!r);
  const refresh = (next: Snapshot) => {
    setMembership(next.keys.map((k) => k.vk));
    setRefreshVersion((v) => v + 1);
  };
  const headers: [string, SortKey][] = [
    ["VK", "vk"],
    ["按键", "name"],
    ["阈值(ms)", "threshold"],
    ["放行次数", "accepted"],
    ["拦截次数", "suppressed"],
    ["最近事件", "seen"],
    ["游戏防抖", "game"],
    ["始终忽略", "ignored"],
  ];
  return (
    <div className="key-page">
      <Card className="key-toolbar">
        <div className="key-filters">
          <label>
            搜索
            <input
              placeholder="输入 VK 或按键名"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </label>
          <label>
            范围
            <select value={scope} onChange={(e) => setScope(e.target.value)}>
              <option value="all">全部</option>
              <option value="learned">仅已学习</option>
              <option value="game">仅游戏防抖</option>
              <option value="ignored">仅始终忽略</option>
            </select>
          </label>
          <label>
            手动 VK
            <input
              type="number"
              min="1"
              max="255"
              value={vk}
              onChange={(e) => setVk(e.target.value)}
            />
          </label>
          <button
            className="primary"
            disabled={
              !Number.isInteger(Number(vk)) ||
              Number(vk) < 1 ||
              Number(vk) > 255
            }
            onClick={() =>
              void run(async () =>
                refresh(
                  await command("set_key_rule", { vk: Number(vk), game: true }),
                ),
              )
            }
          >
            添加到游戏防抖
          </button>
        </div>
      </Card>
      <Card
        title="按键列表"
        className="table-card"
        actions={
          <div className="actions table-actions">
            <button
              onClick={() =>
                void run(async () => refresh(await command("get_snapshot")))
              }
            >
              刷新按键列表
            </button>
            <button onClick={() => setConfirm("clear_ignored")}>
              清空忽略列表
            </button>
            <button onClick={() => setConfirm("reset_learning")}>
              重置学习数据
            </button>
          </div>
        }
      >
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                {headers.map(([title, key]) => (
                  <th
                    key={key}
                    aria-sort={
                      sort === key
                        ? ascending
                          ? "ascending"
                          : "descending"
                        : "none"
                    }
                  >
                    <button
                      onClick={() => {
                        if (sort === key) setAscending(!ascending);
                        else {
                          setSort(key);
                          setAscending(true);
                        }
                      }}
                    >
                      {title}
                      {sort === key ? (ascending ? " ↑" : " ↓") : ""}
                    </button>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr
                  key={row.vk}
                  className={selected === row.vk ? "selected" : ""}
                  onClick={() => setSelected(row.vk)}
                >
                  <td>{row.vk}</td>
                  <td>
                    <span className="truncate" title={row.name}>
                      {row.name}
                    </span>
                  </td>
                  <td>{row.effectiveThresholdMs}</td>
                  <td>{row.learning.acceptedCount}</td>
                  <td>{row.learning.suppressedCount}</td>
                  <td title={row.learning.lastSeenUtc ?? ""}>
                    {row.learning.lastSeenUtc
                      ? new Date(row.learning.lastSeenUtc).toLocaleTimeString()
                      : "—"}
                  </td>
                  <td>
                    <input
                      aria-label={`${row.name} 游戏防抖`}
                      type="checkbox"
                      checked={row.gameFiltered}
                      onChange={(e) =>
                        void run(() =>
                          command("set_key_rule", {
                            vk: row.vk,
                            game: e.target.checked,
                          }),
                        )
                      }
                    />
                  </td>
                  <td>
                    <input
                      aria-label={`${row.name} 始终忽略`}
                      type="checkbox"
                      checked={row.ignored}
                      onChange={(e) =>
                        void run(() =>
                          command("set_key_rule", {
                            vk: row.vk,
                            ignored: e.target.checked,
                          }),
                        )
                      }
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!rows.length && <div className="empty">无匹配按键</div>}
        </div>
      </Card>
      {confirm && (
        <Confirm
          title={confirm === "clear_ignored" ? "清空忽略列表" : "重置学习数据"}
          close={() => setConfirm(null)}
          action={async () => {
            refresh(await command(confirm));
          }}
        >
          {confirm === "clear_ignored"
            ? "清空后，这些按键将重新参与普通防抖。"
            : "将清空学习阈值和统计，保留游戏防抖与忽略规则。"}
        </Confirm>
      )}
    </div>
  );
}
function Application({ s, run }: { s: Snapshot; run: Run }) {
  const [hotkey, setHotkey] = useState(s.settings.pauseHotkey);
  const hotkeyDraft = useRef(hotkey);
  const [dirty, setDirty] = useState(false);
  useEffect(() => {
    if (!dirty) setHotkey(s.settings.pauseHotkey);
  }, [s.settings.pauseHotkey, dirty]);
  return (
    <>
      <div className="two-grid">
        <Card title="启动与运行">
          <Toggle
            label="开机自启"
            value={s.settings.startWithWindows}
            onChange={(enabled) =>
              void run(() => command("set_startup", { enabled }))
            }
          />
          <Toggle
            label="启动时仅显示托盘"
            value={s.settings.silentRun}
            onChange={(silentRun) =>
              void run(() => patchSettings({ silentRun }))
            }
          />
          <NumberSetting
            label="启动延迟"
            value={s.settings.startupDelayMs}
            min={0}
            max={30000}
            onSave={(startupDelayMs) =>
              void run(() => patchSettings({ startupDelayMs }))
            }
          />
        </Card>
        <Card title="暂停热键">
          <div className="hotkey-editor">
            <label className="field">
              热键
              <input
                value={hotkey}
                onChange={(e) => {
                  setHotkey(e.target.value);
                  hotkeyDraft.current = e.target.value;
                  setDirty(true);
                }}
              />
            </label>
            <button
              className="primary"
              onClick={() =>
                void run(async () => {
                  await command("apply_hotkey", { hotkey });
                  if (hotkeyDraft.current === hotkey) setDirty(false);
                })
              }
            >
              应用热键
            </button>
          </div>
          <p className="caption current-binding">
            当前绑定：{s.registeredHotkey ?? "未注册"}
          </p>
        </Card>
      </div>
      <Card
        title="应用信息"
        className="app-info"
        actions={
          <div className="actions">
            <button
              onClick={() => void run(() => query("open_config_directory"))}
            >
              打开配置目录
            </button>
            <button onClick={() => void run(() => command("refresh_admin"))}>
              刷新权限状态
            </button>
          </div>
        }
      >
        <Rows
          values={[
            ["运行权限", s.isAdmin ? "管理员权限" : "普通权限"],
            [
              "配置目录",
              <span className="path" title={s.configDirectory}>
                {s.configDirectory}
              </span>,
            ],
            ["保存状态", saveState(s)],
            ["版本", s.version],
          ]}
        />
      </Card>
    </>
  );
}
export function App() {
  const s = useSnapshot();
  const [page, setPage] = useState(0);
  const [error, setError] = useState("");
  const tabs = useRef<(HTMLButtonElement | null)[]>([]);
  useEffect(() => {
    let active = true;
    let dispose: (() => void) | undefined;
    connect()
      .then((fn) => {
        if (active) dispose = fn;
        else fn();
      })
      .catch((e) => {
        if (active) setError(String(e));
      });
    return () => {
      active = false;
      dispose?.();
    };
  }, []);
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (e.altKey && /^[1-5]$/.test(e.key)) {
        e.preventDefault();
        const p = Number(e.key) - 1;
        setPage(p);
        tabs.current[p]?.focus();
      }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, []);
  const run: Run = async (operation) => {
    try {
      await operation();
      setError("");
    } catch (e) {
      setError(String(e));
    }
  };
  const errors = s
    ? [
        error,
        s.hookError,
        s.hotkeyError,
        s.game.error,
        s.persistence.settingsError,
        s.persistence.learningError,
        ...s.persistence.loadErrors,
      ].filter(Boolean)
    : [error].filter(Boolean);
  return (
    <div className="app">
      <header>
        <nav role="tablist" aria-label="设置页面">
          {pages.map((label, i) => (
            <button
              ref={(element) => {
                tabs.current[i] = element;
              }}
              id={`tab-${i}`}
              aria-controls={`page-${i}`}
              role="tab"
              aria-selected={page === i}
              tabIndex={page === i ? 0 : -1}
              key={label}
              onClick={() => setPage(i)}
              onKeyDown={(e) => {
                if (
                  ["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key)
                ) {
                  e.preventDefault();
                  const next =
                    e.key === "Home"
                      ? 0
                      : e.key === "End"
                        ? 4
                        : (i + (e.key === "ArrowRight" ? 1 : 4)) % 5;
                  setPage(next);
                  tabs.current[next]?.focus();
                }
              }}
            >
              <span className="glyph" aria-hidden="true">
                {glyphs[i]}
              </span>
              {label}
            </button>
          ))}
        </nav>
        {s && (
          <Toggle
            label={protection(s)}
            value={s.settings.enabled}
            onChange={(enabled) =>
              void run(() => command("set_enabled", { enabled }))
            }
          />
        )}
      </header>
      <main>
        {errors.length > 0 && (
          <div className="error-banner" role="alert">
            {[...new Set(errors)].map((message, i) => (
              <div key={i}>{message}</div>
            ))}
          </div>
        )}
        {!s ? (
          <div className="empty">
            {error
              ? "无法连接键盘核心，请通过托盘退出后重新启动。"
              : "正在连接键盘核心…"}
          </div>
        ) : (
          <>
            {pages.map((title, i) => (
              <section
                id={`page-${i}`}
                role="tabpanel"
                aria-labelledby={`tab-${i}`}
                key={title}
                hidden={page !== i}
                className={`page ${i === 2 || i === 3 ? "fill-page" : ""}`}
              >
                <h1>{title}</h1>
                {i === 0 ? (
                  <Overview s={s} navigate={setPage} />
                ) : i === 1 ? (
                  <Normal s={s} run={run} />
                ) : i === 2 ? (
                  <Game s={s} run={run} />
                ) : i === 3 ? (
                  <Keys s={s} run={run} />
                ) : (
                  <Application s={s} run={run} />
                )}
              </section>
            ))}
          </>
        )}
      </main>
    </div>
  );
}
