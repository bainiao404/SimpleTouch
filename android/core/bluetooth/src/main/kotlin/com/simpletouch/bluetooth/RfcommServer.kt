package com.simpletouch.bluetooth

import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothServerSocket
import android.bluetooth.BluetoothSocket
import com.simpletouch.protocol.FrameCodec
import com.simpletouch.protocol.FrameReader
import com.simpletouch.protocol.ProtocolConsts
import com.simpletouch.protocol.StMessage
import java.io.IOException
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicInteger

/** RFCOMM 服务端回调（M0 最小集；连接状态机/自动重连在 M4 完善）。 */
interface RfcommServerListener {
    fun onClientConnected(deviceName: String)
    fun onFrame(seq: Int, message: StMessage)
    fun onDisconnected(error: Exception?)
    fun onListenFailed(error: Exception)
}

/**
 * Android 侧 RFCOMM Server（方案文档 §4：ITransport 的首个实现——服务端角色）。
 * - `listenUsingRfcommWithServiceRecord` 建安全 SDP 服务（要求先系统配对）
 * - accept → 帧读循环；发送侧 synchronized 串行写
 * - M0 单客户端假设；API 31+ 需已授予 BLUETOOTH_CONNECT（由 App 层负责）
 */
class RfcommServer(
    private val adapter: BluetoothAdapter,
    private val listener: RfcommServerListener,
) {
    private var serverSocket: BluetoothServerSocket? = null
    private var socket: BluetoothSocket? = null
    private val txSeq = AtomicInteger(0)

    @Volatile
    private var running = false

    private val executor = Executors.newCachedThreadPool { r ->
        Thread(r, "simpletouch-bt").apply { isDaemon = false }
    }

    fun start() {
        if (running) return
        running = true
        executor.execute { acceptLoop() }
    }

    private fun acceptLoop() {
        try {
            val server = adapter.listenUsingRfcommWithServiceRecord(
                ProtocolConsts.RFCOMM_SERVICE_NAME,
                UUID.fromString(ProtocolConsts.RFCOMM_SERVICE_UUID),
            )
            serverSocket = server
            while (running) {
                val client = server.accept() // 阻塞等待 PC 连接
                handleClient(client)
                break // M0：单客户端
            }
        } catch (e: IOException) {
            if (running) listener.onListenFailed(e) else listener.onDisconnected(null)
        } finally {
            try { serverSocket?.close() } catch (_: IOException) { /* 已关闭 */ }
        }
    }

    private fun handleClient(client: BluetoothSocket) {
        socket = client
        listener.onClientConnected(client.remoteDevice.name ?: client.remoteDevice.address)
        executor.execute { readLoop(client) }
    }

    private fun readLoop(client: BluetoothSocket) {
        try {
            val reader = FrameReader(client.inputStream)
            while (running) {
                val frame = reader.readFrame() ?: break
                listener.onFrame(frame.seq, frame.message)
            }
            listener.onDisconnected(null)
        } catch (e: Exception) {
            if (running) listener.onDisconnected(e) else listener.onDisconnected(null)
        }
    }

    /** 线程安全发送（seq 内部自增）。 */
    fun send(message: StMessage) {
        val s = socket ?: throw IOException("未连接")
        val seq = txSeq.incrementAndGet() and 0xFF
        val frame = FrameCodec.encode(message, seq)
        synchronized(this) {
            s.outputStream.write(frame)
            s.outputStream.flush()
        }
    }

    fun close() {
        running = false
        try { socket?.close() } catch (_: IOException) { /* 已关闭 */ }
        try { serverSocket?.close() } catch (_: IOException) { /* 已关闭 */ }
    }
}
