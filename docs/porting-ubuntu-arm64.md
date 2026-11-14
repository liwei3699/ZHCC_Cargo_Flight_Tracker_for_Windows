# 跨平台移植可行性研究：Ubuntu arm64（RK3588）

> 目标平台：Ubuntu 24.04 LTS arm64，Rockchip RK3588（4×Cortex-A76 + 4×Cortex-A55，Mali-G610）
> 研究对象：本仓库的 Windows 桌面版（.NET 10 + WPF），版本 v1.0.1
> 结论日期：2026-11-14

---

## 一、结论摘要

| 问题 | 结论 |
|---|---|
| 能不能移植？ | **能，且技术风险可控**。Core 层（抓取、存储、导出）已是跨平台 .NET 代码，**零改动**即可在 Linux 上运行（本文已实测）。 |
| 最大工作量在哪？ | 只有 UI 层约 **1,950 行** 需要重写（WPF 只支持 Windows，无法在 Linux 运行）。 |
| 推荐技术路线 | **Avalonia 12 + .NET 10 自包含 linux-arm64 发布**，代码复用率约 55%，界面可保持现有浅色风格。 |
| 预计工作量 | **12～17 人日**（单人约 3～4 周），其中 2～3 天必须留给定板联调。 |
| 三个必须现场验证的点 | ① Mali-G610 上的渲染路径（GPU/软件回退）；② 中文输入法（IBus/Fcitx5）；③ 托盘图标（GNOME AppIndicator）。 |
| 备选方案 | 本地 Web UI（ASP.NET Core + 浏览器 kiosk），**10～14 人日**，渲染与输入最稳，但多一个 Chromium 依赖。 |
| 不建议 | Qt / PySide6 / Electron 重写：UI 全推倒，成本翻倍且要维护两套语言栈。 |

---

## 二、现状盘点

### 2.1 分层与可移植性

| 层 | 代码量（手写） | 目标框架 | 可移植性 |
|---|---|---|---|
| `src/ZhccCargoBoard.Core` | ~1,300 行 | `net10.0`（**已是跨平台 TFM**） | **直接复用**：HttpClient、SQLite、ClosedXML 都支持 linux-arm64 |
| `src/ZhccCargoBoard.App` | ~1,950 行 | `net10.0-windows`（WPF + WinForms） | **必须重写**：WPF 无 Linux 实现 |
| `tools/ZhccCargoBoard.Cli` | ~80 行 | `net10.0` | 直接复用（已在 Linux 跑通） |
| `tests/ZhccCargoBoard.Tests` | ~400 行 | `net10.0` | 直接复用（已在 Linux 跑通 12/12） |

### 2.2 Windows 专有依赖清单

| 依赖 | 位置 | Linux 处理方式 |
|---|---|---|
| WPF（XAML、DataGrid、样式、`DropShadowEffect`） | `MainWindow.xaml`、`App.xaml`、两个对话框 | 用 Avalonia XAML 重写（结构可参照，控件 API 不同） |
| `System.Windows.Forms.NotifyIcon` + `System.Drawing.Icon` | `Services/TrayService.cs` | Avalonia `TrayIcon`（Linux 走 DBus StatusNotifierItem），图标改用 PNG |
| `System.Windows.Threading.DispatcherTimer` | `MainViewModel` | `Avalonia.Threading.DispatcherTimer`（同名同用法） |
| `Microsoft.Win32.SaveFileDialog` | `ExportDialog` | Avalonia `StorageProvider.SaveFilePickerAsync` |
| `app.manifest`、PerMonitorV2 | 项目文件 | 删除；Avalonia 自带 DPI 缩放 |
| `%LOCALAPPDATA%`、`我的文档` | `AppPaths`、`ExportDialog` | 改用 `$XDG_DATA_HOME`（默认 `~/.local/share`）、`~/Documents` |
| 命名 Mutex 单实例 | `App.xaml.cs` | **可保留**：已实测 .NET 命名 Mutex 在 Linux 上跨进程有效（见 §3.5） |
| `.ico` 应用图标 | `Assets/app.ico` | Avalonia `WindowIcon` 支持 ico，同时补一张 PNG 给托盘 |
| 中文字体 Microsoft YaHei UI | 全局样式 | 换 `Noto Sans CJK SC`，并在安装包 `Depends: fonts-noto-cjk`（或内嵌字体） |

