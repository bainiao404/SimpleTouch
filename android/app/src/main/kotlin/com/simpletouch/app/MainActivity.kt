package com.simpletouch.app

import android.Manifest
import android.annotation.SuppressLint
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat
import com.simpletouch.feature.hotkeys.HotkeyPanelScreen
import com.simpletouch.feature.keyboard.TextInjectScreen
import com.simpletouch.feature.touchpad.TouchpadScreen

class MainActivity : ComponentActivity() {

    private lateinit var session: SessionController
    private lateinit var settingsStore: SettingsStore

    private val permissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            if (granted) startListening() else session.onPermissionDenied()
        }

    // API 33+ 通知运行时权限：仅影响通知可见性，拒绝不阻塞监听流程（Pad 5 Pro = API 30 不需要）
    private val notificationPermissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // session/settings 由进程级 SimpleTouchApp 托管（IME 服务同进程共享）
        session = (application as SimpleTouchApp).session
        settingsStore = (application as SimpleTouchApp).settingsStore
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
                    page == "text" && channel != null ->
                        TextInjectScreen(
                            channel = channel,
                            caps = session.peerCaps.value,
                            pcClipboard = session.latestPcClipboard.value,
                            onCopyPcToPhone = { copyToPhoneClipboard(it) },
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
                        onStop = {
                            session.stop()
                            ConnectionService.stop(this@MainActivity)
                        },
                        touchpadEnabled = channel != null,
                        textEnabled = channel != null,
                        hotkeysEnabled = channel != null,
                        onOpenTouchpad = { page = "touchpad" },
                        onOpenText = { page = "text" },
                        onOpenHotkeys = { page = "hotkeys" },
                        onOpenSettings = { page = "settings" },
                    )
                }
            }
        }
        maybeStart()
    }

    private fun copyToPhoneClipboard(text: String) {
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText("SimpleTouch", text))
    }

    private fun startListening() {
        session.start()
        // M4：前台服务保活——监听转入后台/息屏后仍持续（配合电池优化白名单）
        ConnectionService.start(this)
    }

    private fun maybeStart() {
        if (Build.VERSION.SDK_INT >= 33 &&
            ActivityCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        if (Build.VERSION.SDK_INT >= 31 &&
            ActivityCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED
        ) {
            permissionLauncher.launch(Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            startListening()
        }
    }

    override fun onDestroy() {
        // session 托管在 Application，Activity 销毁不停止会话（IME/后台仍可用）
        super.onDestroy()
    }
}

@Composable
private fun ConnectionScreen(
    session: SessionController,
    onStart: () -> Unit,
    onStop: () -> Unit,
    touchpadEnabled: Boolean,
    textEnabled: Boolean,
    hotkeysEnabled: Boolean,
    onOpenTouchpad: () -> Unit,
    onOpenText: () -> Unit,
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

        // M4：启动/停止切换——监听常驻（断线自动等重连），用户可主动停止
        Button(
            onClick = { if (listening.value) onStop() else onStart() },
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(if (listening.value) "停止监听" else "启动监听")
        }

        BatteryOptimizationHint()

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
                onClick = onOpenText,
                enabled = textEnabled,
                modifier = Modifier.weight(1f),
            ) {
                Text(if (textEnabled) "文本" else "文本（连接后可用）")
            }
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Button(
                onClick = onOpenHotkeys,
                enabled = hotkeysEnabled,
                modifier = Modifier.weight(1f),
            ) {
                Text(if (hotkeysEnabled) "快捷键" else "快捷键（连接后可用）")
            }
            Button(
                onClick = onOpenSettings,
                modifier = Modifier.weight(1f),
            ) {
                Text("设置")
            }
        }

        Text("日志", style = MaterialTheme.typography.titleSmall)
        LazyColumn(modifier = Modifier.weight(1f)) {
            items(logs.toList()) { line ->
                Text(line, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}

/**
 * M4 后台保活引导：未加白名单时显示——电池优化白名单一键申请 + MIUI 手动权限提示。
 * 息屏/后台被杀是 M0 已知断连主因（执行文档 §10 风险表「后台被杀」行）。
 */
@Composable
private fun BatteryOptimizationHint() {
    val context = LocalContext.current
    val powerManager = remember { context.getSystemService(Context.POWER_SERVICE) as PowerManager }
    var whitelisted = remember { powerManager.isIgnoringBatteryOptimizations(context.packageName) }
    if (whitelisted) return
    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text("后台保活提示", style = MaterialTheme.typography.titleSmall)
        Text(
            "为避免息屏/后台断连：① 点下方按钮加入电池优化白名单；" +
                "② MIUI 设备请在「系统设置→应用管理→SimpleTouch」中开启「自启动」「后台弹出界面」",
            style = MaterialTheme.typography.bodySmall,
        )
        Button(onClick = {
            @SuppressLint("BatteryLife")
            val intent = Intent(
                Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,
                Uri.parse("package:${context.packageName}"),
            )
            context.startActivity(intent)
            whitelisted = true // 乐观置已处理；用户若拒绝，下次冷启动会再次提示
        }) {
            Text("加入电池优化白名单")
        }
    }
}

/** M2 设置页：灵敏度 / 滚动方向 / 滚动阈值，改动即时生效并由 SettingsStore 落盘 JSON。 */
@Composable
private fun SettingsScreen(store: SettingsStore, onBack: () -> Unit) {    val config = store.config

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
