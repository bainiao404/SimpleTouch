package com.simpletouch.protocol

import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * 流式帧读取器：处理 RFCOMM 字节流的粘包/半包。
 * 流在帧边界干净关闭返回 null；在帧中途断开抛 [EOFException]。
 */
class FrameReader(private val input: InputStream) {

    fun readFrame(): ParsedFrame? {
        val header = ByteArray(ProtocolConsts.FRAME_HEADER_SIZE)
        var total = 0
        while (total < header.size) {
            val n = input.read(header, total, header.size - total)
            if (n < 0) {
                if (total == 0) return null // 帧边界 EOF
                throw EOFException("流在帧头中途结束")
            }
            total += n
        }

        val len = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN)
            .short.toInt() and 0xFFFF
        if (len > ProtocolConsts.MAX_PAYLOAD) throw ProtocolException("payload 超上限: $len")

        val payload = ByteArray(len)
        var got = 0
        while (got < len) {
            val n = input.read(payload, got, len - got)
            if (n < 0) throw EOFException("流在 payload 中途结束")
            got += n
        }

        val frame = ByteArray(ProtocolConsts.FRAME_HEADER_SIZE + len)
        header.copyInto(frame)
        payload.copyInto(frame, ProtocolConsts.FRAME_HEADER_SIZE)
        return FrameCodec.decode(frame)
    }
}
