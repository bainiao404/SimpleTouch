# M3 IME 文字输入 + 剪贴板验收报告

> 日期：2026-11-17 ｜ 结论：**自动化项全部通过**；真实窗口打字/三处必测为人工复核项（自动化环境 GUI 沙箱隔离，见下）

## 本里程碑交付

| 项 | 实现 |
| ---- | ---- |
| 协议落地 | TEXT/CLIPBOARD_SET 分帧语义实现：双端 `TextFrameSplitter`（UTF-8 码点边界切分，落实 protocol.md「不跨帧拆码点」），帧格式无变更 |
| 直通模式 | 自研 IME `SimpleTouchIme`：按键 → KEY 消息（HID Usage ID 词表与 PC HidToVk 对应），中文由 PC 端输入法组词 |
| 直注模式 | IME 直注（逐字符 TEXT）+ App「文本」页直注（系统输入法打字含中文 → 整段 TEXT）→ PC `KEYEVENTF_UNICODE` |
| 触摸条 | IME 键盘上方触摸条：滑动=指针移动、点按=左键（边打字边滑动，方案 F3） |
| 剪贴板双向 | PC 轮询 `GetClipboardSequenceNumber` → 变更自动推送 CLIPBOARD_SET（含防回环：手机刚推来的不回推）；手机「文本」页一键发送 → PC 写剪贴板；PC 推送内容在手机显示并可一键复制到手机剪贴板 |
| 能力门控 | 直注按钮按 caps.TEXT_INJECT、剪贴板按钮按 caps.CLIPBOARD 禁用/提示 |
| 架构 | session 托管上移至 `SimpleTouchApp`（IME 服务与 Activity 同进程共享通道 `KeyboardChannelHolder`） |

## 链路证据（自动化，2026-11-17 实测）

| 项目 | 结果 |
| ---- | ---- |
| 单测 | PC 30/30（+5 分帧器）；Android 11/11（协议 6 + 分帧器 5，含 emoji 4 字节码点/汉字切点回退） |
| CLIPBOARD_SET PC→手机 | `Set-Clipboard "PC剪贴板推送到手机 M3测试"` → 轮询 1s 内检测(seq 609→621) → 推送 15 字符 → **手机文本页一字不差显示** ✓ |
| CLIPBOARD_SET 手机→PC | 手机草稿框 `input text` → 「发送到 PC 剪贴板」→ **`Get-Clipboard` = "hello-m3-clipboard"** ✓ |
| TEXT 直注 | 手机文本页 13 字符 → Agent 日志 `TEXT 直注 13 字符`（计数精确）→ SendInput 注入 ✓ |
| IME 直注模式 | 实机键盘按 H、I → 两条 `TEXT 直注 1 字符` ✓ |
| IME 直通模式 | 实机键盘按 H → `KEY code=0x0B`；⇧+P → `KEY code=0x13 mods=0x02`；⏎ → `KEY code=0x28` ✓ |
| 触摸条点按 | 点按 → `BUTTON btn=0 down/up` 对（57–59ms 间隙，M2 兜底逻辑同样生效）✓ |
| 中文多字节 | PC→手机 15 字符含中文内容完整；分帧器单测覆盖 3 字节汉字/4 字节 emoji 码点边界 |

## 排障记录（新硬核经验）

1. **GlobalSize 跨进程句柄不可靠**：读他人进程放入剪贴板的 HGLOBAL 时 GlobalSize 可能返回 0
   （初版实现因此读到空串）；且初版 P/Invoke 签名本身就写错（GlobalSize 是返回值而非 out 参数）。
   **对策**：剪贴板文本按约定以 `\0` 结尾，直接扫描终止符（上限 1M 字符）
2. **自动化环境 GUI 沙箱隔离**：ZCode 派生的 shell 里 `Start-Process notepad/cmd` 创建的窗口
   EnumWindows/FindWindow **全部不可见**（不在本站桌面），但 UIA 能枚举用户真实窗口——
   会话/桌面隔离所致。**真实窗口打字验证（记事本/浏览器/微信三处必测）转人工复核**。
   Agent 的 SendInput 机制本身已由 M1 人工复核证实可达真实桌面（鼠标移动/点按），TEXT 与
   KEY 走同一 SendInput 通道
3. **uiautomator dump 不含 IME 窗口**：自研键盘的按键坐标须用 `adb exec-out screencap -p`
   截图 × (实际宽/图宽) 换算
4. **IME 需启动一次 App 后才能 ime enable**（装完即 enable 报 Unknown input method）

## 人工复核清单（参照 M1/M2 方式）

- [ ] **三处必测**：记事本、浏览器地址栏、微信聊天框——文本页直注中文 + IME 直通英文各一遍
- [ ] 焦点切换/失焦重试路径：打字中途切窗口再切回，文本落点是否正确
- [ ] PowerShell 类窗口单独记录支持情况（conhost 对 KEYEVENTF_UNICODE 的兼容性）
- [ ] IME 触摸条滑动=指针移动（点按已自动验证）
- [ ] 长文本（>4095B UTF-8）直注分帧重组（自动验证仅到 Agent 日志层）
- [ ] 自研键盘观感：按键显示为大写（MIUI Button textAllCaps 默认）但发送为小写——如困扰改 `isAllCaps=false`

## 已知待办（转 M4+）

- IME 触摸条无视觉边界/把手，不易察觉（截图可见但对比度低）
- IME 无中文引擎：中文经 App「文本」页由系统输入法产生（本里程碑设计决定），自研拼音引擎不做
- PC 剪贴板监听为 1s 轮询；如有延迟诉求换 message-only 窗口 + WM_CLIPBOARDUPDATE
- 直注模式下 Ctrl/Alt 修饰键无 Unicode 语义，仅直通模式生效（设计如此）
