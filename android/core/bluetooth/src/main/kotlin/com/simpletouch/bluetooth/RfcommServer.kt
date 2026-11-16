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

/** RFCOMM 服务端回调。M4 语义：serverSocket 常驻，[onDisconnected] 仅表示单会话结束，服务端继续监听。 */
interface RfcommServerListener {
    fun onClientConnected(deviceName: String)
    fun onFrame(seq: Int, message: StMessage)
    fun onDisconnected(error: Exception?)
    fun onListenFailed(error: Exception)
}

/**
 * Android 侧 RFCOMM Server（方案文档 §4：ITransport 的首个实现——服务端角色）。
 * - `listenUsingRfcommWithServiceRecord` 建安全 SDP 服务（要求先系统配对）
 * - M4 多会话循环：accept → 会话（阻塞至断开）→ 继续 accept；serverSocket 全程保持打开，
 *   SDP 记录持续注册，PC 断线重连时可直接重新发现服务（M0 单会话限制的正式解）
 * - 发送侧 synchronized 串行写；API 31+ 需已授予 BLUETOOTH_CONNECT（由 App 层负责）
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

    private val executor = Executors.newSingleThreadExecutor { r ->
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
                val client = try {
                    server.accept() // 阻塞等待 PC 连接
                } catch (e: IOException) {
                    // close() 会解除 accept 阻塞（用户停止）；蓝牙开关/适配器异常同样走这里——
                    // running 仍在时必须上报，否则上层停留在「监听中」假象
                    if (running) listener.onListenFailed(e)
                    break
                }
                runSession(client)
            }
        } catch (e: IOException) {
            if (running) listener.onListenFailed(e) else listener.onDisconnected(null)
        } finally {
            try { serverSocket?.close() } catch (_: IOException) { /* 已关闭 */ }
        }
    }

    /** 单个会话：读循环阻塞至断开；结束后清理并回到 accept。会话之间严格串行。 */
    private fun runSession(client: BluetoothSocket) {
        socket = client
        listener.onClientConnected(client.remoteDevice.name ?: client.remoteDevice.address)
        try {
            val reader = FrameReader(client.inputStream)
            while (running) {
                val frame = reader.readFrame() ?: break
                listener.onFrame(frame.seq, frame.message)
            }
            listener.onDisconnected(null)
        } catch (e: Exception) {
            listener.onDisconnected(e)
        } finally {
            try { client.close() } catch (_: IOException) { /* 已关闭 */ }
            socket = null
        }
    }

    /** 线程安全发送（seq 内部自增）。仅会话期间可调用。 */
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
