package com.simpletouch.feature.hotkeys

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.simpletouch.protocol.Capabilities
import com.simpletouch.protocol.InputChannel
import com.simpletouch.protocol.InputEvent
import com.simpletouch.protocol.ProtocolConsts

/**
 * M2 快捷键面板 v1（执行文档 §5 / 方案 §2-F2）：预置键网格 → HOTKEY 消息（presetId 见 protocol.md §4）。
 *
 * 能力门控：媒体/consumer 类预设依赖 PC 端 CONSUMER 位，键序/macro 类依赖 MACROS 位；
 * 不满足的按钮禁用（PC HELLO.caps 未达时按钮先全部禁用，握手完成后自动点亮）。
 * 零传输依赖：只经 [InputChannel.send] 发事件（方案文档 §4 耦合控制）。
 */
private data class Preset(val id: Int, val label: String, val consumer: Boolean)

private val PRESETS = listOf(
    Preset(ProtocolConsts.HotkeyPreset.VOLUME_UP, "音量+", consumer = true),
    Preset(ProtocolConsts.HotkeyPreset.VOLUME_DOWN, "音量−", consumer = true),
    Preset(ProtocolConsts.HotkeyPreset.VOLUME_MUTE, "静音", consumer = true),
    Preset(ProtocolConsts.HotkeyPreset.PLAY_PAUSE, "播放/暂停", consumer = true),
    Preset(ProtocolConsts.HotkeyPreset.MEDIA_PREV, "上一曲", consumer = true),
    Preset(ProtocolConsts.HotkeyPreset.MEDIA_NEXT, "下一曲", consumer = true),
    Preset(ProtocolConsts.HotkeyPreset.PAGE_UP, "PgUp", consumer = false),
    Preset(ProtocolConsts.HotkeyPreset.PAGE_DOWN, "PgDn", consumer = false),
    Preset(ProtocolConsts.HotkeyPreset.F5, "F5", consumer = false),
    Preset(ProtocolConsts.HotkeyPreset.ESC, "Esc", consumer = false),
    Preset(ProtocolConsts.HotkeyPreset.WIN_D, "Win+D", consumer = false),
)

private val COLUMNS = 4

@Composable
fun HotkeyPanelScreen(channel: InputChannel, caps: Capabilities, onBack: () -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("快捷键", style = MaterialTheme.typography.titleMedium)
            Button(onClick = onBack) { Text("返回") }
        }

        PRESETS.chunked(COLUMNS).forEach { rowPresets ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                rowPresets.forEach { preset ->
                    val enabled = if (preset.consumer) caps.consumer else caps.macros
                    Button(
                        onClick = { channel.send(InputEvent.HotkeyTrigger(preset.id)) },
                        enabled = enabled,
                        modifier = Modifier
                            .weight(1f)
                            .height(56.dp),
                        colors = ButtonDefaults.buttonColors(),
                    ) {
                        Text(preset.label, style = MaterialTheme.typography.bodyMedium)
                    }
                }
                // 末行补齐占位，保持网格对齐
                repeat(COLUMNS - rowPresets.size) {
                    androidx.compose.foundation.layout.Spacer(modifier = Modifier.weight(1f))
                }
            }
        }

        Text(
            text = when {
                caps.consumer && caps.macros -> "点击发送 HOTKEY → PC 端映射表注入（hotkeys.json 可改键序）"
                !caps.consumer && !caps.macros -> "等待 PC 握手…（能力门控：媒体键=CONSUMER，键序=MACROS）"
                !caps.consumer -> "PC 不支持媒体键（CONSUMER 位未开启），相关按钮已禁用"
                else -> "PC 不支持键序注入（MACROS 位未开启），相关按钮已禁用"
            },
            style = MaterialTheme.typography.bodySmall,
        )
    }
}
