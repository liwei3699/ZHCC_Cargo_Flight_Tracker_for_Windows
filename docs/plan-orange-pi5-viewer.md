# 板端方案：Orange Pi 5 航班看板（只读查看器）

> 目标设备：Orange Pi 5（RK3588S，4GB）/ Ubuntu 22.04 Jammy / XFCE / HDMI 1280x720
> 依据：`BOARD-STATE.md` v1.1 + 本次上板只读探测 + 七个需求的问答
> 状态：**方案阶段，未改动板子任何内容**

---

## 一、需求确认（七问七答）

| # | 问题 | 你的选择 |
|---|---|---|
| 1 | 运行定位 | **做成一个软件，需要时打开**（不常驻、不自启、不与 YOLO 抢屏） |
| 2 | 数据积累 | **只在打开软件时抓，关掉就停**（不做后台服务/定时任务） |
| 3 | 界面形态 | **简化版**：少列 + 弹窗看详情 |
| 4 | 中文输入 | **不需要**：站名/代理用下拉，航班号只打数字字母，时间用选择器 |
| 5 | 本次范围 | **只要方案，先不动板子** |
| 6 | 关闭行为 | **关闭 = 直接退出**（不做托盘） |
| 7 | 导出 | **不做导出功能** |

→ 这七条把板端定成一个**纯查看器**：抓取、浏览、筛选、排序、看详情；不导出、不常驻、不进托盘。

---

## 二、可行性与技术路线

**结论：可行，且比 Windows 版简单。** Core 层（抓取/存储）已在 Linux 实机验证，UI 用 Avalonia 重写为简化版。

| 项 | 选择 | 依据 |
|---|---|---|
| UI 框架 | **Avalonia 12.1.6** + `Avalonia.Controls.DataGrid` 12.1.2 | 交叉发布 arm64 已验证（SkiaSharp/HarfBuzz 均为 AArch64 ELF） |
| 运行时 | **.NET 10 自包含**（`-r linux-arm64`） | 板上**没有 .NET**，且 22.04 装 .NET 10 需要 backports PPA；自包含最省事 |
| 渲染 | `X11PlatformOptions.RenderingMode = [Egl, Software]` | 板上实测：**EGL/X11 可用**（`EGL 1.4 / ARM / Valhall-g13p0`），**GLX 不可用**（`failed to load driver: rockchip`） |
| 复用 | `ZhccCargoBoard.Core` **零改动** | 12 项自检 + 真实抓取已在 Linux 通过 |
| 新项目 | `src/ZhccCargoBoard.Desktop`（net10.0） | Windows 的 `App`（WPF）原样保留，两个前端共享 Core |

---

## 三、板子实测基线（本次只读探测）

| 项 | 实测值 |
|---|---|
| 架构 / 系统 | `aarch64` / Orange Pi 1.2.4 Jammy（Ubuntu 22.04），内核 `6.1.99-rockchip-rk3588` |
| 内存 / 磁盘 | 3.9 GB 总量，**可用 3.0 GB**；`/` 49 GB 可用 |
| 显示 | X11 `:0`，`HDMI-1 1280x720@60*`（驱动里只有 720p/1080p 档位） |
| EGL | X11 平台 **可用**：`EGL 1.4`、`vendor ARM`、`Valhall-"g13p0-01eac0"`、客户端 API = `OpenGL_ES`；GBM 平台 `eglInitialize` 失败 |
| GLX | **不可用**：`glxinfo` 报 `failed to load driver: rockchip`（与文档“只有 GLES、无桌面 OpenGL”一致） |
| 网络到数据源 | `https://www.zzcargo.com/airline/1` → **HTTP 200，0.95 s**（板子能直连，无需代理） |
| 中文环境 | `LANG=zh_CN.UTF-8`，**中文字体 94 个**，`fcitx5` 已装（本方案不需要输入法） |
| .NET | **未安装**（所以必须自包含发布） |

> 结论：网络、字体、显示都没问题；唯一需要工程处理的是 **强制走 EGL + 软件兜底**，不能依赖 Avalonia 默认的 GLX。

---

## 四、相对 Windows 版的功能取舍

