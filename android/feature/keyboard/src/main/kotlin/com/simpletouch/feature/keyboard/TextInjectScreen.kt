package com.simpletouch.feature.keyboard

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.simpletouch.protocol.Capabilities
import com.simpletouch.protocol.InputChannel
import com.simpletouch.protocol.InputEvent

/**
 * M3 文本直注页（执行文档 §6）：
 * - 直注：手机系统输入法在下方输入框打字（含中文）→ TEXT 消息 → PC KEYEVENTF_UNICODE 直注
 *   （中文无需 PC 端切输入法；自研 IME 的键盘见 SimpleTouchIme）
 * - 剪贴板双向：本页文本 → PC 剪贴板（CLIPBOARD_SET 手机→PC）；PC 剪贴板变更自动推送显示（PC→手机）
 * 能力门控：直注需 PC caps.TEXT_INJECT，剪贴板需 caps.CLIPBOARD。
 */
@Composable
fun TextInjectScreen(
    channel: InputChannel,
    caps: Capabilities,
    pcClipboard: String?,
    onCopyPcToPhone: (String) -> Unit,
    onBack: () -> Unit,
) {
    var draft by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("文本 / 剪贴板", style = MaterialTheme.typography.titleMedium)
            Button(onClick = onBack) { Text("返回") }
        }

        Text("直注到 PC 焦点窗口（中文由手机输入法组词）", style = MaterialTheme.typography.bodySmall)
        OutlinedTextField(
            value = draft,
            onValueChange = { draft = it },
            modifier = Modifier
                .fillMaxWidth()
                .height(160.dp),
            placeholder = { Text("在此输入要打到 PC 的文字…") },
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(
                onClick = {
                    channel.send(InputEvent.TextInput(draft))
                    draft = ""
                },
                enabled = caps.textInject && draft.isNotEmpty(),
            ) {
                Text("直注发送 (TEXT)")
            }
            OutlinedButton(
                onClick = { channel.send(InputEvent.ClipboardSet(draft)) },
                enabled = caps.clipboard && draft.isNotEmpty(),
            ) {
                Text("发送到 PC 剪贴板")
            }
        }
        if (!caps.textInject || !caps.clipboard) {
            Text(
                "能力门控：" + buildList {
                    if (!caps.textInject) add("TEXT_INJECT 未开（直注禁用）")
                    if (!caps.clipboard) add("CLIPBOARD 未开（剪贴板禁用）")
                }.joinToString("；"),
                style = MaterialTheme.typography.bodySmall,
            )
        }

        Text("PC 剪贴板（PC 端变更自动推送）", style = MaterialTheme.typography.bodySmall)
        Text(
            pcClipboard?.takeIf { it.isNotEmpty() } ?: "（空）",
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier
                .fillMaxWidth()
                .height(80.dp),
        )
        OutlinedButton(
            onClick = { pcClipboard?.let(onCopyPcToPhone) },
            enabled = pcClipboard != null,
        ) {
            Text("复制到手机剪贴板")
        }

        Text(
            "提示：自研键盘（直通/直注 + 触摸条）需在系统设置启用「SimpleTouch 键盘」并切换为当前输入法",
            style = MaterialTheme.typography.bodySmall,
        )
    }
}
