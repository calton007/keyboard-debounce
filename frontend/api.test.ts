import { beforeEach, expect, it, vi } from "vitest";
import { renderHook, act, cleanup } from "@testing-library/react";
import type { Snapshot } from "./generated/Snapshot";
const mocks = vi.hoisted(() => ({ invoke: vi.fn(), listen: vi.fn() }));
vi.mock("@tauri-apps/api/core", () => ({ invoke: mocks.invoke }));
vi.mock("@tauri-apps/api/event", () => ({ listen: mocks.listen }));
// Snapshot projection tests intentionally use only revision: no other field is read by the store.
function revision(value: number): Snapshot {
  return { revision: value } as Snapshot;
}
beforeEach(() => {
  cleanup();
  vi.resetModules();
  mocks.invoke.mockReset();
  mocks.listen.mockReset();
});
it("先订阅再获取快照，并拒绝倒序更新", async () => {
  const order: string[] = [];
  let notify: (e: { payload: Snapshot }) => void = () => {};
  mocks.listen.mockImplementation(async (_name, fn) => {
    order.push("listen");
    notify = fn;
    return vi.fn();
  });
  mocks.invoke.mockImplementation(async () => {
    order.push("get");
    notify({ payload: revision(5) });
    return revision(4);
  });
  const api = await import("./api");
  await api.connect();
  const { result } = renderHook(api.useSnapshot);
  expect(order).toEqual(["listen", "get"]);
  expect(result.current?.revision).toBe(5);
  act(() => api.acceptSnapshot(revision(3)));
  expect(result.current?.revision).toBe(5);
});
it("快速修改串行提交，失败不会堵塞后续修改", async () => {
  let finish: ((value: Snapshot) => void) | undefined;
  mocks.invoke
    .mockImplementationOnce(
      () =>
        new Promise<Snapshot>((resolve) => {
          finish = resolve;
        }),
    )
    .mockRejectedValueOnce("注册失败")
    .mockResolvedValueOnce(revision(3));
  const api = await import("./api");
  const first = api.command("update_settings", { patch: { silentRun: true } });
  const second = api
    .command("apply_hotkey", { hotkey: "Ctrl+Alt+F10" })
    .catch((e) => e);
  const third = api.command("set_enabled", { enabled: false });
  await Promise.resolve();
  expect(mocks.invoke).toHaveBeenCalledTimes(1);
  finish?.(revision(1));
  await first;
  expect(await second).toBe("注册失败");
  expect((await third).revision).toBe(3);
  expect(mocks.invoke.mock.calls.map((call) => call[0])).toEqual([
    "update_settings",
    "apply_hotkey",
    "set_enabled",
  ]);
});
it("初始化失败会释放订阅", async () => {
  const unlisten = vi.fn();
  mocks.listen.mockResolvedValue(unlisten);
  mocks.invoke.mockRejectedValue("核心退出");
  const api = await import("./api");
  await expect(api.connect()).rejects.toBe("核心退出");
  expect(unlisten).toHaveBeenCalledOnce();
});
