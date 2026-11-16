# SimpleTouch 配对与首连指南

> 适用：SimpleTouch 0.4.0（M4）。本指南浓缩自 M0/M4 实测（Xiaomi Pad 5 Pro + Win10 21H2 + Realtek BT），其他机型流程相同、细节可能略有差异。

## 原理速览

SimpleTouch 走蓝牙经典 **RFCOMM 自定义服务**（UUID `8b6d7c1a-2f4e-4c9b-9a3d-5e1f0a2b3c4d`），Android 端为服务端、PC 端为客户端。这个自定义服务有一条硬性约束：

> **SDP 服务记录只在「手机 App 处于监听中」发生配对时进入 Windows 的配对缓存。**

换言之：先配对、后装 App 的机器，PC 端**发现不了** SimpleTouch 服务，必须重新配对一次。

## 首次配对（三步）

1. **手机端**：安装并打开 SimpleTouch → 点「启动监听」（状态显示「监听中…等待 PC 连接」）。保持 App 在前台或让其以前台服务运行。
2. **手机端**：进入系统「设置 → 蓝牙」，扫描并点击你的 PC 名字发起**配对请求**（不要在 PC 端发起！）。
   - PC 端会弹出配对确认框，双方确认，配对完成。
   - 配对成功后，SimpleTouch 的 RFCOMM 服务即进入 Windows 配对缓存。
3. **PC 端**：运行 Agent，第一次连接建议先 `--list` 确认设备名，再正常启动：

   ```bat
   SimpleTouch.Agent.exe --list
   SimpleTouch.Agent.exe "你的手机蓝牙名"
   ```

   握手完成后手机状态变为「已连接」，即可使用触摸板/快捷键/文本/剪贴板。

> ⚠️ 如果配对发生在 App 未监听时（例如先配对了再装 App）：删除配对 → 打开 App 启动监听 → 手机端重新发起配对。或用 PC 端 `SimpleTouch.Agent.exe "设备名" --repair` 辅助修复。

## 日常使用（已配对）

- 手机打开 App 即自动监听（前台服务保活，可回桌面/息屏）；PC 端启动 Agent 即自动连接。
- **断线自动恢复**：任一端中断（杀 App / 关蓝牙 / 信号丢失），PC 端按 1s→2s→4s→8s→15s→30s 退避重连，手机端自动重新监听，恢复后无需任何手动操作。
- 退出：手机「停止监听」或 PC 端 Ctrl+C。

## 排障

| 症状 | 处置 |
| ---- | ---- |
| PC 报「未找到已配对设备」 | 手机蓝牙未开/未配对 → 先配对；检查设备名是否与 `--list` 输出一致 |
| PC 报「未发现 SimpleTouch RFCOMM 服务」 | ① 手机 App 未启动/未监听 → 启动监听后重试（Agent 会自动重连）② 配对时 App 未监听 → 重新配对（手机端发起）或 `--repair` |
| 反复失败 | 运行 `SimpleTouch.Agent.exe "设备名" --diag` 输出诊断信息（服务发现/MAC/通道状态） |
| 重配对总失败 | 顺序必须是：手机删除本机配对 → App 保持监听 → 手机端蓝牙设置页发起配对（PC 端无 UI 时 PairAsync 会静默失败） |
| 息屏/后台后断连 | App 内申请「电池优化白名单」；MIUI 还需开「自启动」「后台弹出界面」权限 |
| PC 双击闪退（21H2 类系统） | 本产物已内置 `CETCompat=false` 规避；若仍闪退见 docs/troubleshooting/cet-compat.md |

## 实测环境（基线）

- PC：Win10 21H2 (19044) + Ryzen 7 5700G（CET 硬件）+ Realtek BT 适配器
- 手机：Xiaomi Pad 5 Pro（M2105K81AC，Android 11 / API 30）
- 延迟基线：P50 ≈ 17–27ms / P95 ≈ 43–64ms（见 docs/m1-latency.md、m2-acceptance.md）
