# M4 验收报告：稳定性与发布

> 日期：2026-11-17 ｜ 环境：Win10 21H2 (19044) + Ryzen 7 5700G（CET 硬件）+ Realtek BT ⇄ Xiaomi Pad 5 Pro (Android 11/API 30)，debug adb 联调 + release 双端产物实测
> 结论：**M4 全部任务完成，发布检查清单 6/6 核验通过**（1 项程序性验证+遗留人工项）

## 1. 断线退避重连（双端）

### 实现

- **PC Agent**（`Program.cs`）：事件循环模式重构为持久重连循环。会话结束/连接失败按 **1s→2s→4s→8s→15s→30s 封顶**退避重试，握手成功归零；注入器/配置/剪贴板监听跨会话复用；断线期间 PC 剪贴板变更静默跳过（`currentSession=null`），重连后恢复推送（防回环标记跨会话保留）。
- **Android**（`RfcommServer` + `SessionController`）：serverSocket **常驻多会话循环**（accept→会话→继续 accept），SDP 记录持续注册，PC 重连可直接发现服务；会话断开不再销毁监听，仅用户「停止监听」才关闭。

### 演练 A：杀 App（force-stop）

| 时刻 | 事件 |
| ---- | ---- |
| 04:34:33 | `am force-stop` → PC 检测断开 |
| 04:34:33–04:34:41 | PC 退避重试 1s→2s→4s→8s（4 次失败，符合预期——SDP 已随进程死亡消失） |
| 04:34:47 | App 重启（自动监听） |
| 04:34:48.99 | PC 第 5 次尝试 → **04:34:49.6 握手完成（断线重连成功）**，App 启动到重连 **≈1.6s** |

### 演练 B：手机端关/开蓝牙（设置页开关，MIUI 用户版禁用 svc/cmd 关闭）

| 时刻(PC钟) | 事件 |
| ---- | ---- |
| 04:37:47 | 蓝牙关闭 → PC `连接断开: 对端正常关闭`；手机 `连接断开: bt socket closed` + `监听失败: read failed` |
| 04:37:56–04:38:14 | 手机自动重听退避 2s→4s→6s→8s（4 次，蓝牙未就绪自动跳过）；PC 退避重试失败（COMException） |
| ~04:38:13 | 蓝牙重新打开 |
| 04:38:18.7 | **PC 握手完成（断线重连成功）**——手机第 5 次重听成功后立即被 PC 抓住 |
| 04:38:26(手机钟) | HELLO/READY 交换，双端事件流恢复 |

重连后功能冒烟：触摸板滑动（4 move/s）+ 点按（BUTTON down/up）均正常注入 ✓

### 演练中发现并修复的缺口（commit 951c58e）

1. `RfcommServer.accept()` 在蓝牙开关时抛 IOException 会**静默退出循环**（上层停留「监听中」假象）→ 补 `onListenFailed` 上报
2. 蓝牙不可用/监听失败后上层无重试 → `SessionController.scheduleRelisten`（2s×n 封顶 15s，用户停止后不触发，连接成功归零）
3. PC 端异常 Message 为空（COMException）时日志无信息 → 补类型名

## 2. 前台服务 + 后台保活

- `ConnectionService`：connectedDevice 类型前台服务（API 29+ 三参 startForeground；API 34+ 权限已预声明），通知文案随会话状态实时刷新（`SessionController.onStatusChanged` 钩子），服务销毁兜底停会话。
- 实测：`dumpsys` `isForeground=true` ✓；**按 HOME 退后台 90s 进程同 PID 存活** ✓。
- 电池优化白名单：连接页一键申请（`ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`）+ MIUI「自启动/后台弹出」手动引导文案；白名单弹窗的人工确认与息屏存活为遗留人工项。

## 3. PC 发布

- 命令：`dotnet publish SimpleTouch.Agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false`
- 产物：`publish/SimpleTouch.Agent.exe` **95MB 单文件**（含 .NET 10 运行时+WinRT 投影；仅随附 pdb）
- 版本：`0.4.0`（SourceLink commit 元数据：`0.4.0+951c58e`）
- **冒烟**：对真机 `--ping` ×3：P50=27.5/23.2/21.9ms，复测两次 P95=63.7/57.3ms（首测 P95=718ms 为重连后链路未稳，二次起回 M2 基线 ≤80 达标）
- **自包含验证**：`COREHOST_TRACE=1` 追踪——`Detected Single-File app bundle`，运行时从 bundle 解析，**全程 0 次引用系统 dotnet 目录**；`DOTNET_ROOT` 清空运行正常
- CET：本机即 CET 硬件 + 21H2，发布产物启动/运行正常（`CETCompat=false` 出厂保留）

## 4. Android 发布

