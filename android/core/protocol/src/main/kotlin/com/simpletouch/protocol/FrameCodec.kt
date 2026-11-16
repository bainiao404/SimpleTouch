package com.simpletouch.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

/** 帧编解码：[len:u16][type:u8][seq:u8][payload]，小端。与 docs/protocol.md §2 对应。 */
object FrameCodec {

    fun typeOf(message: StMessage): Int = when (message) {
        is StMessage.Hello -> ProtocolConsts.MsgType.HELLO
        is StMessage.Ready -> ProtocolConsts.MsgType.READY
        is StMessage.MouseMove -> ProtocolConsts.MsgType.MOUSE_MOVE
        is StMessage.MouseButton -> ProtocolConsts.MsgType.MOUSE_BUTTON
        is StMessage.MouseScroll -> ProtocolConsts.MsgType.MOUSE_SCROLL
        is StMessage.Key -> ProtocolConsts.MsgType.KEY
        is StMessage.Text -> ProtocolConsts.MsgType.TEXT
        is StMessage.Hotkey -> ProtocolConsts.MsgType.HOTKEY
        is StMessage.ClipboardSet -> ProtocolConsts.MsgType.CLIPBOARD_SET
        is StMessage.Ping -> ProtocolConsts.MsgType.PING
        is StMessage.Pong -> ProtocolConsts.MsgType.PONG
        is StMessage.Bye -> ProtocolConsts.MsgType.BYE
        is StMessage.Unknown -> message.type
    }

    fun encode(message: StMessage, seq: Int): ByteArray {
        val payload = encodePayload(message)
        require(payload.size <= ProtocolConsts.MAX_PAYLOAD) { "payload ${payload.size} 超上限 ${ProtocolConsts.MAX_PAYLOAD}" }

        val frame = ByteArray(ProtocolConsts.FRAME_HEADER_SIZE + payload.size)
        val bb = ByteBuffer.wrap(frame).order(ByteOrder.LITTLE_ENDIAN)
        bb.putShort(payload.size.toShort())
        bb.put(typeOf(message).toByte())
        bb.put(seq.toByte())
        bb.put(payload)
        return frame
    }

    fun decode(frame: ByteArray, offset: Int = 0, count: Int = frame.size - offset): ParsedFrame {
        if (count < ProtocolConsts.FRAME_HEADER_SIZE) throw ProtocolException("帧不完整：不足 4 字节帧头")
        val bb = ByteBuffer.wrap(frame, offset, count).order(ByteOrder.LITTLE_ENDIAN)
        val len = bb.short.toInt() and 0xFFFF
        if (len > ProtocolConsts.MAX_PAYLOAD) throw ProtocolException("payload 超上限: $len")
        if (count < ProtocolConsts.FRAME_HEADER_SIZE + len) throw ProtocolException("帧不完整：payload 截断")

        val type = bb.get().toInt() and 0xFF
        val seq = bb.get().toInt() and 0xFF
        val payload = ByteArray(len)
        bb.get(payload)
        return ParsedFrame(seq, decodePayload(type, payload))
    }

    private fun encodePayload(m: StMessage): ByteArray = when (m) {
        is StMessage.Hello -> {
            val name = m.name.toByteArray(Charsets.UTF_8)
            check(name.size <= 255) { "name 超过 255 字节" }
            ByteBuffer.allocate(7 + name.size).order(ByteOrder.LITTLE_ENDIAN)
                .putShort(m.version.toShort())
                .putInt(m.caps)
                .put(name.size.toByte())
                .put(name)
                .array()
        }
        is StMessage.Ready -> ByteArray(0)
        is StMessage.MouseMove -> short2(m.dx, m.dy)
        is StMessage.MouseButton -> byteArrayOf(m.button.toByte(), m.action.toByte())
        is StMessage.MouseScroll -> byteArrayOf(m.dx, m.dy)
        is StMessage.Key -> byteArrayOf(m.code.toByte(), m.action.toByte(), m.mods.toByte())
        is StMessage.Text -> fragment(m.fin, m.data)
        is StMessage.Hotkey -> short2(m.presetId.toShort())
        is StMessage.ClipboardSet -> fragment(m.fin, m.data)
        is StMessage.Ping -> long8(m.timestamp)
        is StMessage.Pong -> long8(m.timestamp)
        is StMessage.Bye -> byteArrayOf(m.reason.toByte())
        is StMessage.Unknown -> m.payload.copyOf()
    }