| 功能 | 板端 | 说明 |
|---|---|---|
| 全量抓取（58 页 / 600+ 条） | ✅ 保留 | 复用 `ZzcargoClient`，含 IPv4 优先逻辑 |
| 本地累积（SQLite） | ✅ 保留 | 仅在程序运行期间累积；关闭即停（已确认） |
| 30 分钟自动刷新 + 手动刷新 | ✅ 保留 | 运行期间生效 |
| 列头排序 | ✅ 保留 | |
| 筛选 | ✅ 保留（改为下拉） | 航班号 / 进出港 / 起飞机场 / 降落机场 / 状态 / 日期快捷 |
| 详情 | ✅ 改为**弹窗** | 720p 放不下右侧抽屉 |
| 导出 xlsx | ❌ **不做** | Core 里的导出代码保留给 Windows 版，板端不调用 |
| 托盘图标 | ❌ 不做 | 关闭即退出 |
| 设置对话框 | ❌ 不做（或只留“关于”） | 刷新间隔固定 30 分钟，保留天数固定 30 天 |
| 开机自启 / kiosk | ❌ 不做 | 手动从桌面图标打开 |
| 单实例 | ✅ 保留 | 命名 Mutex（已实测 Linux 跨进程有效），重复打开时激活已有窗口 |

---

## 五、界面设计（1280x720）

```
┌──────────────────────────────────────────────────────────────┐
│ 货机航班动态            更新 14:32 · 下次刷新 12:18  [立即刷新] │
├──────────────────────────────────────────────────────────────┤
│ 航班号[____] 进出港[全部▾] 起飞机场[全部▾] 降落机场[全部▾]   │
│ 状态[全部▾] 日期[今天▾]                          [重置]        │
├──────────────────────────────────────────────────────────────┤
│ 航班      起降机场            计划起飞 实际起飞 计划降落 实际降落 状态 │
│ CZ6376   乌鲁木齐 → 郑州       21:00   21:12   00:50   —      到达 │
│ …（一屏约 14~16 行，行高 32）                                     │
├──────────────────────────────────────────────────────────────┤
│ 共 642 条 · 显示 128 条 · 数据截至 14:32:05                    │
└──────────────────────────────────────────────────────────────┘
```

- 启动即最大化（**去掉系统标题栏**，用自绘标题区，省 30px）；
- 8 列，总宽约 1180px，720p 下不出现横向滚动；
- **双击行 → 详情弹窗**（全字段：机型/机号/代理/机位/航站楼/任务/异常/首次记录等）；
- 刷新失败时顶部红色横幅（保留旧数据）；
- 空态提示“暂无数据，点立即刷新”。

字号/行高按 720p 调一档（正文 13px、行高 32px），触摸不友好但鼠标操作足够。

---

## 六、技术实现要点

1. **新项目** `src/ZhccCargoBoard.Desktop`：
   ```
   ZhccCargoBoard.Desktop/
     Program.cs          # AppBuilder + X11PlatformOptions(Egl→Software)
     App.axaml(.cs)
     Views/MainWindow.axaml(.cs)
     Views/DetailWindow.axaml(.cs)
     ViewModels/MainViewModel.cs     # 从 WPF 版移植，DispatcherTimer 换命名空间
     Converters/*.cs                 # IValueConverter 签名适配
     Assets/app.png                  # 桌面/窗口图标（Linux 用 PNG）
   ```
2. **渲染配置**：
   ```csharp
   AppBuilder.Configure<App>()
     .UsePlatformDetect()
     .With(new X11PlatformOptions {
         RenderingMode = [X11RenderingMode.Egl, X11RenderingMode.Software]
     });
   ```
   若 Egl 初始化失败会自动落 Software —— 720p 表格完全够用，只是首屏多几百毫秒。
3. **字体**：`FontFamily("Noto Sans CJK SC")`（板上已有 94 个中文字体，先 `fc-match` 确认实际可用名）。
4. **数据目录**：`$XDG_DATA_HOME/ZhccCargoBoard`（默认 `~/.local/share/ZhccCargoBoard`），保留 `ZHCC_DATA_DIR` 覆盖；与 Windows 的 `%LOCALAPPDATA%` 分支共存于同一份 `AppPaths`。
5. **抓取/存储**：直接引用 Core，不复制代码；`FlightStore` 用 arm64 的 `libe_sqlite3.so`（已验证）。
6. **无导出**：板端不实例化 `XlsxExporter`，也不引用导出对话框；Core 里保留（Windows 版继续用）。
7. **单实例**：命名 Mutex + 已运行则退出（用 `App` 级 Mutex，实测 Linux 跨进程有效）。
8. **关闭即退出**：`ShutdownMode.OnMainWindowClose`，不做托盘、不拦截关闭。

---

## 七、交付与部署

