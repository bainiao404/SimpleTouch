package com.simpletouch.app

import android.bluetooth.BluetoothManager
import android.content.Context
import android.os.Build
import android.os.Handler
import android.os.Looper
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import com.simpletouch.bluetooth.RfcommChannel
import com.simpletouch.bluetooth.RfcommServer
import com.simpletouch.bluetooth.RfcommServerListener
import com.simpletouch.protocol.Capabilities
import com.simpletouch.protocol.ProtocolConsts
import com.simpletouch.protocol.StMessage
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * M0 会话控制器：启动 RFCOMM 监听 → 双向 HELLO → 自动回 PONG（PC 端测 RTT）。
 * 状态用 Compose State 托管，回调统一经主线程 Handler 切换。
 */
class SessionController(context: Context) {

    val status = mutableStateOf("未启动")
    val listening = mutableStateOf(false)
    val logs = mutableStateListOf<String>()

    /** 已建立会话时可用的事件通道（触摸板等特征层的唯一发送入口）。 */
    val channel = mutableStateOf<RfcommChannel?>(null)

    private val mainHandler = Handler(Looper.getMainLooper())
    private val timeFormat = SimpleDateFormat("HH:mm:ss", Locale.US)
    private val adapter = (context.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager).adapter
    private var server: RfcommServer? = null

    private fun log(message: String) = mainHandler.post {
        logs.add("${timeFormat.format(Date())}  $message")
        while (logs.size > 200) logs.removeAt(0)
    }

    fun onPermissionDenied() {
        status.value = "蓝牙权限被拒绝，无法监听"
    }

    fun start() {
        if (server != null) return
        if (adapter == null || !adapter.isEnabled) {
            status.value = "蓝牙不可用（请检查系统蓝牙开关）"
            return
        }
        status.value = "监听中…等待 PC 连接"
        listening.value = true

        var s: RfcommServer? = null
        s = RfcommServer(adapter, object : RfcommServerListener {
            override fun onClientConnected(deviceName: String) {
                log("PC 已连接: $deviceName")
                sendHello()
                val srv = s
                if (srv != null) {
                    val ch = RfcommChannel(srv, Capabilities(textInject = false, clipboard = false, macros = false, consumer = false))
                    mainHandler.post { channel.value = ch }
                }
            }

            override fun onFrame(seq: Int, message: StMessage) {
                when (message) {
                    is StMessage.Hello -> log("HELLO: ${message.name} ver=${message.version} caps=0x${Integer.toHexString(message.caps)}")
                    is StMessage.Ready -> log("PC READY（注入器就绪）")
                    is StMessage.Ping -> {
                        // 协议 §5：PING 自动回显 PONG（PC 端以此测 RTT）
                        try {
                            server?.send(StMessage.Pong(message.timestamp))
                        } catch (e: Exception) {
                            log("回 PONG 失败: ${e.message}")
                        }
                        log("PING seq=$seq → 已回 PONG")
                    }
                    is StMessage.Bye -> log("PC 优雅断开 (reason=${message.reason})")
                    is StMessage.Unknown -> Unit // 兼容规则：未知类型丢弃
                    else -> Unit
                }
            }

            override fun onDisconnected(error: Exception?) {
                log("连接断开: ${error?.message ?: "对端正常关闭"}")
                status.value = "已断开（可重新启动监听）"
                listening.value = false
                mainHandler.post { channel.value?.close(); channel.value = null }
                server = null
            }

            override fun onListenFailed(error: Exception) {
                log("监听失败: ${error.message}")
                status.value = "监听失败"
                listening.value = false
                server = null
            }
        })
        server = s
        s.start()
    }

    /** 协议 §5：双方各自立即发 HELLO。M0 手机端 caps=0（尚无可用能力）。 */
    private fun sendHello() {
        try {
            server?.send(StMessage.Hello(ProtocolConsts.PROTOCOL_VERSION, caps = 0, name = Build.MODEL ?: "Android"))
        } catch (e: Exception) {
            log("发送 HELLO 失败: ${e.message}")
        }
    }

    fun stop() {
        channel.value?.close()
        channel.value = null
        server?.close()
        server = null
        listening.value = false
        status.value = "已停止"
    }
}
