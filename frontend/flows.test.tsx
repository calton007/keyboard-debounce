import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { App } from "./App";
import type { Snapshot } from "./generated/Snapshot";

const bridge = vi.hoisted(() => ({
  snapshot: null as Snapshot | null,
  command: vi.fn(),
  query: vi.fn(),
  connect: vi.fn(async () => () => {}),
}));
vi.mock("./api", () => ({
  useSnapshot: () => bridge.snapshot,
  connect: bridge.connect,
  command: bridge.command,
  query: bridge.query,
  patchSettings: (patch: unknown) =>
    bridge.command("update_settings", { patch }),
}));
function initial(): Snapshot {
  return {
    revision: 1,
    settings: {
      schemaVersion: 1,
      enabled: true,
      startWithWindows: false,
      silentRun: false,
      globalSensitivity: 1,
      defaultThresholdMs: 90,
      longHoldBypassMs: 500,
      startupDelayMs: 3000,
      pauseHotkey: "Ctrl+Alt+F11",
      ignoredKeys: [],
      processGameModeEnabled: false,
      gameProcesses: [],
      gameModeThresholdMs: 45,
      gameModeLongHoldBypassMs: 250,
      gameModeFilteredKeys: [],
    },
    effectiveEnabled: true,
    startupRemainingMs: 0,
    hookError: null,
    hotkeyError: null,
    registeredHotkey: "Ctrl+Alt+F11",
    game: { active: false, running: [], unknown: [], error: null },
    keys: [],
    recentEvent: null,
    persistence: {
      settingsPending: false,
      learningPending: false,
      settingsError: null,
      learningError: null,
      loadErrors: [],
    },
    configDirectory: "C:\\test\\KeyboardDebounceTauri",
    isAdmin: false,
    version: "0.3.0",
  };
}
beforeEach(() => {
  bridge.snapshot = initial();
  bridge.command.mockReset().mockImplementation(async () => bridge.snapshot);
  bridge.query.mockReset();
  // jsdom does not implement native dialogs; test only the React interaction contract here.
  Object.defineProperty(HTMLDialogElement.prototype, "showModal", {
    configurable: true,
    value: function (this: HTMLDialogElement) {
      this.setAttribute("open", "");
    },
  });
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  Reflect.deleteProperty(HTMLDialogElement.prototype, "showModal");
});
function page(name: string) {
  fireEvent.click(screen.getByRole("tab", { name }));
}

it("热键只在显式应用时提交，失败保留草稿并显示原始错误", async () => {
  render(<App />);
  page("应用设置");
  const input = screen.getByRole("textbox", { name: "热键" });
  fireEvent.change(input, { target: { value: "Ctrl+Alt+F10" } });
  expect(bridge.command).not.toHaveBeenCalled();
  bridge.command.mockRejectedValueOnce("RegisterHotKey: 1409");
  fireEvent.click(screen.getByRole("button", { name: "应用热键" }));
  await waitFor(() =>
    expect(screen.getByRole("alert").textContent).toContain(
      "RegisterHotKey: 1409",
    ),
  );
  expect(bridge.command).toHaveBeenCalledWith("apply_hotkey", {
    hotkey: "Ctrl+Alt+F10",
  });
  expect((input as HTMLInputElement).value).toBe("Ctrl+Alt+F10");
  expect(screen.getByText("当前绑定：Ctrl+Alt+F11")).toBeTruthy();
});

it("文件选择的多个 EXE 通过一次列表命令提交", async () => {
  bridge.query.mockResolvedValueOnce(["game-a", "game-b"]);
  render(<App />);
  page("游戏模式");
  fireEvent.click(screen.getByRole("button", { name: /选择 EXE/ }));
  await waitFor(() =>
    expect(bridge.command).toHaveBeenCalledWith("change_games", {
      names: ["game-a", "game-b"],
      add: true,
    }),
  );
  expect(bridge.query).toHaveBeenCalledWith("browse_executables");
});

