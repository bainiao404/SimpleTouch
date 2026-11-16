# M2 触摸板打磨 + 快捷键面板验收报告

> 日期：2026-11-17 ｜ 结论：**自动化项全部通过**；多指手势/真实拖拽/媒体键听感为人工复核项

## 本里程碑交付

| 项 | 实现 |
| ---- | ---- |
| 协议三同步 | `docs/protocol.md` §4 HOTKEY presetId 枚举表定稿（11 预设 + 0x0010–0x00FF 预留自定义）；新增 `hotkey_win_d` 测试向量（16 条）；双端 `HotkeyPreset` 常量 |
| 设置持久化 | 安卓 `SettingsStore`（filesDir/settings.json，org.json 落盘）+ 设置页（灵敏度 0.5–3.0×/自然滚动开关/滚动阈值 8–64px）；PC 端 `AgentSettings`（config/settings.json：moveScale） |
| 触摸板打磨 | 长按拖拽锁定（≥系统长按阈值且位移<tap slop → 左键 down 持续，移动跟随，抬起释放；原地长按抬起=单击）；点按 down/up 间隙（双端，见下）；滚动方向/灵敏度/阈值接线 |
| 快捷键面板 | `feature/hotkeys` 新模块：11 预置键网格（音量±/静音/播放暂停/上下曲/PgUp/PgDn/F5/Esc/Win+D）；PC HELLO.caps 门控（媒体键=CONSUMER、键序=MACROS，未握手全禁用）；PC `config/hotkeys.json` 映射表（缺失自动生成、可编辑）→ `TapKey` 组合键注入（修饰键+VK，holdMs 保持）；未知 presetId 忽略并记日志 |

## 链路证据（自动化，2026-11-17 实测）

| 项目 | 结果 |
| ---- | ---- |
| 单测 | PC 25/25（16 向量往返 + 6 字段断言 + 3 配置测试）；Android 协议 6/6（含新向量循环） |
| 连接/握手 | 多轮会话重启均正常；PC 配置日志 `hotkeys.json 11 项预设, moveScale=1` |
| 灵敏度生效 | 同一 `input swipe`（620px）：1.5× → 光标 +1508px；0.5× → +491px（≈1/3，加载路径 ✓） |
| 设置保存 | UI 翻转自然滚动开关 → `files/settings.json` 即时落盘 `{"sensitivity":1.5,"naturalScroll":true,...}` ✓ |
| 点按间隙 | 修复前 down/up 同毫秒到达 PC（BT 链路合并发送侧间隙，logcat 取证 delay 确挂起 45ms）；PC 注入侧补足后 **65–87ms**（正常点击时长区间）✓ |
| 长按单击 | `input swipe` 同点按住 600ms → 长按分支左键 down/up 对（66ms 间隙）✓ |
| 快捷面板 | 按钮 enabled=true（caps 门控放行）；点按「音量+」「播放/暂停」→ PC 日志 `HOTKEY presetId=1 → 音量+ (vk=0xAF)`、`presetId=4 → 播放/暂停 (vk=0xB3)` → TapKey 注入 ✓ |
| PING RTT (N=100) | **P50=21.3ms / P95=56.3ms** ≤80ms ✓；Max=260.6ms（单次尖刺，持续观察） |

## 关键发现：BT 链路合并小帧（新硬核经验）

手机端 RFCOMM 两次相隔 45ms 的小帧写入（6 字节 BUTTON down/up），**Windows 端实测同毫秒收到**
——发送侧间隙过不了蓝牙栈（发送侧 delay 已用 logcat 取证确认执行：`.600 delaying → .645 done`）。
推论：小帧会积压在发送队列，由后续写触发一并送达。
**对策**：间隙必须在注入侧兜底——PC 端在快速 down→up 对（<目标间隙）注入前 `Sleep(剩余差值)`，
与手机端发送侧间隙叠加后，应用看到的按住时长稳定落在正常点击区间。

## 人工复核清单（adb 无法注入多指触摸/连续拖拽，参照 M1 复核方式）

- [ ] 双指点按=右键；三指点按=中键
- [ ] 双指滑动=滚轮：传统方向 + 设置页切换自然滚动后方向反转
- [ ] 长按拖拽锁定完整流程：按住 ~600ms 后移动 → 光标带左键拖拽 → 抬起释放
- [ ] 快捷键实体听感复核：音量±/静音/播放暂停/上下曲（注入路径已自动验证，媒体键 VK 同机制）
- [ ] Win+D / F5 / Esc / PgUp / PgDn 实体复核（键序注入机制同上）
- [ ] 触摸板跟手感主观评价（灵敏度 0.5–3.0 滑杆实际调优）

## 已知待办（转 M3+）

- 点按间隙实测 65–87ms 略超 40–60 建议（Thread.Sleep 定时器粒度 + BT 到达抖动）；如需精确可换高精度等待
- 长按拖拽锁定的长按阈值取系统 `longPressTimeoutMillis`（~400ms），不可调；如有反馈再加设置项
- PC 端 moveScale 暂无 UI，手改 `config/settings.json`
- RTT Max=260ms 单次尖刺（M0 的 704ms 尖刺同类），持续观察
