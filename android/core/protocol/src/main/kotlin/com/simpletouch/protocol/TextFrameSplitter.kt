package com.simpletouch.protocol

/**
 * TEXT/CLIPBOARD_SET 分帧器（protocol.md §4）：按 UTF-8 **码点边界**切分，
 * 每帧 data ≤ maxPayload-1（留 flags 1 字节），保证不跨帧拆码点；接收方按序拼接至 FIN。
 */
object TextFrameSplitter {

    fun split(text: String, maxPayload: Int = ProtocolConsts.MAX_PAYLOAD): List<ByteArray> {
        val bytes = text.toByteArray(Charsets.UTF_8)
        val maxData = maxPayload - 1
        if (bytes.size <= maxData) return listOf(bytes)

        val frames = mutableListOf<ByteArray>()
        var start = 0
        while (start < bytes.size) {
            var end = minOf(start + maxData, bytes.size)
            // 切点若落在多字节码点的续字节上，回退到码点起始
            while (end > start && end < bytes.size && (bytes[end].toInt() and 0xC0) == 0x80) end--
            if (end == start) end = minOf(start + 4, bytes.size) // 防御：单码点超限（UTF-8 最长 4B，实际不可达）
            frames.add(bytes.copyOfRange(start, end))
            start = end
        }
        return frames
    }
}