it("热键应用尚未返回时继续编辑，成功回执不会覆盖新草稿", async () => {
  let finish: ((value: Snapshot) => void) | undefined;
  bridge.command.mockImplementationOnce(
    () =>
      new Promise<Snapshot>((resolve) => {
        finish = resolve;
      }),
  );
  const { rerender } = render(<App />);
  page("应用设置");
  const input = screen.getByRole("textbox", { name: "热键" });
  fireEvent.change(input, { target: { value: "Ctrl+Alt+F10" } });
  fireEvent.click(screen.getByRole("button", { name: "应用热键" }));
  fireEvent.change(input, { target: { value: "Ctrl+Alt+F9" } });
  const next = initial();
  next.revision = 2;
  next.settings.pauseHotkey = "Ctrl+Alt+F10";
  next.registeredHotkey = "Ctrl+Alt+F10";
  bridge.snapshot = next;
  finish?.(next);
  rerender(<App />);
  await waitFor(() =>
    expect(screen.getByText("当前绑定：Ctrl+Alt+F10")).toBeTruthy(),
  );
  expect((input as HTMLInputElement).value).toBe("Ctrl+Alt+F9");
});

it("运行程序选择器保留筛选前的多选并批量添加", async () => {
  bridge.query.mockResolvedValueOnce(["alpha", "beta"]);
  render(<App />);
  page("游戏模式");
  fireEvent.click(screen.getByRole("button", { name: "从正在运行的程序添加" }));
  const dialog = await screen.findByRole("dialog");
  fireEvent.click(
    await within(dialog).findByRole("checkbox", { name: "alpha.exe" }),
  );
  fireEvent.change(within(dialog).getByRole("textbox"), {
    target: { value: "beta" },
  });
  fireEvent.click(within(dialog).getByRole("checkbox", { name: "beta.exe" }));
  fireEvent.click(
    within(dialog).getByRole("button", { name: "添加所选（2）" }),
  );
  await waitFor(() =>
    expect(bridge.command).toHaveBeenCalledWith("change_games", {
      names: ["alpha", "beta"],
      add: true,
    }),
  );
});

it("维护操作先确认，取消不修改数据", async () => {
  render(<App />);
  page("按键管理");
  fireEvent.click(screen.getByRole("button", { name: "重置学习数据" }));
  fireEvent.click(
    within(screen.getByRole("dialog")).getByRole("button", { name: "取消" }),
  );
  expect(bridge.command).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "重置学习数据" }));
  fireEvent.click(
    within(screen.getByRole("dialog")).getByRole("button", { name: "确认" }),
  );
  await waitFor(() =>
    expect(bridge.command).toHaveBeenCalledWith("reset_learning"),
  );
});

it("状态推送与导航不丢失搜索内容，方向键和 Alt 快捷键可切页", () => {
  const { rerender } = render(<App />);
  fireEvent.keyDown(window, { key: "4", altKey: true });
  const input = screen.getByRole("textbox", { name: "搜索" });
  input.focus();
  fireEvent.change(input, { target: { value: "Space" } });
  bridge.snapshot = { ...initial(), revision: 2, recentEvent: "A · 已放行" };
  rerender(<App />);
  expect(document.activeElement).toBe(input);
  expect((input as HTMLInputElement).value).toBe("Space");
  const tab = screen.getByRole("tab", { name: "按键管理" });
  fireEvent.keyDown(tab, { key: "ArrowRight" });
  expect(
    screen.getByRole("tab", { name: "应用设置" }).getAttribute("aria-selected"),
  ).toBe("true");
  page("按键管理");
  expect(
    (screen.getByRole("textbox", { name: "搜索" }) as HTMLInputElement).value,
  ).toBe("Space");
});

it("概览只展示一份参数和最近事件，暂停时不突出任何活动模式", () => {
  const next = initial();
  next.recentEvent = "A · 已放行";
  bridge.snapshot = next;
  const { rerender } = render(<App />);
  const overview = screen.getByRole("tabpanel", { name: "概览" });
  expect(within(overview).getAllByText("A · 已放行")).toHaveLength(1);
  expect(within(overview).getAllByText("默认阈值")).toHaveLength(1);
  expect(within(overview).getAllByText("已学习按键")).toHaveLength(1);
  expect(overview.querySelectorAll(".active-card")).toHaveLength(1);
  bridge.snapshot = {
    ...next,
    revision: 2,
    effectiveEnabled: false,
    settings: { ...next.settings, enabled: false },
  };
  rerender(<App />);
  expect(overview.querySelector(".active-card")).toBeNull();
  expect(within(overview).getByText("已暂停")).toBeTruthy();
});