---

## 三、本机实测证据

> 说明：本机为 Windows + WSL2，可用 x64 版 Ubuntu 24.04 验证代码行为；arm64 部分通过**交叉发布 + ELF 头校验**证明工具链可用。以下均为本次实测，不是推测。

### 3.1 Core 在 Ubuntu 24.04 上跑通（x64，WSL）

```
$ ~/zhcc-verify/tests/ZhccCargoBoard.Tests
通过 12 项，失败 0 项。        # JSON 映射 / 时间解析 / 指纹 / SQLite 入库清理 / xlsx 格式
```

真实联网抓取（同一份 CLI 二进制，Linux 自包含发布）：

```
抓取完成：642 条 / 59 页 / 请求 59 次 / 耗时 2.2s
入库：新增 642，状态变化 0，未变化 0
```

→ SQLite 原生库、ClosedXML、HttpClient（含 IPv4 优先的 `ConnectCallback`）、文件 IO 在 Linux 上全部正常。

### 3.2 arm64 交叉发布可用

| 探针 | 结论 |
|---|---|
| Core + SQLite 自包含发布 `-r linux-arm64` | 96.0 MB，含 `libe_sqlite3.so`，ELF 头 `e_machine = 0xB7`（**AArch64**） |
| Avalonia 12.1.6 空窗口 + DataGrid | 106.2 MB，含 `libSkiaSharp.so`、`libHarfBuzzSharp.so`，均为 AArch64 |

```
7F 45 4C 46 02 01 01 03 00 00 00 00 00 00 00 00 03 00 B7 00   ← libSkiaSharp.so
```

→ 不需要在 RK3588 上装 SDK 也能出 arm64 产物；Windows 上一条 `dotnet publish` 即可，适合放 CI。

### 3.3 Avalonia 依赖链 arm64 齐全

- `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` **12.1.6**
- `Avalonia.Controls.DataGrid` **12.1.2**（DataGrid 是独立包）
- `Avalonia.Headless` **12.1.6**（无显示环境跑 UI 测试，可用于 CI）

### 3.4 干净 Ubuntu 的环境基线（重要）

在未装任何东西的 Ubuntu 24.04 上实测：

```
ZH_FONT_COUNT=0     # 一个中文字体都没有
NOTO_PKGS=0
DOTNET=none         # 没有 .NET 运行时
```

→ 必须 **自包含发布**（约 100～140 MB）并把中文字体作为安装依赖，否则界面全是方块。

### 3.5 单实例 Mutex 在 Linux 上可用

两个进程争抢同一命名 Mutex：第二个 `createdNew=False` —— 现有单实例逻辑可原样保留（作用域为当前用户会话）。

---

## 四、目标平台分析

### 4.1 硬件

| 项 | 规格 | 对本项目的影响 |
|---|---|---|
| CPU | 4×Cortex-A76 @2.4GHz + 4×Cortex-A55 | 636 行表格 + 30 分钟抓取，性能绰绰有余 |
| GPU | Mali-G610 MP4（Panfrost/Panthor 开源驱动，GLES 3.1） | Avalonia 可用 GL/EGL；**必要时回退软件渲染** |
| 内存 | 4～32 GB | 程序常驻约 250 MB，无压力 |
| 显示 | HDMI 2.1 / DSI / 触摸 | 建议 1080p；4K 软件渲染会吃力 |
| 存储 | eMMC / SD / NVMe | 程序约 140 MB，历史库 30 天约 50 MB |

### 4.2 系统与运行时

- Ubuntu 22.04（厂商内核 5.10）或 **24.04（内核 6.1）**，两版都有成熟板卡镜像（Rock 5B、Orange Pi 5、Firefly 等）。
- .NET 10 是 **LTS，支持到 2028-11-14**；Ubuntu 24.04 官方源/Backports 提供 arm64 包，但**我们仍建议自包含**，避免现场装运行时。
- 板卡厂商镜像的 GPU/内核差异较大 → 建议**锁定一个镜像版本**并写进运维手册。

### 4.3 图形栈与渲染

