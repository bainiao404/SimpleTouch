# SimpleTouch 通信协议规范

> **协议版本 v1** ｜ 定稿：2026-11-17 ｜ 状态：M0 基线
> 本文件是协议的**唯一权威来源**。任何变更必须先改本文件与 `test-vectors.json`，再动双端实现（三同步原则）。
> 双端编解码单测必须加载同一份 `docs/protocol/test-vectors.json`。

## 1. 传输层

- 通道：经典蓝牙 RFCOMM（SPP 语义），可靠有序字节流
- 服务 UUID：`8b6d7c1a-2f4e-4c9b-9a3d-5e1f0a2b3c4d`
- 服务名：`SimpleTouch`
- 连接方向：**Android 为 Server**（`listenUsingRfcommWithServiceRecord`），Windows Agent 为 Client（WinRT `RfcommDeviceService` + `StreamSocket`）
- 前提：两端已完成系统级蓝牙配对（安全 RFCOMM）
- 预留：WiFi(TCP) 通道后续以 `ITransport` 新实现接入，帧格式与消息层完全复用

## 2. 帧格式（所有多字节字段均为小端 LE）

```text
┌──────────┬──────────┬──────────┬───────────────┐
│ len :u16 │ type :u8 │ seq  :u8 │ payload 0..4K │
└──────────┴──────────┴──────────┴───────────────┘
```

| 字段 | 说明 |
| ---- | ---- |
| len | **仅** payload 长度，上限 4096 |
| type | 消息类型，见 §3 |
| seq | 发送方逐帧自增计数（mod 256），用于 RTT 测量与排障 |
| payload | 按消息类型定义，可为空 |

帧头共 4 字节（len 2 + type 1 + seq 1）。接收方按流式读取：先取 4 字节头，再取 len 字节 payload。

## 3. 消息总表

| type | 名称 | 方向 | payload 长度 | 说明 |
| ---- | ---- | ---- | ---- | ---- |
| 0x01 | HELLO | 双向 | ≥7 | 握手：版本/能力/设备名 |
| 0x02 | READY | PC→手机 | 0 | PC 端就绪（注入器可用） |
| 0x10 | MOUSE_MOVE | 手机→PC | 4 | 指针相对移动（手机端已做加速度） |
| 0x11 | MOUSE_BUTTON | 手机→PC | 2 | 按键按下/抬起 |
| 0x12 | MOUSE_SCROLL | 手机→PC | 2 | 滚轮/水平滚动 |
| 0x20 | KEY | 手机→PC | 3 | 按键事件（HID Usage ID 编码） |
| 0x21 | TEXT | 手机→PC | ≥1 | Unicode 文本直注（可分帧） |
| 0x22 | HOTKEY | 手机→PC | 2 | 预置快捷键/媒体动作（PC 端映射表决定实际键序） |
| 0x30 | CLIPBOARD_SET | 双向 | ≥1 | 剪贴板同步（可分帧） |
| 0x40 | PING | 双向 | 8 | RTT 测量 |
| 0x41 | PONG | 双向 | 8 | 原样回显 PING 的 payload |
| 0x50 | BYE | 双向 | 1 | 优雅断开 |

## 4. 载荷字段定义

### HELLO (0x01)

```text
┌─────────┬─────────┬──────────┬────────────────┐
│ ver :u16│ caps:u32│ nameLen:u8│ name:UTF-8    │
└─────────┴─────────┴──────────┴────────────────┘
```

- ver：协议版本，当前 = 1。主版本不一致 → 回 BYE(3) 并断开
- caps 能力位（bit0 起）：

| bit | 名称 | 含义 |
| ---- | ---- | ---- |
| 0 | TEXT_INJECT (0x01) | 支持 Unicode 文本直注 |
| 1 | CLIPBOARD (0x02) | 支持剪贴板同步 |
| 2 | MACROS (0x04) | 支持宏 |
| 3 | CONSUMER (0x08) | 支持媒体/consumer 动作（经 HOTKEY 通道） |

### MOUSE_MOVE (0x10)：`dx:i16, dy:i16`（已含加速度的最终增量）

### MOUSE_BUTTON (0x11)

- button：`0`=左键，`1`=右键，`2`=中键
- action：`0`=按下，`1`=抬起

### MOUSE_SCROLL (0x12)：`dx:i8, dy:i8`（正 dy = 向上滚动）

### KEY (0x20)：`code:u8, action:u8, mods:u8`

