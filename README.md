# KeyboardDebounce

中文 | [English](#english)

Windows 用户态键盘防抖托盘程序，用于缓解键盘单击重复触发。采用 **Tauri 2 + React + TypeScript + Rust + Windows API**，不使用键盘驱动。

0.3.0 当前为待实机验收构建。自动回归和本地打包已完成；窗口生命周期、物理键盘及安装/卸载的剩余检查见 [验证记录](docs/TAURI-VALIDATION.md)。旧 C# 工程和旧测试已移除，历史实现可从 Git 历史查阅。

## 下载与运行

Windows 10 2004（19041）及以上 / Windows 11，x64。

| 产物 | 使用方式 |
|---|---|
| `KeyboardDebounce-0.3.0-win-x64-setup.exe` | 当前用户安装包；缺少 WebView2 时联网补齐 |
| `KeyboardDebounce-0.3.0-win-x64.exe` | 便携程序；需要机器已有 Microsoft Edge WebView2 Runtime |
| `KeyboardDebounce-0.3.0-win-x64.zip` | 便携程序、README、设计与验证记录、LICENSE 和图标 |
| `KeyboardDebounce-0.3.0-win-x64.sha256` | 上述三个文件的 SHA256 |

新版无需 .NET Desktop Runtime。便携版缺少 WebView2 时会明确提示，不能把便携 EXE 视为完全离线自包含浏览器的包。

启动默认打开主界面并进入托盘。关闭主窗口会销毁 WebView，防抖、热键、保存和游戏检测继续运行；从托盘重新打开时回到概览。只有托盘“退出”结束程序。“启动时仅显示托盘”使下次启动不创建 WebView。

- 托盘：显示主界面、启用 / 暂停、退出。
- 默认暂停热键：`Ctrl+Alt+F11`。注册失败时不能启用；修改失败保留已有绑定。
- 默认启动延迟 3 秒，普通阈值 90ms、长按 500ms，全局敏感度 1.0。
- 默认关闭开机自启和游戏自动切换。自启使用当前用户注册表项，校验实际可执行文件路径。
- 同一用户会话只能运行一个实例，新旧版也不能同时运行。

## 设置界面

顶部五页：概览、普通防抖、游戏模式、按键管理、应用设置。固定浅色主题，原生标题栏。支持方向键和 `Alt+1` 至 `Alt+5` 切页。

界面采用紧凑布局，仅显示控件标签、数值、状态及必要的错误和确认信息。概览不重复展示参数和最近事件；普通防抖按行调整；游戏与按键列表使用独立滚动区域。参数原理、隐私和权限边界统一在本文说明。

除热键必须点击“应用热键”之外，设置更改即时生效并后台保存。保存失败持续提示；“已生效”和“已保存”分别表示运行态与磁盘状态。

- 游戏模式：从磁盘批量选择 EXE，或从当前运行程序搜索、多选添加。选择不会执行文件，只保存不含路径和 `.exe` 后缀的规范化文件名。首次添加开启自动切换。
- 任一名单程序运行时进入游戏模式，每秒检测一次。仅“游戏防抖”名单中的故障键继续过滤，其余键直通；名单为空时全部直通。
- 游戏阈值是普通有效阈值和游戏阈值上限中的较小值，默认上限 45ms、长按 250ms。学习、计数和最近事件冻结。
- 检测失败显示未知及原始错误，并保留最后确认的模式；后续成功检测恢复。手动刷新使用同一串行检测队列。
- 按键管理：按十进制 VK 或名称搜索，筛选已学习、游戏防抖或始终忽略；支持手动添加 VK、排序、清空忽略和重置学习。
- 非 VK 排序时忽略项固定在最后。后台刷新更新已有行数值，不重排、不抢焦点；新出现的按键通过“刷新按键列表”加入。

## 防抖行为

运行时间线按 VK + 扫描码 + Extended 区分，学习和规则按 VK 保存。每次物理 KeyDown 重置静默窗口，持续抖动不会周期漏放。被拦截且未交付的 KeyDown，其 KeyUp 成对拦截，跨暂停、忽略和模式切换保留配对。已交付的长按在切换期间保持直通直到释放。

修饰键和软件注入事件（包括 SendInput）直通，不学习。暂停与启动延迟不更新学习数据。明显长按放行，保留退格和方向键的连续输入。

只有普通模式中，物理释放后的连续两次贴边拦截才上调：间隔位于有效阈值最后 5ms，目标为 `ceil((间隔 + 5ms) / 敏感度)`，上限 `min(250ms, 默认阈值 + 40ms)`。放行事件不会上调。高于默认值的阈值在连续 12 次稳定放行后下降 5ms，最低回到默认值。有效阈值沿用原版的 ties-to-even 舍入。

## 数据与隐私

新版从默认值开始，不导入或修改旧目录 `%APPDATA%\KeyboardDebounce`。

- `%APPDATA%\KeyboardDebounceTauri\settings.json`
- `%APPDATA%\KeyboardDebounceTauri\learning-state.json`

两个文件均为 Schema 1、camelCase JSON；时间使用 UTC ISO 8601。只保存配置、VK 阈值、统计和最近调整信息，不保存输入文本或按键序列。

同目录临时文件原子替换，保留上一次有效 `.bak`。损坏文件先复制为 `.corrupt-*` 隔离文件，再尝试恢复；设置无法恢复时停用防抖，学习无法恢复时清空学习并报告错误。普通学习统计每 5 秒合并保存，阈值调整立即请求保存；失败按 1–60 秒指数退避重试。退出会最后保存，失败用原生提示明确报告。

## 开发与验证

需要 Node.js 22 LTS、Rust MSVC 工具链（rustfmt、clippy）、Visual Studio C++ Build Tools、Windows SDK 和 WebView2。

```powershell
npm ci
npm.cmd run tauri -- dev
.\scripts\verify.ps1
.\scripts\publish.ps1
```

PowerShell 中使用 `npm.cmd` 向 Tauri 转发参数。脚本会检查 `%USERPROFILE%\.cargo\bin`，无需手动修改全局 PATH。

`verify.ps1` 运行 Rust 格式检查、Clippy、Rust 测试、生成的 TypeScript 类型检查、前端交互测试、前端构建及发布事务测试。Rust 回归包含原 C# 引擎生成的 5,400 条合成事件，逐项比对拦截决策与学习统计；不会采集真实用户的输入序列。

新版测试和合成基准保留在源码仓库供 CI 回归使用，不随安装包或便携 ZIP 分发。

`publish.ps1` 先验证并构建，再在 `dist\release-staging` 校验 EXE、ZIP、安装包及哈希，最后以可回滚事务更新 `releases`。文件占用时保留原产物，不结束占用进程。版本仅取自 `src-tauri/Cargo.toml`。发布二进制不提交到仓库。

```powershell
Get-Content .\releases\KeyboardDebounce-0.3.0-win-x64.sha256
Get-FileHash .\releases\KeyboardDebounce-0.3.0-win-x64.exe -Algorithm SHA256
```

## 边界

不覆盖登录界面、UAC 安全桌面及所有管理员窗口。低层钩子无法可靠区分内置和外接键盘，规则对所有键盘全局生效。游戏按 EXE 文件名匹配，同名但不同路径的程序也会命中；不自动识别游戏。硬件防抖验证需要真实物理键盘，软件注入测试只能验证直通行为。

## English

KeyboardDebounce is a Windows x64 user-mode tray utility for keyboard chatter, rebuilt with Tauri 2, React, TypeScript, Rust and Windows APIs. It requires Windows 10 2004+ or Windows 11 and Microsoft Edge WebView2, but no .NET runtime.

Use the per-user NSIS installer to provision WebView2, or the portable EXE/ZIP on a machine with WebView2 already installed. Closing the settings window destroys the WebView while the Rust core remains active. Reopen from the tray; use the tray Exit command to stop. Silent startup creates no WebView.

The default pause hotkey is `Ctrl+Alt+F11`; startup delay is 3 seconds. Normal threshold is 90ms, long hold 500ms. Game mode uses an explicit EXE filename list and filters only selected faulty keys while freezing ordinary learning.

The new version starts fresh in `%APPDATA%\KeyboardDebounceTauri`, without importing or modifying the old configuration directory. JSON files contain settings and per-key statistics, never typed text or input sequences. Atomic writes preserve backups; failures remain visible and retry automatically.

Build and verify using the PowerShell commands above. `scripts/verify.ps1` includes 5,400 deterministic decisions captured from the original C# filter, Rust tests, frontend tests and publication rollback tests. Release artifacts include an installer, portable EXE, ZIP and SHA256 file. See [docs/DESIGN.md](docs/DESIGN.md) for module ownership and runtime contracts.
