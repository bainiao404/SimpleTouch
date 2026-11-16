package com.simpletouch.protocol

import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue
import org.json.JSONArray
import org.json.JSONObject

class FrameCodecTest {

    private data class VectorCase(val name: String, val desc: String, val bytes: ByteArray)

    /** 从仓库根向上查找 docs/protocol/test-vectors.json（与 PC 端共用同一向量源）。 */
    private fun loadVectors(): List<VectorCase> {
        var dir: File? = File(System.getProperty("user.dir")).absoluteFile
        while (dir != null) {
            val candidate = File(dir, "docs/protocol/test-vectors.json")
            if (candidate.exists()) {
                val root = JSONObject(candidate.readText(Charsets.UTF_8))
                val arr: JSONArray = root.getJSONArray("vectors")
                return (0 until arr.length()).map { i ->
                    val el = arr.getJSONObject(i)
                    VectorCase(el.getString("name"), el.getString("desc"), hexToBytes(el.getString("hex")))
                }
            }
            dir = dir.parentFile
        }
        throw IllegalStateException("未找到 docs/protocol/test-vectors.json（自 ${System.getProperty("user.dir")} 向上查找）")
    }

    private fun hexToBytes(hex: String): ByteArray =
        ByteArray(hex.length / 2) { i ->
            ((Character.digit(hex[i * 2], 16) shl 4) + Character.digit(hex[i * 2 + 1], 16)).toByte()
        }

    /** 核心断言：解码 → 以相同 seq 重编码 → 与向量逐字节一致（双向证明编解码器符合规范）。 */
    @Test
    fun 向量解码与重编码逐字节一致() {
        loadVectors().forEach { v ->
            val frame = FrameCodec.decode(v.bytes)
            val reEncoded = FrameCodec.encode(frame.message, frame.seq)
            assertTrue(v.bytes.contentEquals(reEncoded), "向量 ${v.name} 往返不一致: ${reEncoded.joinToString("") { "%02X".format(it) }}")
        }
    }

    @Test
    fun hello字段断言() {
        val frame = FrameCodec.decode(hexToBytes("1800010101000F0000001153696D706C65546F7563682E4167656E74"))
        val hello = assertIs<StMessage.Hello>(frame.message)
        assertEquals(1, hello.version)
        assertEquals(0x0F, hello.caps)
        assertEquals("SimpleTouch.Agent", hello.name)
        assertEquals(0x01, frame.seq)
    }

    @Test
    fun mouseMove负数增量() {
        val frame = FrameCodec.decode(hexToBytes("040010040F00E2FF"))
        val mv = assertIs<StMessage.MouseMove>(frame.message)
        assertEquals(15, mv.dx)
        assertEquals(-30, mv.dy)
    }

    @Test
    fun text中文单帧FIN() {
        val frame = FrameCodec.decode(hexToBytes("0700210801E4BDA0E5A5BD"))
        val text = assertIs<StMessage.Text>(frame.message)
        assertEquals(true, text.fin)
        assertEquals("你好", String(text.data, Charsets.UTF_8))
    }

    @Test
    fun 未知类型解码为Unknown且可重编码() {
        val bytes = hexToBytes("01007F012A")
        val frame = FrameCodec.decode(bytes)
        val unknown = assertIs<StMessage.Unknown>(frame.message)
        assertEquals(0x7F, unknown.type)
        assertTrue(bytes.contentEquals(FrameCodec.encode(unknown, frame.seq)))
    }

    @Test
    fun 截断帧抛ProtocolException() {
        assertFailsWith<ProtocolException> { FrameCodec.decode(hexToBytes("080040")) }
    }
}
