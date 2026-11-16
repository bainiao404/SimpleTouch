package com.simpletouch.app

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat
import com.simpletouch.feature.hotkeys.HotkeyPanelScreen
import com.simpletouch.feature.touchpad.TouchpadScreen

class MainActivity : ComponentActivity() {

    private lateinit var session: SessionController
    private lateinit var settingsStore: SettingsStore

    private val permissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            if (granted) session.start() else session.onPermissionDenied()
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        session = SessionController(this)
        settingsStore = SettingsStore(this)
        // 调试/自动化入口：-e auto_touchpad true → 连接建立后自动进入触摸板页
        val autoTouchpad = intent?.getBooleanExtra("auto_touchpad", false) == true
        setContent {
            MaterialTheme {
                // 必须 remember：否则每次重组都会重置页面状态，点击导航永远失效
                var page by remember { mutableStateOf(if (autoTouchpad) "touchpad" else "connection") }
                val channel = session.channel.value
                when {
                    page == "touchpad" && channel != null ->
                        TouchpadScreen(
                            channel = channel,
                            config = settingsStore.config,
                            onExit = { page = "connection" },
                        )
                    page == "hotkeys" && channel != null ->
                        HotkeyPanelScreen(
                            channel = channel,
                            caps = session.peerCaps.value,
                            onBack = { page = "connection" },
                        )
                    page == "settings" ->
                        SettingsScreen(
                            store = settingsStore,
                            onBack = { page = "connection" },
                        )
                    else -> ConnectionScreen(
                        session = session,
                        onStart = { maybeStart() },
                        touchpadEnabled = channel != null,
                        hotkeysEnabled = channel != null,
                        onOpenTouchpad = { page = "touchpad" },
                        onOpenHotkeys = { page = "hotkeys" },
                        onOpenSettings = { page = "settings" },
                    )
                }
            }
        }
        maybeStart()
    }

    private fun maybeStart() {
        if (Build.VERSION.SDK_INT >= 31 &&
            ActivityCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED
        ) {
            permissionLauncher.launch(Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            session.start()
        }
    }

    override fun onDestroy() {
        session.stop()
        super.onDestroy()
    }
}

@Composable
private fun ConnectionScreen(
    session: SessionController,
    onStart: () -> Unit,
    touchpadEnabled: Boolean,
    hotkeysEnabled: Boolean,
    onOpenTouchpad: () -> Unit,
    onOpenHotkeys: () -> Unit,
    onOpenSettings: () -> Unit,
) {
    val status = session.status
    val listening = session.listening
    val logs = session.logs

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text("SimpleTouch", style = MaterialTheme.typography.headlineMedium)
        Text(status.value, style = MaterialTheme.typography.bodyMedium)

        Button(
            onClick = onStart,
            enabled = !listening.value,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(if (listening.value) "监听中…" else "启动监听")
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Button(
                onClick = onOpenTouchpad,
                enabled = touchpadEnabled,
                modifier = Modifier.weight(1f),
            ) {
                Text(if (touchpadEnabled) "触摸板" else "触摸板（连接后可用）")
            }
            Button(
                onClick = onOpenHotkeys,
                enabled = hotkeysEnabled,
                modifier = Modifier.weight(1f),
            ) {
                Text(if (hotkeysEnabled) "快捷键" else "快捷键（连接后可用）")
            }
        }

        Button(
            onClick = onOpenSettings,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("设置")
        }

        Text("日志", style = MaterialTheme.typography.titleSmall)
        LazyColumn(modifier = Modifier.weight(1f)) {
            items(logs.toList()) { line ->
                Text(line, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}

/** M2 设置页：灵敏度 / 滚动方向 / 滚动阈值，改动即时生效并由 SettingsStore 落盘 JSON。 */
@Composable
private fun SettingsScreen(store: SettingsStore, onBack: () -> Unit) {
    val config = store.config

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text("设置", style = MaterialTheme.typography.titleMedium)

        Text("指针灵敏度  %.1f×".format(config.sensitivity), style = MaterialTheme.typography.bodyMedium)
        Slider(
            value = config.sensitivity,
            onValueChange = { store.update(sensitivity = (it * 10).toInt() / 10f) },
            valueRange = 0.5f..3f,
            steps = 24, // 0.1 步进
        )
        Text("提示：Windows 指针加速会再叠加（综合 ≈2.2×），感觉过快就把滑杆调低", style = MaterialTheme.typography.bodySmall)

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column {
                Text("自然滚动", style = MaterialTheme.typography.bodyMedium)
                Text(
                    if (config.naturalScroll) "开：内容随手指示方向" else "关：传统滚轮方向",
                    style = MaterialTheme.typography.bodySmall,
                )
            }
            Switch(
                checked = config.naturalScroll,
                onCheckedChange = { store.update(naturalScroll = it) },
            )
        }

        Text("滚动阈值  %.0f px/格".format(config.scrollThreshold), style = MaterialTheme.typography.bodyMedium)
        Slider(
            value = config.scrollThreshold,
            onValueChange = { store.update(scrollThreshold = ((it / 4).toInt() * 4f)) },
            valueRange = 8f..64f,
            steps = 13, // 4 px 步进
        )

        Button(onClick = onBack, modifier = Modifier.fillMaxWidth()) {
            Text("返回")
        }
    }
}