Avalonia 在 Linux 上默认 X11（Wayland 会话下走 XWayland），12.1 起提供**原生 Wayland 后端**。

渲染模式是一个按优先级尝试的列表，默认 `Glx → Software`；嵌入式建议显式配置：

```csharp
.With(new X11PlatformOptions {
    RenderingMode = [X11RenderingMode.Egl, X11RenderingMode.Software]
})
```

- `Egl` 走 Panfrost 的 GLES（RK3588 上比 GLX 更靠谱）；
- 保留 `Software` 兜底 —— 社区已有类似嵌入式板卡“只有软件渲染也能跑、只是首屏约 1 秒”的案例；对 600 行表格完全可接受。

### 4.4 会话与开机启动

| 场景 | 建议 |
|---|---|
| 值班室固定显示器 | X11 + lightdm 自动登录 + `.desktop` 自启，全屏 1080p |
| 触摸屏一体机 | 同上，加 `--kiosk`（窗口全屏、禁止最小化） |
| 无桌面（服务器式） | 方案 C（无界面服务）或方案 B（Web UI）+ Chromium kiosk |
| 无人值守 | systemd user service 常驻抓取，GUI 可选 |

---

## 五、方案对比

| 维度 | **A. Avalonia 移植（推荐）** | B. 本地 Web UI | C. 仅 CLI/服务 | D. Qt/PySide 重写 |
|---|---|---|---|---|
| 界面体验 | 与 Windows 版几乎一致 | 浏览器表格，样式需重做 | 无界面 | 可做得最好 |
| 复用 Core | **100%** | 100% | 100% | 需重写解析/导出（或用 gRPC 调 .NET 服务） |
| 新增依赖 | Avalonia ~100 MB 自包含 | + Chromium ~200 MB | 无 | Qt 运行时 |
| 渲染风险 | 中（GPU 回退可用） | **低**（Chromium 自带渲染） | 无 | 低 |
| 中文输入 | 需验证（IBus） | **好**（浏览器原生） | 无 | 好 |
| 托盘 | GNOME 需 AppIndicator | 不需要 | 不需要 | 需适配 |
| 触摸/DPI | 好 | 好 | — | 好 |
| 预计工作量 | **12～17 人日** | 10～14 人日 | 3～4 人日 | 25～35 人日 |
| 长期维护 | 一套 C# 代码 | 两套技术栈（C#+前端） | 一套 | 两套技术栈 |

**建议**：如果 RK3588 上跑的是带桌面的 Ubuntu → 选 **A**；如果是无桌面/定制系统、只要能看能导 → 选 **B**（把 Avalonia 换成 ASP.NET Core + 一个本地页面，浏览器 kiosk 全屏）。

---

## 六、推荐路线（方案 A）实施计划

### P0 · 技术验证（2～3 人日，必须在真板子上做）

| 事项 | 验收标准 |
|---|---|
| Avalonia 空窗口跑到 RK3588 上 | 全屏 1080p，无花屏；`Egl` 失败时自动落 `Software` |
| 中文字体 | 界面中文正常，无方块；字号/行高与 Windows 版接近 |
| 中文输入法 | IBus/Fcitx5 能在筛选框输入「浦东」并触发过滤 |
| DataGrid 600 行 | 滚动流畅（≥30fps），排序/筛选不卡 |
| 联网抓取 | 板子上跑 CLI 抓 600+ 条，≤10 秒 |

> P0 只要有一项不过，就切方案 B —— 三条路线中**只有 P0 的结果能决定去留**，所以先做。

### P1 · 功能对齐（6～8 人日）

1. 工程改造：`App` 拆成 `ZhccCargoBoard.Desktop`（Avalonia）+ 复用 `Core`；
2. `MainViewModel` 迁移：`DispatcherTimer` 换命名空间即可，其余（筛选/排序/计数/刷新状态）基本原样；
3. 主界面：标题栏、两行筛选区（WrapPanel）、统计条、DataGrid 8+3 列、状态栏；
4. 详情抽屉：`Border` + `BoxShadow` 替代 `DropShadowEffect`；
5. 导出对话框 + 设置对话框：`StorageProvider` 选路径，其余逻辑复用；
6. 样式：把 `App.xaml` 里的配色/按钮/输入框样式翻成 Avalonia 样式选择器（约 260 行）；
7. 数据目录：`$XDG_DATA_HOME/ZhccCargoBoard`，保留 `ZHCC_DATA_DIR` 与便携 `data/` 约定。

