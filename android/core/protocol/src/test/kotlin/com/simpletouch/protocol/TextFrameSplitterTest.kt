package com.simpletouch.protocol

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import java.nio.ByteBuffer
import java.nio.charset.Charset
import java.nio.charset.CodingErrorAction

/** M3 分帧器单测：协议要求「data 必须是完整 UTF-8 码点序列，不跨帧拆码点」。 */
class TextFrameSplitterTest {

    private val utf8 = Charset.forName("UTF-8")

    /** 每帧必须能独立按 UTF-8 严格解码（严格模式遇残缺序列即抛错），即帧尾不截断码点。 */
    private fun assertFrameIsCompleteCodePoints(frame: ByteArray) {
        val decoder = utf8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
        decoder.decode(ByteBuffer.wrap(frame)) // 拆了码点会抛 CharacterCodingException
    }

    @Test
    fun 短文本_单帧即UTF8字节() {
        val text = "你好 SimpleTouch"
        val frames = TextFrameSplitter.split(text)
        assertEquals(1, frames.size)
        assertEquals(text.toByteArray(utf8).toList(), frames[0].toList())
    }

    @Test
    fun 中文长文_多帧且不跨帧拆码点() {
        val text = "剪贴板同步测试".repeat(10) // 每个汉字 3 字节，共 210B
        val frames = TextFrameSplitter.split(text, maxPayload = 16) // maxData=15 → 切点会落进汉字内部
        assertTrue(frames.size > 1)
        for (f in frames) {
            assertTrue(f.size <= 15, "帧超限: ${f.size}")
            assertTrue(!isContinuation(f[0]), "帧首字节是续字节")
            assertFrameIsCompleteCodePoints(f)
        }
        assertEquals(text.toByteArray(utf8).toList(), frames.flatMap { it.toList() })
    }

    @Test
    fun emoji四字节码点_保留完整() {
        val text = "ab🚀cd🚀ef".repeat(3) // 🚀 = UTF-8 4 字节
        val frames = TextFrameSplitter.split(text, maxPayload = 8)
        assertTrue(frames.size > 1)
        for (f in frames) {
            assertTrue(!isContinuation(f[0]))
            assertFrameIsCompleteCodePoints(f)
        }
        assertEquals(text.toByteArray(utf8).toList(), frames.flatMap { it.toList() })
        assertEquals(text, String(frames.reduce { a, b -> a + b }, utf8))
    }

    @Test
    fun 切点恰在码点前_回退避免截断() {
        // "aaa你"：3 字节 ASCII + 3 字节汉字；maxData=4 时切点落在"你"内部 → 回退到 3
        val frames = TextFrameSplitter.split("aaa你", maxPayload = 5)
        assertEquals(
            listOf("aaa".toByteArray(utf8).toList(), "你".toByteArray(utf8).toList()),
            frames.map { it.toList() },
        )
    }

    @Test
    fun 空文本_单空帧() {
        val frames = TextFrameSplitter.split("")
        assertEquals(1, frames.size)
        assertEquals(0, frames[0].size)
    }

    private fun isContinuation(b: Byte) = (b.toInt() and 0xC0) == 0x80
}
