import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { useSyncExternalStore } from "react";
import type { Snapshot } from "./generated/Snapshot";
import type { SettingsPatch } from "./generated/SettingsPatch";

let snapshot: Snapshot | null = null;
const listeners = new Set<() => void>();
export function acceptSnapshot(next: Snapshot) {
  if (!snapshot || next.revision >= snapshot.revision) {
    snapshot = next;
    listeners.forEach((listener) => listener());
  }
}
export function useSnapshot() {
  return useSyncExternalStore(
    (callback) => {
      listeners.add(callback);
      return () => {
        listeners.delete(callback);
      };
    },
    () => snapshot,
  );
}
export async function connect() {
  const unlisten = await listen<Snapshot>("state-changed", (event) =>
    acceptSnapshot(event.payload),
  );
  try {
    acceptSnapshot(await invoke<Snapshot>("get_snapshot"));
  } catch (error) {
    unlisten();
    throw error;
  }
  return unlisten;
}
let queue: Promise<unknown> = Promise.resolve();
type CommandArguments = {
  get_snapshot: undefined;
  update_settings: { patch: Partial<SettingsPatch> };
  set_enabled: { enabled: boolean };
  apply_hotkey: { hotkey: string };
  set_startup: { enabled: boolean };
  change_games: { names: string[]; add: boolean };
  set_key_rule: { vk: number; ignored?: boolean; game?: boolean };
  clear_ignored: undefined;
  reset_learning: undefined;
  refresh_admin: undefined;
  refresh_games: undefined;
};
export function command<N extends keyof CommandArguments>(
  name: N,
  ...input: CommandArguments[N] extends undefined
    ? []
    : [args: CommandArguments[N]]
): Promise<Snapshot> {
  const request = queue.then(() => invoke<Snapshot>(name, input[0]));
  queue = request.catch(() => undefined);
  return request.then((next) => {
    acceptSnapshot(next);
    return next;
  });
}
export const patchSettings = (patch: Partial<SettingsPatch>) =>
  command("update_settings", { patch });
type QueryResults = {
  list_processes: string[];
  browse_executables: string[];
  open_config_directory: void;
};
export const query = <N extends keyof QueryResults>(name: N) =>
  invoke<QueryResults[N]>(name);