### P2 · 集成与发布（4～6 人日）

1. 托盘：Avalonia `TrayIcon`（Linux 走 DBus SNI）；GNOME 下若不可用，退化为「关闭=最小化到任务栏 + systemd 常驻」；
2. 开机自启：`.desktop` 或 systemd user unit（二选一，默认 systemd）；
3. 单实例：现有命名 Mutex 保留，加文件锁兜底；
4. 打包：`tar.gz`（免安装，解压即用）+ `.deb`（`Depends: fonts-noto-cjk`）；
5. 日志：`~/.local/share/ZhccCargoBoard/logs`，保留 14 天；
6. 发布脚本 `build-linux.sh`，CI 用 `ubuntu-24.04-arm` 跑 build + headless 测试；
7. 现场验收表 + 运维手册（镜像版本、字体、自启方式、故障排查）。

---

## 七、逐文件改造清单

| 文件 | 行数 | 处理 | 工作量 |
|---|---|---|---|
| `Core/**`（9 个文件） | ~1,300 | **不改** | 0 |
| `App/ViewModels/MainViewModel.cs` | 539 | 换 DispatcherTimer 命名空间；`ExportScope` 等逻辑保留 | 0.5 天 |
| `App/MainWindow.xaml` | 400 | Avalonia XAML 重写（DataGrid 列、筛选 WrapPanel、抽屉、空态、状态栏） | 2 天 |
| `App/App.xaml` | 269 | 样式/画刷/控件模板翻写（Avalonia 无 DataTrigger，用选择器与伪类） | 1.5 天 |
| `App/MainWindow.xaml.cs` | 89 | 事件处理保留，排序/关闭逻辑微调 | 0.5 天 |
| `App/Dialogs/ExportDialog.*` | 229 | 窗口重写 + StorageProvider | 1 天 |
| `App/Dialogs/SettingsDialog.*` | 124 | 同上 | 0.5 天 |
| `App/Services/TrayService.cs` | 125 | Avalonia TrayIcon + NativeMenu，PNG 图标 | 0.5 天 |
| `App/Converters/Converters.cs` | 66 | `IValueConverter` 签名与画刷类型调整；`IsVisible` 可直接绑 bool | 0.25 天 |
| `App/App.xaml.cs` | 116 | 启动流程、单实例、托盘菜单接线 | 0.5 天 |
| `AppPaths.cs` / `AppSettings.cs` | 168 | 加 XDG 路径分支（保留 Windows 分支） | 0.25 天 |
| 新增 `Desktop` 项目 + 打包脚本 | — | Avalonia 工程、CI、deb/tar | 2 天 |
| 真机联调与验收 | — | P0 + P2 现场 | 3 天 |

---

## 八、风险与对策

| # | 风险 | 影响 | 概率 | 对策 / 验证 |
|---|---|---|---|---|
| 1 | Mali-G610 上 `Glx` 初始化失败 | 自动落软件渲染，4K 下滚动卡 | 中 | 显式 `Egl → Software`；分辨率锁 1080p；P0 实测帧率 |
| 2 | GNOME 不显示托盘图标 | 关窗后找不到程序 | 中高 | 默认「关闭=最小化」，托盘失败时回退；装 `gnome-shell-extension-appindicator` |
| 3 | 中文输入法在 Avalonia X11 不工作 | 无法按中文站名筛选 | 中 | P0 必测「浦东」「乌鲁木齐」；不行则切方案 B |
| 4 | 系统无中文字体 | 界面方块 | **高（已实测 0 字体）** | `Depends: fonts-noto-cjk`，或内嵌 Noto Sans SC 子集 |
| 5 | 只读根文件系统 / 权限不足 | 无法建库写日志 | 中 | 数据写 `$XDG_DATA_HOME`；systemd `ReadWritePaths=` |
| 6 | 板卡镜像内核/驱动差异 | GPU 打不开、休眠异常 | 中 | 锁定一个厂商镜像版本；禁用休眠；写进运维手册 |
| 7 | 自包含体积 100～140 MB | 升级包偏大 | 低 | 用 `tar.zst` / deb 增量；可接受 |
| 8 | 现场网络（代理/内网） | 抓取失败 | 低 | 保留失败重试与日志；支持 `HTTP_PROXY` 环境变量（.NET 默认读） |
| 9 | 时区/locale 未设 | 时间显示不对 | 低 | 安装时设 `Asia/Shanghai`、`zh_CN.UTF-8` |
| 10 | 历史数据迁移 | 老数据丢 | 低 | 提供「导入 Windows 版 data.db」入口（SQLite 同构，直接拷贝） |