    private fun decodePayload(type: Int, p: ByteArray): StMessage = when (type) {
        ProtocolConsts.MsgType.HELLO -> {
            requireMin(p, 7)
            val nameLen = p[6].toInt() and 0xFF
            requireLen(p, 7 + nameLen)
            StMessage.Hello(
                version = (p[0].toInt() and 0xFF) or ((p[1].toInt() and 0xFF) shl 8),
                caps = ByteBuffer.wrap(p, 2, 4).order(ByteOrder.LITTLE_ENDIAN).int,
                name = String(p, 7, nameLen, Charsets.UTF_8),
            )
        }
        ProtocolConsts.MsgType.READY -> requireLen(p, 0, StMessage.Ready)
        ProtocolConsts.MsgType.MOUSE_MOVE -> {
            requireLen(p, 4)
            val bb = ByteBuffer.wrap(p).order(ByteOrder.LITTLE_ENDIAN)
            StMessage.MouseMove(bb.short, bb.short)
        }
        ProtocolConsts.MsgType.MOUSE_BUTTON -> {
            requireLen(p, 2)
            StMessage.MouseButton(p[0].toInt() and 0xFF, p[1].toInt() and 0xFF)
        }
        ProtocolConsts.MsgType.MOUSE_SCROLL -> {
            requireLen(p, 2)
            StMessage.MouseScroll(p[0], p[1])
        }
        ProtocolConsts.MsgType.KEY -> {
            requireLen(p, 3)
            StMessage.Key(p[0].toInt() and 0xFF, p[1].toInt() and 0xFF, p[2].toInt() and 0xFF)
        }
        ProtocolConsts.MsgType.TEXT -> {
            requireMin(p, 1)
            StMessage.Text(p[0].toInt() and 0x01 != 0, p.copyOfRange(1, p.size))
        }
        ProtocolConsts.MsgType.HOTKEY -> {
            requireLen(p, 2)
            StMessage.Hotkey((p[0].toInt() and 0xFF) or ((p[1].toInt() and 0xFF) shl 8))
        }
        ProtocolConsts.MsgType.CLIPBOARD_SET -> {
            requireMin(p, 1)
            StMessage.ClipboardSet(p[0].toInt() and 0x01 != 0, p.copyOfRange(1, p.size))
        }
        ProtocolConsts.MsgType.PING -> {
            requireLen(p, 8)
            StMessage.Ping(ByteBuffer.wrap(p).order(ByteOrder.LITTLE_ENDIAN).long)
        }
        ProtocolConsts.MsgType.PONG -> {
            requireLen(p, 8)
            StMessage.Pong(ByteBuffer.wrap(p).order(ByteOrder.LITTLE_ENDIAN).long)
        }
        ProtocolConsts.MsgType.BYE -> {
            requireLen(p, 1)
            StMessage.Bye(p[0].toInt() and 0xFF)
        }
        else -> StMessage.Unknown(type, p)
    }

    private fun fragment(fin: Boolean, data: ByteArray): ByteArray {
        val payload = ByteArray(1 + data.size)
        payload[0] = if (fin) 0x01 else 0x00
        data.copyInto(payload, 1)
        return payload
    }

    private fun short2(a: Short, b: Short? = null): ByteArray {
        val n = if (b == null) 2 else 4
        val bb = ByteBuffer.allocate(n).order(ByteOrder.LITTLE_ENDIAN)
        bb.putShort(a)
        b?.let { bb.putShort(it) }
        return bb.array()
    }

    private fun long8(v: Long): ByteArray =
        ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).putLong(v).array()

    private fun requireLen(p: ByteArray, expected: Int) {
        check(p.size == expected) { "载荷长度 ${p.size} ≠ 期望 $expected" }
    }

    private fun requireLen(p: ByteArray, expected: Int, message: StMessage): StMessage {
        requireLen(p, expected)
        return message
    }

    private fun requireMin(p: ByteArray, min: Int) {
        check(p.size >= min) { "载荷长度 ${p.size} < 最低 $min" }
    }
}