it("五页不再渲染常驻说明，短标签仍可完成静默启动设置", async () => {
  const { container } = render(<App />);
  for (const copy of [
    "学习规则",
    "普通模式参数摘要",
    "当前模式、最近事件",
    "不会运行",
    "不记录输入文本",
    "无需管理员权限",
    "这里只保留",
    "点击表头排序",
    "常用操作",
  ]) {
    expect(container.textContent).not.toContain(copy);
  }
  page("普通防抖");
  expect(screen.getAllByRole("slider")).toHaveLength(3);
  expect(screen.getAllByRole("spinbutton")).toHaveLength(3);
  page("应用设置");
  fireEvent.click(screen.getByRole("switch", { name: "启动时仅显示托盘" }));
  await waitFor(() =>
    expect(bridge.command).toHaveBeenCalledWith("update_settings", {
      patch: { silentRun: true },
    }),
  );
});

it("空状态简短且保存失败持续可见，成功操作不能隐藏后端错误", async () => {
  const next = initial();
  next.persistence.settingsError = "settings.json: access denied";
  next.persistence.settingsPending = true;
  bridge.snapshot = next;
  render(<App />);
  expect(screen.getByText("暂无事件")).toBeTruthy();
  expect(
    within(screen.getByRole("tabpanel", { name: "概览" })).getByText(
      "保存失败 · 已生效",
    ),
  ).toBeTruthy();
  page("游戏模式");
  expect(screen.getByText("未添加游戏")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "刷新检测状态" }));
  await waitFor(() =>
    expect(bridge.command).toHaveBeenCalledWith("refresh_games"),
  );
  expect(screen.getByRole("alert").textContent).toContain(
    "settings.json: access denied",
  );
  page("按键管理");
  expect(screen.getByText("无匹配按键")).toBeTruthy();
});

it("运行程序加载完成后移除加载状态，空结果保留搜索框", async () => {
  let finish: ((names: string[]) => void) | undefined;
  bridge.query.mockImplementationOnce(
    () =>
      new Promise<string[]>((resolve) => {
        finish = resolve;
      }),
  );
  render(<App />);
  page("游戏模式");
  fireEvent.click(screen.getByRole("button", { name: "从正在运行的程序添加" }));
  const dialog = screen.getByRole("dialog");
  expect(within(dialog).getByRole("status").textContent).toBe(
    "正在读取运行中程序…",
  );
  finish?.([]);
  await within(dialog).findByText("无匹配程序");
  expect(within(dialog).queryByRole("status")).toBeNull();
  expect(
    within(dialog).getByRole("textbox", { name: "搜索运行中 EXE" }),
  ).toBeTruthy();
});

it("按键规则操作和维护确认后果在紧凑表格中保留", async () => {
  const next = initial();
  next.keys = [
    {
      vk: 65,
      name: "A",
      learned: true,
      ignored: false,
      gameFiltered: false,
      effectiveThresholdMs: 90,
      learning: {
        thresholdMs: 90,
        acceptedCount: 1,
        suppressedCount: 0,
        lastIntervalMs: 0,
        lastSeenUtc: null,
        lastAdjustmentMs: 0,
        lastAdjustmentReason: "",
        lastAdjustedUtc: null,
      },
    },
  ];
  bridge.snapshot = next;
  render(<App />);
  page("按键管理");
  fireEvent.click(screen.getByRole("checkbox", { name: "A 游戏防抖" }));
  await waitFor(() =>
    expect(bridge.command).toHaveBeenCalledWith("set_key_rule", {
      vk: 65,
      game: true,
    }),
  );
  fireEvent.click(screen.getByRole("checkbox", { name: "A 始终忽略" }));
  await waitFor(() =>
    expect(bridge.command).toHaveBeenCalledWith("set_key_rule", {
      vk: 65,
      ignored: true,
    }),
  );
  fireEvent.click(screen.getByRole("button", { name: "重置学习数据" }));
  expect(
    within(screen.getByRole("dialog")).getByText(
      "将清空学习阈值和统计，保留游戏防抖与忽略规则。",
    ),
  ).toBeTruthy();
});