---

## 九、发布、运维与 CI

- **产物**：`ZhccCargoBoard-linux-arm64-vX.Y.Z.tar.gz`（解压即用）+ `zhcc-cargo-board_X.Y.Z_arm64.deb`。
- **自启**：`systemctl --user enable --now zhcc-cargo-board.service`；kiosk 场景再加 `lightdm` 自动登录。
- **日志**：`~/.local/share/ZhccCargoBoard/logs/app-YYYYMMDD.log`（与 Windows 版同格式，便于排查）。
- **CI**：GitHub 的 arm64 托管 runner（`ubuntu-24.04-arm`）已对**公共仓库免费 GA**，可跑 `dotnet build` + `Avalonia.Headless` 的 UI 冒烟 + Core 的 12 项自检，无需模拟器。
- **升级**：保留便携目录，解压覆盖 exe/程序目录即可；数据目录独立。

---

## 十、成本汇总

| 阶段 | 方案 A（Avalonia） | 方案 B（Web UI） | 方案 C（无界面） |
|---|---|---|---|
| P0 验证 | 2～3 天 | 1～2 天 | 0.5 天 |
| 功能实现 | 8～10 天 | 6～8 天 | 2 天 |
| 集成发布 | 4～6 天 | 3～4 天 | 1 天 |
| **合计** | **14～19 人日**（含真机联调） | **10～14 人日** | **3～4 人日** |

> 注：上表含现场联调；若 P0 的 GPU/输入法两项顺利，实际可压到 12～15 人日。

**结论**：技术上完全可行，Core 已经证明能在 Linux 上跑；真正的成本在 UI 重写（约 1,950 行）与真机适配。
**建议先用 2～3 天做 P0 验证**（Avalonia 空壳 + 中文字体 + 输入法 + DataGrid + 抓取跑在 RK3588 上），用结果决定是否投入 P1。

---

## 附录 A：本次实验的复现命令

```powershell
# 1) arm64 交叉发布（Windows 上即可）
dotnet publish tools\ZhccCargoBoard.Cli -c Release -r linux-arm64 --self-contained true -o out\linux-arm64
# 校验 ELF：e_machine = 0xB7 即 AArch64
# 2) Linux 上跑自检（本机用 WSL Ubuntu 24.04 验证）
dotnet publish tests\ZhccCargoBoard.Tests -c Release -r linux-x64 --self-contained true -o out\linux-x64\tests
# 3) Avalonia 探针
dotnet add package Avalonia --version 12.1.6
dotnet add package Avalonia.Controls.DataGrid --version 12.1.2
dotnet publish -c Release -r linux-arm64 --self-contained true
```

## 附录 B：设备到货后 90 分钟验证清单

1. `uname -m` = `aarch64`；`lsb_release -a` 记录镜像版本；
2. `fc-list :lang=zh | wc -l` ≥ 1（否则装 `fonts-noto-cjk`）；
3. 解压 arm64 产物，跑 CLI `fetch`：能抓到 600+ 条；
4. 跑 Avalonia 空壳：窗口出现、中文正常、拖窗不撕裂；
5. 用 IBus 输入「浦东」到 TextBox：能上屏；
6. DataGrid 灌 600 行：滚动/排序主观流畅；
7. `top` 看进程 CPU/内存：空闲 <5% / <400 MB；
8. 断网 → 程序能保留上次数据并提示；
9. 关闭窗口 → 托盘或最小化行为符合预期；
10. 重启板子 → 自启生效、数据未丢。
