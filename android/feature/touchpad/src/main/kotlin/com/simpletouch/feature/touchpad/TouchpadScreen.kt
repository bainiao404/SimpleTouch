package com.simpletouch.feature.touchpad

import android.app.Activity
import android.view.WindowManager
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.dp
import com.simpletouch.protocol.InputChannel
import com.simpletouch.protocol.InputEvent
import com.simpletouch.protocol.ProtocolConsts
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * M2 触摸板（执行文档 §5）：
 * - 单指滑动=指针移动；单指点按=左键；单指长按=拖拽锁定（按住左键移动，抬起释放）
 * - 双指点按=右键；双指滑动=滚轮（方向随 [TouchpadConfig.naturalScroll]）；三指点按=中键
 * - 灵敏度/滚动阈值来自 [TouchpadConfig]（设置页可调，JSON 持久化）
 * - 事件统一走 [InputChannel]（特征层零传输依赖）；点按 down/up 间隙由 RfcommChannel 消费侧保证
 */
@Composable
fun TouchpadScreen(
    channel: InputChannel,
    config: TouchpadConfig,
    onExit: () -> Unit,
) {
    val view = LocalView.current
    LaunchedEffect(Unit) {
        (view.context as? Activity)?.window?.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
    }
    DisposableEffect(Unit) {
        onDispose {
            (view.context as? Activity)?.window?.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("触摸板", style = MaterialTheme.typography.titleMedium)
            Button(onClick = onExit) { Text("退出") }
        }

        Box(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .clip(RoundedCornerShape(16.dp))
                .background(Color(0xFFEFF3F8))
                .pointerInput(channel, config) {
                    awaitEachGesture {
                        gestureLoop(channel, config, viewConfiguration.longPressTimeoutMillis)
                    }
                },
        )

        Text(
            "单指滑动=移动 · 点按=左键 · 长按=拖拽 · 双指点按=右键 · 双指滑动=滚轮 · 三指点按=中键",
            style = MaterialTheme.typography.bodySmall,
        )
    }
}

/**
 * 单次手势（第一个手指按下到全部抬起）。channel.send 为非阻塞调用，
 * 可安全运行在 awaitEachGesture 的受限挂起作用域内。
 *
 * 长按拖拽锁定无需定时器：手指静止期间无事件，锁定判定在下一个事件（恢复移动或抬起）到达时进行——
 * 按住 ≥ 长按阈值且累计位移未超 tap slop 即锁定左键，与物理触摸板 press-and-hold 语义一致。
 */
private suspend fun androidx.compose.ui.input.pointer.AwaitPointerEventScope.gestureLoop(
    channel: InputChannel,
    config: TouchpadConfig,
    longPressMs: Long,
) {
    var startTime = awaitFirstDown(requireUnconsumed = false).uptimeMillis
    var lastUptime = startTime
    var maxPointers = 1
    var totalMove = 0f
    var scrollAccum = 0f
    var scrolling = false
    var dragLocked = false

    while (true) {
        val event = awaitPointerEvent()
        lastUptime = event.changes.firstOrNull()?.uptimeMillis ?: lastUptime
        val pressed = event.changes.filter { it.pressed }
        val n = pressed.size
        if (n > maxPointers) maxPointers = n
        if (n == 0) break // 全部抬起

        when {
            dragLocked -> {
                // 拖拽锁定中：左键持续按下，仅跟随第一个按压指针移动
                val delta = pressed[0].positionChange()
                sendMove(channel, config, delta.x, delta.y)
            }
            n == 1 && !scrolling && maxPointers == 1 -> {
                val delta = pressed[0].positionChange()
                totalMove += delta.getDistance()
                // 长按拖拽锁定：按住超过长按阈值且几乎未移动 → 按下左键进入拖拽
                if (totalMove < TAP_SLOP_PX && lastUptime - startTime >= longPressMs) {
                    dragLocked = true
                    channel.send(InputEvent.PointerButton(ProtocolConsts.MouseButton.LEFT, down = true))
                }
                sendMove(channel, config, delta.x, delta.y)
            }
            n >= 2 -> {
                // 双指滚动：前两指平均 dy 累积，到阈值发一格（方向由设置决定）
                val dy = (pressed[0].positionChange().y + pressed[1].positionChange().y) / 2f
                totalMove += abs(dy)
                scrollAccum += dy
                val sign = if (config.naturalScroll) -1 else 1
                while (scrollAccum <= -config.scrollThreshold) {
                    channel.send(InputEvent.PointerScroll(0, sign)) // 上滑 → sign 正负号方向（协议正 dy=向上滚）
                    scrollAccum -= config.scrollThreshold
                    scrolling = true
                }
                while (scrollAccum >= config.scrollThreshold) {
                    channel.send(InputEvent.PointerScroll(0, -sign))
                    scrollAccum += config.scrollThreshold
                    scrolling = true
                }
            }
        }
        pressed.forEach { it.consume() }
    }

    // 全部抬起 → 收尾
    if (dragLocked) {
        // 抬起释放拖拽（左键 up；与 down 间隔为真实按住时长，消费侧不会额外插间隙）
        channel.send(InputEvent.PointerButton(ProtocolConsts.MouseButton.LEFT, down = false))
    } else if (!scrolling && totalMove < TAP_SLOP_PX) {
        val duration = lastUptime - startTime
        when {
            duration < TAP_MAX_MS -> {
                // 点按：单指=左键、双指=右键、三指及以上=中键
                val button = when (maxPointers) {
                    1 -> ProtocolConsts.MouseButton.LEFT
                    2 -> ProtocolConsts.MouseButton.RIGHT
                    else -> ProtocolConsts.MouseButton.MIDDLE
                }
                channel.send(InputEvent.PointerButton(button, down = true))
                channel.send(InputEvent.PointerButton(button, down = false))
            }
            maxPointers == 1 && duration >= longPressMs -> {
                // 长按后原地抬起（全程无移动事件）＝左键单击（press-and-hold 语义）
                channel.send(InputEvent.PointerButton(ProtocolConsts.MouseButton.LEFT, down = true))
                channel.send(InputEvent.PointerButton(ProtocolConsts.MouseButton.LEFT, down = false))
            }
        }
    }
}

private fun sendMove(
    channel: InputChannel,
    config: TouchpadConfig,
    dx: Float,
    dy: Float,
) {
    if (dx != 0f || dy != 0f) {
        channel.send(
            InputEvent.PointerMove(
                (dx * config.sensitivity).roundToInt(),
                (dy * config.sensitivity).roundToInt(),
            )
        )
    }
}

private const val TAP_SLOP_PX = 24f
private const val TAP_MAX_MS = 250
