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
 * M1 触摸板（执行文档 §4）：
 * - 单指滑动=指针移动；单指点按=左键；双指点按=右键；双指滑动=滚轮；三指点按=中键
 * - 事件统一走 [InputChannel]（特征层零传输依赖）；移动端合并由 RfcommChannel 完成
 * 灵敏度/滚动阈值为 M1 常量，M2 转为可配置。
 */
@Composable
fun TouchpadScreen(channel: InputChannel, onExit: () -> Unit) {
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
                .pointerInput(channel) {
                    awaitEachGesture {
                        gestureLoop(channel)
                    }
                },
        )

        Text(
            "单指滑动=移动 · 点按=左键 · 双指点按=右键 · 双指滑动=滚轮 · 三指点按=中键",
            style = MaterialTheme.typography.bodySmall,
        )
    }
}

/**
 * 单次手势（第一个手指按下到全部抬起）。channel.send 为非阻塞调用，
 * 可安全运行在 awaitEachGesture 的受限挂起作用域内。
 */
private suspend fun androidx.compose.ui.input.pointer.AwaitPointerEventScope.gestureLoop(channel: InputChannel) {
    var startTime = awaitFirstDown(requireUnconsumed = false).uptimeMillis
    var lastUptime = startTime
    var maxPointers = 1
    var totalMove = 0f
    var scrollAccum = 0f
    var scrolling = false

    while (true) {
        val event = awaitPointerEvent()
        lastUptime = event.changes.firstOrNull()?.uptimeMillis ?: lastUptime
        val pressed = event.changes.filter { it.pressed }
        val n = pressed.size
        if (n > maxPointers) maxPointers = n
        if (n == 0) break // 全部抬起

        if (n == 1 && !scrolling && maxPointers == 1) {
            val delta = pressed[0].positionChange()
            totalMove += delta.getDistance()
            if (delta != androidx.compose.ui.geometry.Offset.Zero) {
                channel.send(
                    InputEvent.PointerMove(
                        (delta.x * SENSITIVITY).roundToInt(),
                        (delta.y * SENSITIVITY).roundToInt(),
                    )
                )
            }
        } else if (n >= 2) {
            // 双指滚动：前两指平均 dy 累积，到阈值发一格
            val dy = (pressed[0].positionChange().y + pressed[1].positionChange().y) / 2f
            totalMove += abs(dy)
            scrollAccum += dy
            while (scrollAccum <= -SCROLL_THRESHOLD) {
                channel.send(InputEvent.PointerScroll(0, 1)) // 上滑 → 滚轮向上（协议正 dy）
                scrollAccum -= SCROLL_THRESHOLD
                scrolling = true
            }
            while (scrollAccum >= SCROLL_THRESHOLD) {
                channel.send(InputEvent.PointerScroll(0, -1))
                scrollAccum += SCROLL_THRESHOLD
                scrolling = true
            }
        }
        pressed.forEach { it.consume() }
    }

    // 全部抬起 → 点按判定
    val duration = lastUptime - startTime
    if (!scrolling && totalMove < TAP_SLOP_PX && duration < TAP_MAX_MS) {
        val button = when (maxPointers) {
            1 -> ProtocolConsts.MouseButton.LEFT
            2 -> ProtocolConsts.MouseButton.RIGHT
            else -> ProtocolConsts.MouseButton.MIDDLE
        }
        channel.send(InputEvent.PointerButton(button, down = true))
        channel.send(InputEvent.PointerButton(button, down = false))
    }
}

private const val SENSITIVITY = 1.5f
private const val TAP_SLOP_PX = 24f
private const val TAP_MAX_MS = 250
private const val SCROLL_THRESHOLD = 24f
