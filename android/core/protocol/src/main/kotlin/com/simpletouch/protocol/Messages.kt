package com.simpletouch.protocol

sealed class StMessage {
    data class Hello(val version: Int, val caps: Int, val name: String) : StMessage()
    data object Ready : StMessage()
    data class MouseMove(val dx: Short, val dy: Short) : StMessage()
    data class MouseButton(val button: Int, val action: Int) : StMessage()
    data class MouseScroll(val dx: Byte, val dy: Byte) : StMessage()
    data class Key(val code: Int, val action: Int, val mods: Int) : StMessage()
    data class Text(val fin: Boolean, val data: ByteArray) : StMessage() {
        override fun equals(other: Any?): Boolean =
            other is Text && other.fin == fin && other.data.contentEquals(data)
        override fun hashCode(): Int = fin.hashCode() * 31 + data.contentHashCode()
    }
    data class Hotkey(val presetId: Int) : StMessage()
    data class ClipboardSet(val fin: Boolean, val data: ByteArray) : StMessage() {
        override fun equals(other: Any?): Boolean =
            other is ClipboardSet && other.fin == fin && other.data.contentEquals(data)
        override fun hashCode(): Int = fin.hashCode() * 31 + data.contentHashCode()
    }
    data class Ping(val timestamp: Long) : StMessage()
    data class Pong(val timestamp: Long) : StMessage()
    data class Bye(val reason: Int) : StMessage()

    /** 兼容规则：未知 type 解码为 Unknown，由调用方丢弃（保留便于日志）。 */
    data class Unknown(val type: Int, val payload: ByteArray) : StMessage() {
        override fun equals(other: Any?): Boolean =
            other is Unknown && other.type == type && other.payload.contentEquals(payload)
        override fun hashCode(): Int = type * 31 + payload.contentHashCode()
    }
}

data class ParsedFrame(val seq: Int, val message: StMessage)

/** 协议级错误（载荷非法/超限），区别于传输层 IO 异常。 */
class ProtocolException(message: String) : java.io.IOException(message)