- code：USB HID Usage ID（Keyboard/Keypad 页 0x07），如 A=0x04；**双端共用词表**（HID 降级模式直通的前提）
- action：同上（0=按下，1=抬起）
- mods 位掩码：bit0=Ctrl(0x01)，bit1=Shift(0x02)，bit2=Alt(0x04)，bit3=Meta/Win(0x08)

### TEXT (0x21) / CLIPBOARD_SET (0x30)

```text
┌─────────┬──────────────────┐
│ flags:u8│ data:UTF-8 字节  │
└─────────┴──────────────────┘
```

- flags bit0 = FIN（最后分帧标记）。单帧发送时 flags=0x01
- 超过 4095 字节的内容按 UTF-8 码点边界分帧；接收方按序拼接至 FIN
- TEXT 的 data 必须是完整 UTF-8 码点序列，不跨帧拆码点

### HOTKEY (0x22)：`presetId:u16`（M2 细化枚举见下表；映射表存于 PC 端 JSON 配置）

presetId 枚举表（协议 v1，M2 定稿）：

| presetId | 常量名 | 名称 | 类别 | PC 默认映射（hotkeys.json 可改） |
| -------- | ------ | ---- | ---- | -------------------------------- |
| 0x0001 | VOLUME_UP | 音量+ | consumer | VK_VOLUME_UP (0xAF) |
| 0x0002 | VOLUME_DOWN | 音量− | consumer | VK_VOLUME_DOWN (0xB0) |
| 0x0003 | VOLUME_MUTE | 静音 | consumer | VK_VOLUME_MUTE (0xAD) |
| 0x0004 | PLAY_PAUSE | 播放/暂停 | consumer | VK_MEDIA_PLAY_PAUSE (0xB3) |
| 0x0005 | MEDIA_PREV | 上一曲 | consumer | VK_MEDIA_PREV_TRACK (0xB1) |
| 0x0006 | MEDIA_NEXT | 下一曲 | consumer | VK_MEDIA_NEXT_TRACK (0xB2) |
| 0x0007 | PAGE_UP | PgUp | macro | VK_PRIOR (0x21) |
| 0x0008 | PAGE_DOWN | PgDn | macro | VK_NEXT (0x22) |
| 0x0009 | F5 | F5 | macro | VK_F5 (0x74) |
| 0x000A | ESC | Esc | macro | VK_ESCAPE (0x1B) |
| 0x000B | WIN_D | Win+D | macro | Meta + VK_D (0x44) |

- 类别 = consumer 的预设依赖 PC 端 **CONSUMER** 能力位；macro 依赖 **MACROS** 能力位。
  手机端快捷面板按 PC HELLO.caps 门控（禁用不支持类别的按钮）
- 0x0010–0x00FF 预留给 PC 映射表自定义预设；未收录的 presetId 一律忽略并记日志（兼容规则）

### PING (0x40) / PONG (0x41)：`ts:u64`（发送方毫秒时间戳）。PONG 必须逐字节回显 payload

### BYE (0x50)：`reason:u8`：`0`=正常，`1`=应用退出，`2`=会话被替换，`3`=版本不兼容，`4`=协议错误

## 5. 连接时序

```text
配对（一次性） → RFCOMM 连接
  → 双方各自立即发送 HELLO（互不等待）
  → 各自校验对端 HELLO（版本/能力）；主版本不符 → BYE(3) + 断开
  → PC 发送 READY
  → 事件流（双向 PING 保活 ≥1s 间隔；手机→PC 输入事件）
  → 断线：PC 端退避重连；BYE 为优雅退出
```

## 6. 兼容规则

- 未知 type：**丢弃该帧**（len 前置保证流不脱同步），记录日志
- 未知字段/扩展位：容忍（新版本可加 caps 位与 payload 尾部字段）
- caps 中未知 bit 置 0 处理

## 7. 测试向量

`test-vectors.json` 为双端共同验收基准：

```json
{ "version": 1, "vectors": [ { "name": "...", "desc": "...", "hex": "1800..." } ] }
```

- hex 为**完整帧**（含帧头）的十六进制串
- 双端单测要求：① 解码字段断言正确；② 以相同 seq 重新编码后与向量逐字节一致
- 已覆盖：HELLO×2、READY、MOUSE_MOVE/BUTTON/SCROLL、KEY、TEXT（单帧 FIN + 两帧分帧）、HOTKEY×2（generic + Win+D）、CLIPBOARD_SET（中文）、PING/PONG、BYE
