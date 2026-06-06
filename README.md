# KeyboardDebounce

中文 | [English](#english)

## 中文

### 项目简介

KeyboardDebounce 是一个 Windows 用户态键盘防抖托盘程序，用于缓解笔记本自带键盘“单击触发多次”的问题。它不是键盘驱动。

### 下载

请从项目的 GitHub Releases 页面下载 `.exe` 或 `.zip`。发布产物应作为 GitHub Release assets 上传，不应提交进源码仓库。

### 运行

启动 `KeyboardDebounce-0.1.0-win-x64.exe`。默认会打开主界面，并出现在系统托盘。

* 托盘菜单：启用/暂停、显示主界面、退出。
* 暂停热键：`Ctrl+Alt+F12`。
* 默认启动后有 3 秒延迟，避免开机后立即影响输入。
* 默认不开机自启，可在主界面中开启。
* 勾选“静默运行”后，下次启动只进入托盘，不自动展示主界面。
* 程序、托盘和主界面标题栏使用自定义键盘盾牌图标；加载失败时回退系统盾牌。

### 构建

需要 .NET 8 SDK。

```powershell
dotnet restore
dotnet build -c Release
dotnet test .\tests\KeyboardDebounce.Tests\KeyboardDebounce.Tests.csproj -c Release
.\scripts\publish.ps1
```

发布脚本会在本地生成：

* `releases\KeyboardDebounce-0.1.0-win-x64.exe`
* `releases\KeyboardDebounce-0.1.0-win-x64.zip`

这些文件用于上传到 GitHub Releases，不应提交进源码仓库。

### 防抖策略

* 同一按键在短时间内重复触发会被拦截。
* 明显长按会放行，避免破坏退格、方向键等连续操作。
* 每个按键独立学习阈值。
* 主界面会显示实际运行阈值、放行/拦截次数和最近事件时间。
* 最近事件栏会显示本次事件的学习变化，例如 `learn=+5ms accepted-suspected-bounce`。
* 设置表格最后一列是“忽略”复选框；勾选后该键永远放行，不参与拦截和学习。
* 只有已出现过的键，或已经在忽略列表里的键，会出现在表格中。

### 隐私

程序不保存输入文本，也不保存文本序列。

用户配置保存：

* 忽略键列表
* 启用状态、开机自启、静默运行、全局敏感度、默认阈值、长按放行、启动延迟和暂停热键

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

主界面中的阈值会按全局敏感度换算后显示；基础学习阈值保存在学习状态文件中。

配置位置：

* `%APPDATA%\KeyboardDebounce\settings.json`
* `%APPDATA%\KeyboardDebounce\learning-state.json`

### 边界

* 这是用户态程序，不是驱动。
* 不覆盖 Windows 登录界面、UAC 安全桌面或所有管理员权限窗口。
* 默认对所有键盘全局生效，无法稳定只区分笔记本内置键盘。
* 忽略列表是按虚拟键码生效，不区分内置键盘和外接键盘。
* 表格点击列标题可排序；排序只影响显示，不影响防抖逻辑。
* 除 VK 列外，点击任意列排序时忽略项都会固定排在最后。
* 自动刷新只更新当前可见行的数值，不会改变排序、选中行或当前编辑焦点。
* 新按键出现后需要点击“刷新”才会加入表格。

---

## English

### Overview

KeyboardDebounce is a Windows user-mode tray app for reducing accidental rapid repeated key presses, such as laptop keyboard chatter. It is not a keyboard driver.

### Download

Download the `.exe` or `.zip` package from the project's GitHub Releases page. Release binaries are published as GitHub Release assets and are not meant to be committed to the source repository.

### Usage

* Start `KeyboardDebounce-0.1.0-win-x64.exe`.
* By default, the main window opens at startup and the app also appears in the system tray.
* Tray menu: enable/pause, show main window, exit.
* Pause hotkey: `Ctrl+Alt+F12`.
* Startup has a default 3-second delay before suppression starts.
* Start with Windows is disabled by default and can be enabled in the main window.
* Enable silent run if you want future launches to start only in the tray without showing the main window.

### Build

Requires the .NET 8 SDK.

```powershell
dotnet restore
dotnet build -c Release
dotnet test .\tests\KeyboardDebounce.Tests\KeyboardDebounce.Tests.csproj -c Release
.\scripts\publish.ps1
```

The publish script creates local release assets under `releases\`. Upload those files to GitHub Releases instead of committing them to the source repository.

### Debounce Strategy

* Repeated `KeyDown` events for the same key inside the effective threshold are suppressed.
* Long holds are allowed so Backspace, arrow keys, and normal key repeats remain usable.
* Each virtual key has its own learned threshold.
* The main window shows the effective threshold, accepted/suppressed counts, and last event time.
* Ignored keys are always allowed and do not participate in suppression or learning.

### Privacy

KeyboardDebounce does not save typed text or text sequences.

User settings are stored at `%APPDATA%\KeyboardDebounce\settings.json`. Automatic learning state is stored at `%APPDATA%\KeyboardDebounce\learning-state.json`.

Saved data is limited to settings, virtual key codes, thresholds, counters, timestamps, and learning reasons.

### Limitations

* This is a Windows user-mode app, not a driver.
* It does not cover the Windows sign-in screen, the UAC secure desktop, or all elevated administrator windows.
* Low-level keyboard hooks cannot reliably distinguish the built-in keyboard from an external keyboard, so rules apply globally.
