# SimpleTouch —— 手机 → PC 蓝牙外设

把 Android 手机/平板变成 PC 的蓝牙输入外设：**触摸板、快捷键面板、文字输入（自研 IME）、剪贴板双向同步**。

- 触摸板：单指移动/点按=左键、长按=拖拽锁定、双指点按=右键、双指滑动=滚轮、三指点按=中键
- 快捷键：11 个预置键（媒体/翻页/F5/Esc/Win+D），按 PC 端能力自动门控
- 文字输入：自研输入法「直通 KEY / 直注 TEXT」双模式 + 触摸条；直注模式支持任意 Unicode（中文无需 PC 端切换输入法）
- 剪贴板：手机 ⇄ PC 双向同步
- 连接：RFCOMM（Android 为服务端），断线双端自动退避重连，Android 前台服务保活

当前状态：**M0–M4 完成，M4.5（PC WPF 托盘 UI）完成**（版本 0.5.0）。路线图与验收数据见 `docs/`。

## 架构

```
Android (Kotlin, Gradle 多模块)              Windows (C#/.NET 10)
├─ core/protocol    帧编解码/分帧(纯Kotlin)   ├─ SimpleTouch.Agent.Core            协议/会话/分帧/配置(平台无关)
├─ core/bluetooth   RFCOMM 服务端(常驻多会话)  ├─ SimpleTouch.Agent.Platform.Windows WinRT RFCOMM/SendInput/Win32剪贴板
├─ feature/*        触摸板/快捷键/IME(Compose) ├─ SimpleTouch.Agent.Host            AgentHost 内核(重连循环+事件处理)
└─ app              会话控制器/前台服务         ├─ SimpleTouch.Agent                 控制台宿主(CLI: --ping/--diag/--repair)
                    └──── RFCOMM 自定义 SPP   ├─ SimpleTouch.Agent.Ui              WPF 托盘 UI(连接/设置/日志/后续功能)
                        UUID，协议见          └─ SimpleTouch.Agent.Tests           33 个单测(双端同源向量)
                        docs/protocol.md ────┘
```

PC 端两种宿主形态共用同一 AgentHost 内核：**UI 版**（托盘常驻+图形界面，日常使用推荐）与**控制台版**（CLI/自动化/联调）。

- **协议**：`[len:u16][type:u8][seq:u8][payload≤4K]` 小端；HELLO 握手 → 事件流；未知类型丢弃（版本兼容）。唯一权威来源：[docs/protocol.md](docs/protocol.md)，双端同源测试向量 16 条。
- **版本号**：双端保持一致（当前 0.5.0）；HELLO 协议版本 = 1（协议未变更）。
- **兼容性**：PC 端发布内置 `CETCompat=false`（Win10 21H2 + 现代 CPU 上 .NET 10 默认 apphost 会启动即闪退，见 docs/troubleshooting/cet-compat.md）。

## 使用（双端）

### 1. 安装

- **PC**：下载/构建 `SimpleTouch.Agent.exe`（单文件、自包含 .NET，无需装 SDK）。首次运行在 `config/` 下自动生成 `hotkeys.json`（快捷键映射，可编辑）与 `settings.json`（灵敏度）。
- **Android**：安装 `app-release.apk`（Android 9+），打开 App 点「启动监听」。

### 2. 配对与连接

关键规则：**配对必须由手机/平板端发起，且发起时 App 处于监听状态**（否则自定义 SPP 服务不进 Windows 配对缓存）。详细步骤与排障见 [docs/pairing-guide.md](docs/pairing-guide.md)。

已配对后日常使用：

1. 手机打开 App（自动进入监听，前台服务保活，可回桌面/息屏）
2. PC 运行 `SimpleTouch.Agent.exe "你的设备名"`
3. 自动握手进入事件循环；断开后 PC 端按 1s→30s 退避自动重连，手机端自动重新监听——**双端都不需要手动干预**
4. 退出：手机点「停止监听」或 Ctrl+C 结束 PC 端

### 3. 后台保活（Android）

- App 内一键申请「电池优化白名单」
- MIUI 类系统需在「系统设置→应用管理→SimpleTouch」手动开启「自启动」「后台弹出界面」

## 构建与开发

| 端 | 命令 |
| ---- | ---- |
| PC 构建 | `cd pc && dotnet build SimpleTouch.slnx` |
| PC 单测 | `dotnet test SimpleTouch.Agent.Tests/SimpleTouch.Agent.Tests.csproj`（30 个） |
| PC 发布(控制台) | `dotnet publish SimpleTouch.Agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false`（Trimmed 与 WinRT 投影冲突，禁用） |
| PC 发布(UI) | `dotnet publish SimpleTouch.Agent.Ui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false`（产物为 exe + WPF 原生 DLL 数个，需整目录分发） |
| PC UI 使用 | 启动 `SimpleTouch.Agent.Ui.exe`：托盘常驻，连接/设置/日志/后续功能四页；关闭窗口不退出，托盘「退出」才结束 |
| Android 构建 | `cd android && ./gradlew assembleDebug`（需 JDK 17） |
| Android 单测 | `./gradlew :core:protocol:test --rerun` |
| Android 发布 | `./gradlew assembleRelease`（需 `android/keystore/keystore.properties` + keystore，本地生成不入库；缺失回退 debug 签名） |
| 发布签名 | `keytool -genkeypair -keystore android/keystore/simpletouch.keystore -alias simpletouch -keyalg RSA -keysize 2048 -validity 10000`，密码写入 `android/keystore/keystore.properties`（格式见 build.gradle.kts） |

## 文档

- [方案前瞻探索.md](方案前瞻探索.md) —— 架构选型与决策记录
- [项目执行流程和方案.md](项目执行流程和方案.md) —— 里程碑 DoD、发布检查清单
- [docs/protocol.md](docs/protocol.md) —— 协议规范（帧格式/消息表/分帧规则/HOTKEY 枚举）
- [docs/pairing-guide.md](docs/pairing-guide.md) —— 配对与首连指南
- [docs/m0-acceptance.md … m4-acceptance.md](docs/) —— 各里程碑验收数据与踩坑记录

## 已知限制

- 自定义 SPP 服务依赖配对时机：若配对发生在 App 未监听时，PC 端会发现不了服务——重新配对（手机端发起）或运行 `SimpleTouch.Agent.exe --repair`
- Windows 端仅支持 x64（Win10 1903+/Win11）；macOS/Linux 预留（Agent 分层已抽象）
- 多设备同控、iOS 端为非目标
