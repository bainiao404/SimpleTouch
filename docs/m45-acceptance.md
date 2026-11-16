# M4.5 验收报告：PC WPF 托盘 UI（版本 0.5.0）

> 日期：2026-11-17 ｜ 环境：Win10 21H2 (19044) + Ryzen 7 5700G（CET 硬件）⇄ Xiaomi Pad 5 Pro
> 结论：**内核解耦 + UI 四页完成，自动化验证全过；窗口/托盘视觉交互待人工验收（见 §4 清单）**

## 1. 架构落地

```
SimpleTouch.Agent.Core        Log.Emitted 事件、AgentSettings.DeviceName+Save（+3 单测）
SimpleTouch.Agent.Host (新)   AgentHost：重连循环/事件处理/状态机/热重载（自 Program.cs 原样迁移）
SimpleTouch.Agent             瘦身：CLI 分支保留，主模式委托 AgentHost（行为等价）
SimpleTouch.Agent.Ui   (新)   WPF 托盘（NotifyIcon+GDI+ 图标）+ 四页（连接/设置/日志/后续功能）
```

- 事件链：`AgentHost.StateChanged/PeerChanged` + `Log.Emitted` → VM（Dispatcher 切线程）→ 窗口/托盘
- 托盘：单实例互斥、悬浮提示随状态、状态转移气泡（连接成功/断开重连/停止）、关闭窗口=隐藏
- 零外部 NuGet 包：WinForms NotifyIcon、手写 ObservableObject/RelayCommand

## 2. 自动化验证

| 项 | 结果 |
| ---- | ---- |
| `dotnet build SimpleTouch.slnx`（6 项目） | 0 警告 0 错误 |
| 单测（33：原 30 + AgentSettings 3） | 33/33 ✓ |
| console 行为等价（`--ping` 对真机，走新 AgentHost 代码路径） | P50=21.2ms P95=47.4ms ✓ |
| UI 发布产物启动 | 进程存活、logs/agent-ui-*.log 产出、CET 硬件无 FailFast ✓ |
| Android 0.5.0（versionCode=4）release 重装 | versionName=0.5.0 ✓（无功能变化，仅版本一致性） |
| 发布产物 | PC 控制台 exe 单文件；UI exe 190MB + WPF 原生 DLL 数个（整目录分发，README 已注明） |

## 3. 已知限制 / 设计说明

- WPF 单文件发布不能内嵌 WPF 原生库（D3DCompiler/PresentationNative 等），发布目录含数个原生 DLL——分发需整个文件夹
- UI 与控制台**不能同时连同一设备**（RFCOMM 单客户端；同时启动会 WSAEADDRINUSE，重连循环会持续重试，先停掉一方即可）
- 快捷键编辑器/宏/HID 模式/WiFi 通道/自启动为占位页（后续功能导航），协议与内核槽位已预留

## 4. 人工验收清单（3 分钟，ZCode 派生 shell 的 GUI 在隔离桌面无法自动化）

1. 双击 `publish/SimpleTouch.Agent.Ui.exe` → 主窗口出现（四页 Tab：连接/设置/日志/后续功能），托盘出现紫色 ST 图标
2. 关闭主窗口（点 X）→ 进程仍在（托盘），托盘双击/菜单「打开 SimpleTouch」恢复窗口
3. 连接页：设备下拉能列出 "Xiaomi Pad 5 Pro" → 点「启动连接」→ 状态行变为「已连接: DESKTOP-xxx」+ 气泡；对端 caps 显示 ✓/✗
4. 平板触摸板滑动/点按 → 日志页实时滚动 BUTTON/MOVE 行；托盘悬浮提示更新
5. 设置页：拖动灵敏度滑杆 → 平板滑动速度变化；快捷键表格 11 行；「打开配置文件夹」能打开
6. 断开平板蓝牙再恢复 → 状态行「连接断开→重连」+ 气泡，几秒内自动恢复已连接
7. 托盘右键「退出」→ 进程消失（任务管理器确认无 SimpleTouch.Agent.Ui 残留）
