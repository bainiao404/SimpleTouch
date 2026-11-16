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
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat
import com.simpletouch.feature.touchpad.TouchpadScreen

class MainActivity : ComponentActivity() {

    private lateinit var session: SessionController

    private val permissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            if (granted) session.start() else session.onPermissionDenied()
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        session = SessionController(this)
        // 调试/自动化入口：-e auto_touchpad true → 连接建立后自动进入触摸板页
        val autoTouchpad = intent?.getBooleanExtra("auto_touchpad", false) == true
        setContent {
            MaterialTheme {
                // 必须 remember：否则每次重组都会重置页面状态，点击导航永远失效
                var page by remember { mutableStateOf(if (autoTouchpad) "touchpad" else "connection") }
                val channel = session.channel.value
                if (page == "touchpad" && channel != null) {
                    TouchpadScreen(channel, onExit = { page = "connection" })
                } else {
                    ConnectionScreen(
                        session = session,
                        onStart = { maybeStart() },
                        touchpadEnabled = channel != null,
                        onOpenTouchpad = { page = "touchpad" },
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
    onOpenTouchpad: () -> Unit,
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

        Button(
            onClick = onOpenTouchpad,
            enabled = touchpadEnabled,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(if (touchpadEnabled) "进入触摸板" else "连接后可用")
        }

        Text("日志", style = MaterialTheme.typography.titleSmall)
        LazyColumn(modifier = Modifier.weight(1f)) {
            items(logs.toList()) { line ->
                Text(line, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}
