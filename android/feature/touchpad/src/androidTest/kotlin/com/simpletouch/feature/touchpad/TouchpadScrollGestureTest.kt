package com.simpletouch.feature.touchpad

import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performTouchInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.simpletouch.protocol.Capabilities
import com.simpletouch.protocol.InputChannel
import com.simpletouch.protocol.InputEvent
import com.simpletouch.protocol.ProtocolConsts
import java.util.concurrent.CopyOnWriteArrayList
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

/**
 * M4 复盘回归测试：双指滚动方向分支曾因 `scrollAccum += 阈值`（应为 -=）在主线程死循环，
 * 表现为 App 卡死 ANR + 堆被 UNLIMITED 队列撑爆。此 bug 自 M1 存在、三次里程碑未被发现，
 * 根因是 adb 无法注入多指、人工复核只测了向上滚——故这里固化双指双向的自动化回归。
 *
 * performTouchInput 的 TouchInjectionScope 支持真正的多指针注入。
 */
@RunWith(AndroidJUnit4::class)
class TouchpadScrollGestureTest {

    /** 记录型 InputChannel：捕获手势层发出的全部事件。 */
    private class RecordingChannel : InputChannel {
        val events = CopyOnWriteArrayList<InputEvent>()
        override val capabilities = Capabilities(textInject = false, clipboard = false, macros = false, consumer = false)
        override fun send(event: InputEvent) {
            events.add(event)
        }
    }

    @get:Rule
    val composeRule = createComposeRule()

    private fun setContent(channel: InputChannel) {
        composeRule.setContent {
            MaterialTheme {
                TouchpadScreen(channel = channel, config = TouchpadConfig(scrollThreshold = 24f), onExit = {})
            }
        }
    }

    /** 双指同步竖直滑动：steps 步 × 每步 dyPx 像素。 */
    private fun twoFingerVerticalSwipe(startY: Float, dyPx: Float, steps: Int) {
        composeRule.onRoot().performTouchInput {
            down(0, Offset(200f, startY))
            down(1, Offset(400f, startY))
            repeat(steps) { i ->
                moveTo(0, Offset(200f, startY + dyPx * (i + 1)))
                moveTo(1, Offset(400f, startY + dyPx * (i + 1)))
            }
            up(0)
            up(1)
        }
    }

    @Test
    fun twoFingerScrollDown_sendsScrollEvents_withoutHanging() {
        val channel = RecordingChannel()
        setContent(channel)
        // 手指向下 300px：scrollAccum 走 >= 分支（修复前 = 主线程死循环，此测试会超时挂死）
        twoFingerVerticalSwipe(startY = 200f, dyPx = 20f, steps = 15)
        val scrolls = channel.events.filterIsInstance<InputEvent.PointerScroll>()
        assertTrue("向下双指滚动应产生滚动事件", scrolls.isNotEmpty())
        assertTrue("不应夹带指针移动事件", channel.events.filterIsInstance<InputEvent.PointerMove>().isEmpty())
        // 自然滚动关（默认）：手指向下 → 协议负 dy（向下滚）
        assertEquals(-1, scrolls.first().dy)
        assertEquals(0, scrolls.first().dx)
    }

    @Test
    fun twoFingerScrollUp_sendsScrollEvents_withoutHanging() {
        val channel = RecordingChannel()
        setContent(channel)
        // 手指向上 300px：scrollAccum 走 <= 分支（原本就正确的路径，同样回归）
        twoFingerVerticalSwipe(startY = 600f, dyPx = -20f, steps = 15)
        val scrolls = channel.events.filterIsInstance<InputEvent.PointerScroll>()
        assertTrue("向上双指滚动应产生滚动事件", scrolls.isNotEmpty())
        // 手指向上 → 协议正 dy（向上滚）
        assertEquals(1, scrolls.first().dy)
    }

    @Test
    fun twoFingerTap_sendsRightButton() {
        val channel = RecordingChannel()
        setContent(channel)
        composeRule.onRoot().performTouchInput {
            down(0, Offset(300f, 300f))
            down(1, Offset(500f, 300f))
            up(0)
            up(1)
        }
        val buttons = channel.events.filterIsInstance<InputEvent.PointerButton>()
        assertEquals("双指点按=右键 down+up", 2, buttons.size)
        assertEquals(ProtocolConsts.MouseButton.RIGHT, buttons[0].button)
        assertEquals(true, buttons[0].down)
        assertEquals(false, buttons[1].down)
    }
}
