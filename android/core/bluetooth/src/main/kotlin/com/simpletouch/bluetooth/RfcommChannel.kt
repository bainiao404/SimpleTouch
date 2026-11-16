package com.simpletouch.bluetooth

import com.simpletouch.protocol.Capabilities
import com.simpletouch.protocol.InputChannel
import com.simpletouch.protocol.InputEvent
import com.simpletouch.protocol.ProtocolConsts
import com.simpletouch.protocol.StMessage
import com.simpletouch.protocol.TextFrameSplitter
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * InputChannel 的第一个实现（方案文档 §4 耦合控制）：语义事件 → 协议消息 → RFCOMM。
 *
 * 线程模型：
 * - [send] 非阻塞，可在手势回调/主线程直接调用；
 * - PointerMove 只做增量累积（锁保护），由 16ms 定时循环合并发送（§4 数据流设计）；
 * - 其余事件进内部队列，由单消费者协程按序发送（保证 down→up 等顺序）。
 */
class RfcommChannel(
    private val server: RfcommServer,
    override val capabilities: Capabilities,
) : InputChannel {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val queue = Channel<InputEvent>(Channel.UNLIMITED)
    private val lock = Any()
    private var accX = 0f
    private var accY = 0f

    /** 上一次派发的按键 down（用于识别快速 down→up 点按对，M1 已知问题 #2）。仅单消费者协程访问。 */
    private var lastDownButton = -1
    private var lastDownDispatchNanos = 0L

    @Volatile
    private var running = true

    init {
        // 16ms 移动合并循环
        scope.launch {
            while (isActive && running) {
                delay(FLUSH_INTERVAL_MS)
                val (dx, dy) = synchronized(lock) {
                    val x = accX; val y = accY; accX = 0f; accY = 0f
                    x to y
                }
                if (dx != 0f || dy != 0f) {
                    val ok = runCatching { sendMove(dx, dy) }
                    if (ok.isFailure) running = false
                }
            }
        }
        // 非移动事件单消费者
        scope.launch {
            while (isActive && running) {
                val event = queue.receiveCatching().getOrNull() ?: break
                val ok = runCatching { dispatch(event) }
                if (ok.isFailure) running = false
            }
        }
    }

    override fun send(event: InputEvent) {
        when (event) {
            is InputEvent.PointerMove -> synchronized(lock) {
                accX += event.dx
                accY += event.dy
            }
            else -> queue.trySend(event)
        }
    }

    private suspend fun dispatch(event: InputEvent) {
        when (event) {
            is InputEvent.PointerButton -> {
                val now = System.nanoTime()
                // 快速 down→up 点按对（<150ms）在中间补 45ms 间隙，避免同毫秒 down/up 被应用吞掉。
                // 注意：BT 链路可能把相邻小帧合并送达（见 docs/m2-acceptance.md），PC 端注入侧还会兜底补足
                if (!event.down && event.button == lastDownButton &&
                    now - lastDownDispatchNanos < TAP_PAIR_NANOS
                ) {
                    delay(TAP_GAP_MS)
                }
                if (event.down) {
                    lastDownButton = event.button
                    lastDownDispatchNanos = System.nanoTime()
                }
                server.send(
                    StMessage.MouseButton(
                        event.button,
                        if (event.down) ProtocolConsts.PressAction.DOWN else ProtocolConsts.PressAction.UP,
                    )
                )
            }
            is InputEvent.PointerScroll -> server.send(
                StMessage.MouseScroll(
                    event.dx.coerceIn(-127, 127).toByte(),
                    event.dy.coerceIn(-127, 127).toByte(),
                )
            )
            is InputEvent.KeyPress -> server.send(
                StMessage.Key(
                    event.hidUsage,
                    if (event.down) ProtocolConsts.PressAction.DOWN else ProtocolConsts.PressAction.UP,
                    event.mods,
                )
            )
            is InputEvent.HotkeyTrigger -> server.send(StMessage.Hotkey(event.presetId))
            is InputEvent.ConsumerAction -> server.send(StMessage.Hotkey(event.presetId))
            is InputEvent.TextInput -> sendText(event.text)
            is InputEvent.ClipboardSet -> sendClipboard(event.text)
            is InputEvent.PointerMove -> Unit // 走合并路径，不会入队
        }
    }

    /** TEXT 分帧发送：TextFrameSplitter 保证码点边界（protocol.md §4），末帧带 FIN。 */
    private suspend fun sendText(text: String) {
        val frames = TextFrameSplitter.split(text)
        frames.forEachIndexed { index, chunk ->
            server.send(StMessage.Text(fin = index == frames.lastIndex, chunk))
        }
    }

    /** CLIPBOARD_SET 分帧发送（手机→PC 方向），分帧规则同 TEXT。 */
    private suspend fun sendClipboard(text: String) {
        val frames = TextFrameSplitter.split(text)
        frames.forEachIndexed { index, chunk ->
            server.send(StMessage.ClipboardSet(fin = index == frames.lastIndex, chunk))
        }
    }

    private fun sendMove(dx: Float, dy: Float) {
        server.send(
            StMessage.MouseMove(
                clampToShort(dx).toShort(),
                clampToShort(dy).toShort(),
            )
        )
    }

    fun close() {
        running = false
        queue.close()
        scope.cancel()
    }

    private fun clampToShort(v: Float): Int = v.toInt().coerceIn(Short.MIN_VALUE.toInt(), Short.MAX_VALUE.toInt())

    private companion object {
        const val FLUSH_INTERVAL_MS = 16L

        /** 点按 down/up 间隙（M1 验收报告建议 40–60ms） */
        const val TAP_GAP_MS = 45L

        /** 判定为同一次点按的 down→up 间隔上限（拖拽 down…up 间隔远大于此值，不插间隙） */
        const val TAP_PAIR_NANOS = 150_000_000L
    }
}
