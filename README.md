# KeyboardDebounce

中文 | [English](#english)

## 中文

### 项目简介

KeyboardDebounce 是一个 Windows 用户态键盘防抖托盘程序，用于缓解笔记本自带键盘“单击触发多次”的问题。它不是键盘驱动。

### 下载

请从项目的 GitHub Releases 页面下载 `.exe` 或 `.zip`。`win-x64` 的 EXE 是自包含单文件，无需另装 .NET Desktop Runtime。发布产物应作为 GitHub Release assets 上传，不应提交进源码仓库。

### 运行

启动 `KeyboardDebounce-0.2.0-win-x64.exe`。默认会打开主界面，并出现在系统托盘。

* 托盘菜单：启用/暂停、显示主界面、退出。
* 暂停热键：`Ctrl+Alt+F11`。
* 默认启动后有 3 秒延迟，避免开机后立即影响输入。
* 默认不开机自启，可在主界面中开启。
* 勾选“静默运行”后，下次启动只进入托盘，不自动展示主界面。
* 在“游戏模式”页可“浏览选择 EXE”，或“从正在运行的程序添加”；任一已添加的 EXE 运行时自动进入游戏低干预模式，全部退出后恢复普通模式。托盘每秒检测一次，托盘、概览和游戏页投影同一份运行态；程序不会自动判断哪些是游戏。
* 游戏模式只对表格中勾选“游戏防抖”的故障键继续过滤；列表为空时所有普通键直通。该模式使用独立阈值上限和长按时间，并冻结学习数据。
* 启动延迟和暂停热键可在主界面修改；点击“应用热键”后尝试注册，失败时保留原热键。
* 程序、托盘和主界面标题栏使用自定义键盘盾牌图标；加载失败时回退系统盾牌。

### 设置界面

设置窗口使用 WinUI 3 实现，重做为顶部导航结构，分为五页：概览、普通防抖、游戏模式、按键管理、应用设置。页面通过顶部 Tab 风格导航、方向键或 `Alt+1` 至 `Alt+5` 切换；全局防抖开关始终位于右上角，最近事件显示在概览页，保存失败会在当前页面明确提示。除暂停热键需点击“应用热键”外，其余设置仍保持即时保存。

界面视觉采用 WinUI 3 方案 3 的固定浅色基准，统一字距、卡片、按钮和焦点层级；不提供深色主题切换。五页只保留清晰的页面标题，冗余的页面级副标题已移除，卡片和控件内的功能说明继续保留。

“游戏模式”使用双栏控制中心：左侧显示实时状态和可同步调节参数，右侧管理游戏 EXE。文件选择和运行中程序选择都支持批量添加，首次添加会自动开启模式；界面不要求手工输入进程名。配置只保存规范化后的 EXE 文件名，不保存或启动所选文件。手动刷新与每秒自动检测共用同一检测队列；运行集合或检测健康状态变化后，当前页由事件立即刷新，隐藏页在再次激活时补刷。检测失败会明确显示“状态未知”，并保持上一次已确认的实际模式。

“按键管理”支持按十进制 VK 或按键名称搜索，并可筛选全部、仅已学习、仅游戏防抖和仅始终忽略。搜索、筛选、排序、选中行和编辑焦点会在自动刷新期间保留；按键列表会随窗口剩余高度扩展，在默认 `1160×760` 窗口至少完整显示 5 行，在 `960×640` 窗口至少完整显示 3 行，并使用列表自身滚动。

五个页面在窗口创建时完成一次预热，之后切换只复用既有页面并更新当前页状态，不会重建全部页面内容；缓存集合保持筛选、排序与焦点状态，隐藏页依据运行态版本在切回时补刷。程序面向 Windows 10/11，并启用 Per-Monitor V2 DPI 适配。

### 构建

需要 .NET 8 SDK。

```powershell
.\scripts\verify.ps1
.\scripts\publish.ps1
```

`verify.ps1` 会统一运行还原、Release 构建、xUnit 测试和发布脚本契约测试。Windows CI 也调用同一入口。

发布脚本先在 `dist\release-staging` 完整生成并校验产物，随后以可回滚事务更新：

* `releases\KeyboardDebounce-0.2.0-win-x64.exe`
* `releases\KeyboardDebounce-0.2.0-win-x64.zip`
* `releases\KeyboardDebounce-0.2.0-win-x64.sha256`

版本号只读取 `KeyboardDebounce.csproj` 中的 `<Version>`。EXE 是 `win-x64` 自包含单文件；ZIP 还包含 README、LICENSE 和图标。`.sha256` 同时记录 EXE 与 ZIP 的 SHA256：

```powershell
Get-Content .\releases\KeyboardDebounce-0.2.0-win-x64.sha256
Get-FileHash .\releases\KeyboardDebounce-0.2.0-win-x64.exe -Algorithm SHA256
Get-FileHash .\releases\KeyboardDebounce-0.2.0-win-x64.zip -Algorithm SHA256
```

这些文件用于上传到 GitHub Releases，不应提交进源码仓库。
如果同版本 EXE 或其他目标产物正在运行、被占用或不可替换，发布会保留原有三件套并报告具体路径；脚本不会结束占用进程。

### 防抖策略

* 同一按键在短时间内重复触发会被拦截；每次观察到的 `KeyDown` 都会重新计算静默窗口，持续抖动不会周期漏放。
* 若某个 `KeyDown` 被拦截且系统从未见到该键按下，对应的 `KeyUp` 也会成对拦截；待配对释放会跨暂停和忽略切换保留，避免产生孤立释放事件。
* 明显长按会放行，避免破坏退格、方向键等连续操作。
* `Ctrl`、`Shift`、`Alt`、`Win` 等修饰键始终直通，避免改变组合键语义；注入输入（如屏幕键盘、密码管理器和 `SendInput`）也不参与防抖或学习。
* 暂停和启动延迟期间完全旁路引擎，不更新计数或学习状态。
* 每个按键独立学习阈值。已放行事件永不提高阈值；只有普通模式下、物理键已经释放、且拦截间隔位于有效阈值最后 `5ms` 内的事件可作为上调证据，连续两次后按 `ceil((间隔 + 5ms) / 全局敏感度)` 调整基础阈值。
* 自动上调不超过 `min(250ms, 默认阈值 + 40ms)`；相同节奏达到目标后不会继续累加。任意放行、长按、非贴边拦截或模式切换都会清空短期上调证据。
* 只有基础阈值高于默认值时才允许自动下降；连续 12 次放行间隔达到 `max(300ms, 2 × 有效阈值)` 后下降 `5ms`，最低回到默认阈值，不会继续降低。
* 运行态按 `扫描码 + Extended 标志 + 虚拟键码` 区分物理键，避免主键区和扩展键共享同一防抖时间线；为兼容已有配置，学习阈值和键列表仍按虚拟键码保存。
* 游戏 EXE 模式使用普通有效阈值与“游戏阈值上限”中的较小值，学习、计数和最近时间均保持冻结，不污染普通模式数据。
* 主界面会显示实际运行阈值、放行/拦截次数和最近事件时间。
* 最近事件栏会显示本次事件的学习变化及原因，便于确认上调、稳定回落或迁移是否发生。
* 设置表格最后一列是“忽略”复选框；勾选后该键永远放行，不参与拦截和学习。
* 已出现过的键，以及已加入“游戏防抖”或“始终忽略”列表的键，会出现在表格中。

### 隐私

程序不保存输入文本，也不保存文本序列。

用户配置保存：

* 忽略键列表
* 启用状态、开机自启、静默运行、全局敏感度、默认阈值、长按放行、启动延迟和暂停热键
* 游戏 EXE 自动切换开关、规范化 EXE 文件名名单、游戏阈值上限、游戏长按放行和游戏防抖键列表

学习状态保存：

* 虚拟键码
* 基础学习阈值
* 放行次数
* 拦截次数
* 最近事件间隔
* 最近事件时间
* 最近阈值调整
* 最近阈值调整原因
* 最近阈值调整时间
* 学习状态结构版本

主界面中的阈值会按全局敏感度换算后显示；基础学习阈值保存在学习状态文件中。

配置位置：

* `%APPDATA%\KeyboardDebounce\settings.json`
* `%APPDATA%\KeyboardDebounce\learning-state.json`

程序会保留同目录 `.bak` 作为上一次有效备份。JSON 损坏时原内容会复制为带 `.corrupt-时间戳` 后缀的隔离文件；设置无法恢复时防抖保持停用，学习状态无法恢复时只重置学习数据。首次加载版本 2 以前的学习文件时，高于当前默认值的阈值会降回默认值，较低阈值和累计统计保留；写回前一次性创建且不覆盖 `learning-state.pre-v2.json`。备份或写回失败时继续使用内存中的修复值，并按现有退避机制重试。

普通学习统计会按 5 秒窗口合并为后台检查点，阈值调整会立即保存。瞬时保存失败会自动退避重试，错误在恢复前持续显示在当前设置页面。

### 边界

* 这是用户态程序，不是驱动。
* 不覆盖 Windows 登录界面、UAC 安全桌面或所有管理员权限窗口。
* 默认对所有键盘全局生效，无法稳定只区分笔记本内置键盘。
* 忽略列表是按虚拟键码生效，不区分内置键盘和外接键盘。
* 修饰键和软件注入输入按设计直通，因此不会针对这些输入执行防抖。
* 同一用户会话只允许运行一个实例。
* 游戏模式按规范化后的 EXE 文件名匹配，不读取窗口标题、安装目录或游戏类型；同名 EXE 即使位于不同目录也会命中。检测是周期性的，EXE 启动或退出后可能需要约 1 秒切换。
* 表格点击列标题可排序；排序只影响显示，不影响防抖逻辑。
* 除 VK 列外，点击任意列排序时忽略项都会固定排在最后。
* 自动刷新只更新全局开关、错误提示和当前页面动态值，不会改变搜索、筛选、排序、选中行或当前编辑焦点。
* 新按键出现后可在“按键管理”页点击“刷新”加入表格。

---

## English

### Overview

KeyboardDebounce is a Windows user-mode tray app for reducing accidental rapid repeated key presses, such as laptop keyboard chatter. It is not a keyboard driver.

### Download

Download the `.exe` or `.zip` package from the project's GitHub Releases page. The `win-x64` EXE is a self-contained single file and does not require a separate .NET Desktop Runtime installation. Release binaries are published as GitHub Release assets and are not meant to be committed to the source repository.

### Usage

* Start `KeyboardDebounce-0.2.0-win-x64.exe`.
* By default, the main window opens at startup and the app also appears in the system tray.
* Tray menu: enable/pause, show main window, exit.
* Pause hotkey: `Ctrl+Alt+F11`.
* Startup has a default 3-second delay before suppression starts.
* Start with Windows is disabled by default and can be enabled in the main window.
* Enable silent run if you want future launches to start only in the tray without showing the main window.
* Add one or more executable files from disk, or choose them from currently running programs. When any listed EXE is running, the app enters game mode; it never tries to identify games automatically.
* In game mode, only keys checked under "Game debounce" remain filtered. If that list is empty, all ordinary keys pass through. Game mode has its own threshold cap and long-hold setting, and freezes learning data.
* The startup delay and pause hotkey are editable. Applying a new hotkey attempts registration immediately; if it fails, the previous binding remains active.

### Settings UI

The settings window is rewritten in WinUI 3 with top navigation and five pages: Overview, Normal debounce, Game mode, Key management, and Application settings. Use top tabs, arrow keys, or `Alt+1` through `Alt+5`. The global enable switch remains at the upper right, recent activity lives on Overview, and save failures surface on the current page. Redundant page-level subtitles are removed while card and control guidance remains. Settings still save immediately except for the pause hotkey, which uses its explicit Apply button.

Game mode uses a two-column control center: live status and synchronized slider/value controls on the left, and EXE management on the right. Disk selection and running-program selection both support batch additions. The first addition enables automatic switching. Only normalized EXE filenames are saved; selected files are never launched and paths are not persisted.

Key management can search by decimal virtual-key code or key name and filter all, learned, game-filtered, or always-ignored keys. Refreshes preserve search, scope, sorting, selection, and editing focus. Its list fills the remaining height, showing at least five complete rows at `1160×760` and three at `960×640`, with its own vertical scrolling. The WinUI 3 design uses the fixed Scheme 3 light baseline with no dark-mode switch and supports Per-Monitor V2 DPI on Windows 10/11.

### Build

Requires the .NET 8 SDK.

```powershell
.\scripts\verify.ps1
.\scripts\publish.ps1
```

`verify.ps1` is the single local and Windows CI entry point for restore, Release build, xUnit tests, and publish-script contract tests.

The publish script first generates and validates the complete release in `dist\release-staging`, then transactionally updates:

* `releases\KeyboardDebounce-0.2.0-win-x64.exe`
* `releases\KeyboardDebounce-0.2.0-win-x64.zip`
* `releases\KeyboardDebounce-0.2.0-win-x64.sha256`

The version is read only from `<Version>` in `KeyboardDebounce.csproj`. The EXE is a self-contained `win-x64` single file. The ZIP also contains the README, LICENSE, and icon. The `.sha256` file covers both the EXE and ZIP; compare it with `Get-FileHash -Algorithm SHA256`. Upload all three release assets to GitHub Releases instead of committing them to the source repository.

If an existing same-version EXE or another target artifact is running, locked, or cannot be replaced, publishing reports the exact path and preserves the previous three-file release. The script never terminates the owning process.

### Debounce Strategy

* Repeated `KeyDown` events for the same key inside the effective threshold are suppressed. Every observed `KeyDown` restarts the silence window, so sustained chatter cannot leak periodically.
* When a suppressed `KeyDown` was never delivered to Windows, its matching `KeyUp` is suppressed as well—even across pause or ignore transitions—to keep the delivered event stream balanced.
* Long holds are allowed so Backspace, arrow keys, and normal key repeats remain usable.
* Modifier keys (`Ctrl`, `Shift`, `Alt`, and `Win`) always pass through to preserve shortcut semantics. Injected input from tools such as on-screen keyboards, password managers, and `SendInput` also bypasses suppression and learning.
* Pause mode and the startup delay bypass the engine completely, so they do not change counters or learned thresholds.
* Each virtual key has its own learned threshold.
* Runtime timelines distinguish physical identity with virtual key, scan code, and the extended-key flag. Persisted learning and configured key lists remain keyed by virtual key for compatibility.
* EXE-triggered game mode uses the smaller of the normal effective threshold and the game-mode cap. Learning, counts, and recent-event timestamps are frozen while it is active.
* The main window shows the effective threshold, accepted/suppressed counts, and last event time.
* Ignored keys are always allowed and do not participate in suppression or learning.

### Privacy

KeyboardDebounce does not save typed text or text sequences.

User settings are stored at `%APPDATA%\KeyboardDebounce\settings.json`. Automatic learning state is stored at `%APPDATA%\KeyboardDebounce\learning-state.json`.

Saved data is limited to settings (including normalized game EXE filenames and the game-mode key list), virtual key codes, thresholds, counters, timestamps, and learning reasons.

The app keeps a `.bak` file beside each JSON file. Corrupt JSON is copied to a timestamped `.corrupt-*` quarantine file. If settings cannot be recovered, suppression stays disabled; unrecoverable learning data is reset without enabling or disabling the app.

Ordinary learning statistics are coalesced into background checkpoints every five seconds, while threshold changes save immediately. Transient save failures retry with bounded backoff and remain visible on the current settings page until recovery.

### Limitations

* This is a Windows user-mode app, not a driver.
* It does not cover the Windows sign-in screen, the UAC secure desktop, or all elevated administrator windows.
* Low-level keyboard hooks cannot reliably distinguish the built-in keyboard from an external keyboard, so rules apply globally.
* Modifier keys and injected software input intentionally bypass debouncing.
* Only one instance can run per user session.
* Game mode matches normalized EXE filenames only. It does not inspect window titles, installation paths, or application type. An identically named EXE in another directory also matches, and startup or exit can take about one second to be reflected.
