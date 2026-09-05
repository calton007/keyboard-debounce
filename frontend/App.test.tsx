import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { NumberSetting, filterAndSort } from "./App";
import type { KeyRow } from "./generated/KeyRow";

afterEach(cleanup);
function row(vk: number, ignored = false): KeyRow {
  return {
    vk,
    name: String.fromCharCode(vk),
    ignored,
    learned: true,
    gameFiltered: false,
    effectiveThresholdMs: vk,
    learning: {
      thresholdMs: 90,
      acceptedCount: vk,
      suppressedCount: 0,
      lastIntervalMs: 0,
      lastSeenUtc: null,
      lastAdjustmentMs: 0,
      lastAdjustmentReason: "",
      lastAdjustedUtc: null,
    },
  };
}
describe("按键列表", () => {
  it("非 VK 排序的升降序都把忽略项放在最后", () => {
    for (const ascending of [true, false]) {
      expect(
        filterAndSort(
          [row(66), row(65, true), row(67)],
          "",
          "all",
          "accepted",
          ascending,
        ).at(-1)?.vk,
      ).toBe(65);
    }
  });
  it("VK 排序不特殊分组，搜索同时支持名字和十进制", () => {
    expect(
      filterAndSort([row(66), row(65, true)], "", "all", "vk", true).map(
        (r) => r.vk,
      ),
    ).toEqual([65, 66]);
    expect(
      filterAndSort([row(65), row(66)], "a", "all", "vk", true).map(
        (r) => r.vk,
      ),
    ).toEqual([65]);
    expect(
      filterAndSort([row(65), row(66)], "66", "all", "vk", true).map(
        (r) => r.vk,
      ),
    ).toEqual([66]);
  });
  it("范围筛选使用后端规则", () => {
    const rows = [row(65), row(66, true)];
    expect(
      filterAndSort(rows, "", "ignored", "vk", true).map((r) => r.vk),
    ).toEqual([66]);
    expect(filterAndSort(rows, "", "game", "vk", true)).toHaveLength(0);
  });
});
describe("即时保存输入框", () => {
  it("调节滑块时，旧的保存回执不会拉回当前值", () => {
    const save = vi.fn();
    const { rerender } = render(
      <NumberSetting
        label="阈值"
        value={90}
        min={20}
        max={250}
        onSave={save}
      />,
    );
    const slider = screen.getByRole("slider");
    slider.focus();
    fireEvent.change(slider, { target: { value: "120" } });
    rerender(
      <NumberSetting
        label="阈值"
        value={95}
        min={20}
        max={250}
        onSave={save}
      />,
    );
    expect((slider as HTMLInputElement).value).toBe("120");
    expect((screen.getByRole("spinbutton") as HTMLInputElement).value).toBe(
      "120",
    );
    expect(document.activeElement).toBe(slider);
  });
  it("拒绝空值、越界及小数，不将中间输入写入后端", () => {
    const save = vi.fn();
    render(
      <NumberSetting
        label="阈值"
        value={90}
        min={20}
        max={250}
        onSave={save}
      />,
    );
    const input = screen.getByRole("spinbutton", { name: "阈值" });
    for (const value of ["", "9", "251", "20.5"])
      fireEvent.change(input, { target: { value } });
    expect(save).not.toHaveBeenCalled();
    fireEvent.change(input, { target: { value: "95" } });
    expect(save).toHaveBeenCalledWith(95);
  });
  it("后台刷新不会覆盖正在编辑的文本或焦点", () => {
    const save = vi.fn();
    const { rerender } = render(
      <NumberSetting
        label="阈值"
        value={90}
        min={20}
        max={250}
        onSave={save}
      />,
    );
    const input = screen.getByRole("spinbutton", {
      name: "阈值",
    }) as HTMLInputElement;
    input.focus();
    fireEvent.change(input, { target: { value: "12" } });
    rerender(
      <NumberSetting
        label="阈值"
        value={95}
        min={20}
        max={250}
        onSave={save}
      />,
    );
    expect(input.value).toBe("12");
    expect(document.activeElement).toBe(input);
  });
  it("滑块和数值框共享草稿且只提交有效值", () => {
    const save = vi.fn();
    render(
      <NumberSetting
        label="阈值"
        value={90}
        min={20}
        max={250}
        onSave={save}
      />,
    );
    fireEvent.change(screen.getByRole("slider"), { target: { value: "120" } });
    expect(save).toHaveBeenCalledWith(120);
    expect((screen.getByRole("spinbutton") as HTMLInputElement).value).toBe(
      "120",
    );
  });
});
