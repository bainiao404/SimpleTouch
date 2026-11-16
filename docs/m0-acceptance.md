# M0 端到端验收报告

> 日期：2026-11-17 ｜ 结论：**通过**（DoD：双向握手成功 + PING RTT 实测数据）

## 环境

| 项 | 值 |
| ---- | ---- |
| 测试设备 | Xiaomi Pad 5 Pro（M2105K81AC，Android 11 / API 30） |
| PC | Windows 10 21H2 Build 19044，Realtek Bluetooth Adapter，Ryzen 7 5700G |
| 连接方式 | Android RFCOMM Server（自定义 UUID）← WinRT StreamSocket Client |
| 配对要点 | **自定义 SPP 服务只在「App 监听中」发生配对时进入 Windows 缓存**（手机端发起配对实测成功） |

## 实测数据（PING ×100 @200ms，协议 v1）

```text
握手完成: peer=M2105K81AC ver=1 caps=0x0
PING #1: 60.4 ms   #2: 16.2 ms   #3: 27.8 ms   #4: 32.3 ms   #5: 21.7 ms
PING #6: 8.8 ms    #7: 47.7 ms   #8: 47.3 ms   ...（N=100）
RTT 统计: N=100 P50=26.5ms P95=704.8ms Max=755.6ms
```

## 观察与 M1 行动项

1. **P50=26.5ms 达标**：经典蓝牙链路往返基线良好，指针输入可用区间
2. **P95=704.8ms 不达标（M1 核心）**：出现规律性 ~700ms 尖刺，怀疑蓝牙 sniff 省电间隔
   与安卓线程调度。M1 对策：
   - 输入事件按 16ms 窗口合并（已列入 §4 数据流设计）
   - 尖刺期间指针增量补偿（事件时间戳回放）
   - 探索连接参数（REQUEST_CONNECTION_PRIORITY / 保持 ACL 活跃）
   - 对比 BLE GATT 通道延迟数据（若仍不达标）
3. **PING ≠ 指针路径**：M1 需对 MOVE 事件单独实测端到端（双端时间戳对齐 + 录屏对拍）
4. M0 已知限制（按计划在后续里程碑处理）：
   - 单客户端：会话结束后手机端监听自动停止，需重启 App（M4 连接状态机）
   - 无自动重连（M4）；手机端 caps=0（M2/M3 随功能开放）