| 步骤 | 做法 |
|---|---|
| 构建（Windows 侧） | `dotnet publish src\ZhccCargoBoard.Desktop -c Release -r linux-arm64 --self-contained -p:DebugType=none -o dist-linux\zhcc-board` |
| 打包 | `tar -czf zhcc-board-linux-arm64.tar.gz zhcc-board`（约 70 MB，实际以构建为准） |
| 传输 | `scp` 到板子 `~/apps/`（**板子下载慢，包只传一次**；后续升级只传变更文件或整包覆盖） |
| 安装 | 解压到 `~/apps/zhcc-board/`；桌面放 `zhcc-board.desktop` 快捷方式（图标 PNG） |
| 权限 | 普通用户运行，**不需要 root / 不装 apt 包**（字体已具备） |
| 数据 | 首次运行自动建库于 `~/.local/share/ZhccCargoBoard` |
| 升级 | 覆盖目录 + 重启程序；数据库不动 |

> 传输时间估算：70 MB ÷ ~200 KB/s ≈ **6 分钟**（WiFi）；若走有线（`eth0`）会快很多。

---

## 八、实施计划（方案获批后执行）

| 阶段 | 内容 | 工作量 | 验收 |
|---|---|---|---|
| **P0** | 空壳上板：Avalonia + EGL 渲染 + 中文 + 表格 600 行 + 抓取 | 1～1.5 天 | 窗口正常、中文不缺字、滚动不卡、能抓到 600+ 条 |
| **P1** | 主界面（8 列）+ 下拉筛选 + 排序 + 详情弹窗 + 刷新逻辑 | 3～4 天 | 功能与 Windows 版对齐（除导出/托盘） |
| **P2** | 打包、桌面快捷方式、真机验收、文档 | 1～2 天 | 双击能开、关掉即退、连续运行 2 小时无异常 |
| **合计** | | **6～8 人日** | |

比上一版可行性报告（12～17 人日）少了一半，因为去掉了：导出、托盘、设置、IME 适配、常驻服务。

---

## 九、风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| Avalonia 的 EGL 初始化失败 | 回退软件渲染，滚动略慢 | 已配 `Software` 兜底；P0 实测帧率，720p 可接受 |
| ARM 闭源 GLES 驱动与 Skia 兼容性问题 | 花屏/崩溃 | P0 先验；不行则锁定 `Software` 渲染 |
| 与 YOLO 同时运行抢内存 | 4GB 紧张（当前可用 3.0G） | 本方案常驻约 250～350 MB；建议 P0 时与 YOLO 同跑 10 分钟观察 |
| 关闭即退出 → 历史不累积 | 只有当前“今天~未来 3 天” | 已确认接受；如后续要历史，再加班次定时抓取 |
| 无导出 | 数据只能屏幕看 | 已确认不做；将来要的话可复用 Core 的 `XlsxExporter` 加回来 |
| 板子 IP 变动 | 找不到设备 | 按 `BOARD-STATE.md` §1.1 的方法定位 |
| XFCE 主题/DPI | 界面偏大或偏小 | P0 按实际 720p 调字号 |
| 数据源改版 | 抓取失败 | 已有错误提示 + 日志；Core 抓取逻辑与 Windows 共用，改一处两端生效 |

---

## 十、真机验收清单（P2 用）

1. 双击桌面图标能打开，窗口铺满 1280x720；
2. 中文显示正常（航班号、机场名、状态无方块）；
3. 首次打开 5 秒内抓到 600+ 条并显示；
4. 下拉筛选“进港/出港/机场/状态”生效，计数正确；
5. 点列头能排序；
6. 双击行弹详情，字段齐全，可关闭；
7. 关窗后进程退出（`ps` 看不到）；
8. 断网重开：给出失败提示，不崩溃；
9. 与 YOLO 同跑 10 分钟：不卡死、不 OOM；
10. 连续运行 2 小时：内存无明显增长（`top` 观察）。

---

## 十一、必须遵守的记账规则

实施阶段对板子的每一步（scp 安装目录、建桌面快捷方式、改任何系统文件）都要：

```bash
~/log-change.sh "装了什么/改了什么" "影响/原因" "备份位置"
```

并把板上的 `~/BOARD-STATE.md` 拉回 Windows 覆盖本仓库快照；若影响第 0 节（新增项目路径、依赖、内存占用），同步更新第 0 节并把版本号 +0.1。

**本次方案阶段未改动板子**，只做了只读探测（`uname`、`xrandr`、`eglinfo`、`curl`、`fc-list`），无需记账。

---

## 十二、复现本次探测

```powershell
# Windows 侧（免密 SSH）
ssh orangepi@192.168.170.42 "DISPLAY=:0 xrandr | head -3"
ssh orangepi@192.168.170.42 "DISPLAY=:0 eglinfo -B | head -12"
ssh orangepi@192.168.170.42 "curl -sS -o /dev/null -m 20 -w '%{http_code} %{time_total}\n' https://www.zzcargo.com/airline/1"
```
