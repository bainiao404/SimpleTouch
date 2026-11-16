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
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat

class MainActivity : ComponentActivity() {

    private lateinit var session: SessionController

    private val permissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            if (granted) session.start() else session.onPermissionDenied()
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        session = SessionController(this)
        setContent { SessionScreen(session) }
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
private fun SessionScreen(session: SessionController) {
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
            onClick = { session.start() },
            enabled = !listening.value,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(if (listening.value) "监听中…" else "启动监听")
        }

        Text("日志", style = MaterialTheme.typography.titleSmall)
        LazyColumn(modifier = Modifier.weight(1f)) {
            items(logs.toList()) { line ->
                Text(line, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}