- 签名：`android/keystore/simpletouch.keystore`（RSA 2048，有效期 10000 天，本地生成**不入库**，`keystore.properties` 提供密码，缺失回退 debug 签名）
- 产物：`app-release.apk` **1.98MB**（debug 27MB）；apksigner 验证 **v2 scheme ✓**（minSdk 28 ≥ 24 无需 v1）
- R8：minify + shrinkResources；keep `com.simpletouch.protocol.**`；移除 `Log.v/d/i`（保留 w/e）
- **冒烟**：卸载 debug → 安装 release：`versionName=0.4.0` ✓ → 启动监听 → 前台服务 ✓ → PC 发布产物自动连接 ✓ → 触摸板事件（BUTTON+MOVE）✓ → IME 重新 enable ✓

## 5. 文档

- `README.md`（新）：定位/架构/使用/构建发布命令/已知限制
- `docs/pairing-guide.md`（新）：配对时机硬规则（手机端发起 + App 监听中）、日常使用、排障表

## 6. 发布检查清单核验（执行文档 §7）

| 项 | 结果 |
| ---- | ---- |
| csproj 含 `CETCompat=false` | ✅（CET 硬件实机运行通过） |
| 自包含产物无 SDK 环境可运行 | ✅ 程序性验证（host 追踪零系统运行时引用）；无 SDK 干净 VM 双击=遗留人工项 |
| 21H2 类系统实测启动正常 | ✅（本机即 19044，发布产物 --ping ×3） |
| Android release 关日志/混淆 keep 协议 | ✅（R8 规则+release 冒烟） |
| README + 配对指南 | ✅ |
| 版本号双端一致 / HELLO 协议版本核对 | ✅（双端 0.4.0；协议版本未变更=1 双端一致） |

## 7. 新增硬核经验（详见 任务继承.md §5 17–21）

1. MIUI 用户版 `svc bluetooth disable` 不可用（exit 137）——走设置页开关 + uiautomator 定位
2. 蓝牙开关会让阻塞中的 `accept()` 抛 IOException，且此时 `running` 仍为 true——必须上报否则上层假象
3. 连接页新增电池优化卡片会**下移按钮布局**，§6 快捷坐标随之失效（截图换算需 ×实际分辨率/截图分辨率）
4. jarsigner 只验 v1 签名，minSdk≥24 的 AGP 产物需 `apksigner verify`
5. `am start -e` 对已运行 Activity 走 onNewIntent，extra 不生效（自动化入口需先 force-stop）
6. Git Bash 下 adb 设备路径参数需 `MSYS_NO_PATHCONV=1`，但此时本机路径必须给 Windows 形式

## 8. 遗留人工项

- 无 SDK 干净环境（沙箱/VM）双击验证发布产物
- 电池优化白名单弹窗人工确认 + 息屏（灭屏）长时保活观察
- M2/M3 人工复核项继续有效（多指手势/真实窗口打字，见对应验收文档）
- PC 发布产物建议在第二台非开发机（无 Realtek BT 的机器）做兼容抽测

## 9. 发布后复盘：双指滚动 ANR（2026-11-17 当天用户报告）

**现象**：双指滚动必致安卓端卡死（ANR，3 次记录 03:25/04:04/04:58）。

**取证链**：logcat `am_anr: Input dispatching timed out` → GC 日志（0.35s 内堆 121→188MB，
~20B/对象的百万级活对象，"freed 0(0B)"）→ OOM 递归打印死亡螺旋（DefaultExecutor 烧 15.6s CPU）。
MIUI 触摸驱动 input_filter 丢弃 sendevent 注入的大部分事件码（实测仅 Y/TRACKING_ID 可达），
无法做多指内核注入；release 包非 debuggable 不可 dumpheap——最终以 Compose 测试
`performTouchInput` 多指注入做定位与回归。

**根因**：TouchpadScreen 双指滚动两个分支的累积算术**都写反了**——向上分支 `scrollAccum -=`
（负向发散）、向下分支 `+=`（正向发散），`while` 条件永真 → 主线程死循环以 CPU 速率
trySend 塞爆 UNLIMITED 队列 → 堆 OOM + 输入超时 ANR。**M1 起即存在，双向滚动都卡死**；
潜伏 4 个里程碑的原因：adb 无法注入多指 + M1 人工复核只测了向上滚（其 PC 日志 dy=1 实为
死循环刷屏的前几帧）+ M2–M4 未复测。

**修复与验证**：两分支改向 0 回收；RfcommChannel.send 死通道丢弃事件（防同类问题借
UNLIMITED 队列 OOM 放大）；新增 `TouchpadScrollGestureTest`（performTouchInput 双指双向
滚动 + 双指点按右键）——修复前滚动用例挂 6min 至 keyDispatchingTimedOut，修复后 3/3 通过。
版本 0.4.1 双端重打发布产物并冒烟通过。

**教训**：① 无法自动化的手势路径必须尽快补 Compose 多指注入测试（performTouchInput
原生支持）；② 累积量循环的算术方向要"向 0 回收"对偶检查；③ UNLIMITED 队列必须有死
通道短路，否则任何上游 bug 都会放大成 OOM。
