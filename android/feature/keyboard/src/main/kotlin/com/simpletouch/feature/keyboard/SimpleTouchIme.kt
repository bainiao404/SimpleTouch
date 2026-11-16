package com.simpletouch.feature.keyboard

import android.content.Context
import android.graphics.Color
import android.inputmethodservice.InputMethodService
import android.view.MotionEvent
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import com.simpletouch.protocol.InputChannel
import com.simpletouch.protocol.InputEvent
import com.simpletouch.protocol.ProtocolConsts
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * M3 自研 IME（执行文档 §6 / 方案 §2-F3）：
 * - 直通模式：按键 → KEY 消息（HID Usage ID），中文由 PC 端输入法组词
 * - 直注模式：字符 → TEXT 消息（Unicode 直注，无需 PC 切输入法；本键盘输出 ASCII，
 *   中文走 App 内「文本」页由系统输入法产生）
 * - 键盘上方触摸条：滑动=指针移动，点按=左键（边打字边滑动，方案 F3）
 * 输出全部经 InputChannel（特征层零传输依赖）；不向手机端字段提交文本。
 */
class SimpleTouchIme : InputMethodService() {

    private enum class Mode { PASSTHROUGH, DIRECT_INJECT }

    private var mode = Mode.DIRECT_INJECT
    private var shiftOn = false
    private var ctrlOn = false
    private var altOn = false

    private lateinit var statusView: TextView
    private lateinit var modeButton: Button
    private val modButtons = mutableMapOf<String, Button>()

    override fun onCreateInputView(): View {
        val ctx = this
        val density = resources.displayMetrics.density
        val root = LinearLayout(ctx).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.rgb(232, 236, 242))
        }

        root.addView(buildTouchStrip(ctx))

        val statusRow = LinearLayout(ctx).apply { orientation = LinearLayout.HORIZONTAL }
        statusView = TextView(ctx).apply {
            setPadding((16 * density).toInt(), (12 * density).toInt(), (8 * density).toInt(), (12 * density).toInt())
            setTextColor(Color.BLACK)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        modeButton = Button(ctx).apply {
            setOnClickListener {
                mode = if (mode == Mode.PASSTHROUGH) Mode.DIRECT_INJECT else Mode.PASSTHROUGH
                refreshStatus()
            }
        }
        statusRow.addView(statusView)
        statusRow.addView(modeButton)
        root.addView(statusRow)

        // QWERTY 布局：数字行 + 三字母行（含行尾符号/修饰键）+ 底部功能行
        val keyRows: List<List<KeyDef>> = listOf(
            "1234567890".map(::digitKey),
            "qwertyuiop".map(::letterKey),
            "asdfghjkl".map(::letterKey) + KeyDef("-", 0x2D),
            listOf(KeyDef("⇧", 0, isMod = true)) +
                "zxcvbnm".map(::letterKey) +
                listOf(KeyDef("⌫", 0x2A, kind = KeyKind.BACKSPACE)),
        )
        keyRows.forEach { root.addView(buildKeyRow(ctx, it)) }
        root.addView(
            buildKeyRow(
                ctx,
                listOf(
                    KeyDef("^", 0, isMod = true),
                    KeyDef("⎇", 0, isMod = true),
                    KeyDef(",", 0x36),
                    KeyDef(" ", 0x2C),
                    KeyDef(".", 0x37),
                    KeyDef("⏎", 0x28, kind = KeyKind.ENTER),
                ),
            )
        )
        refreshMods()
        refreshStatus()
        return root
    }

    override fun onStartInputView(info: android.view.inputmethod.EditorInfo?, restarting: Boolean) {
        super.onStartInputView(info, restarting)
        refreshStatus()
    }

    // ---- UI 构建 ----

    private fun letterKey(c: Char) = KeyDef(c.toString(), 0x04 + (c - 'a'))

    private fun digitKey(c: Char) =
        if (c == '0') KeyDef("0", 0x27) else KeyDef(c.toString(), 0x1E + (c - '1'))

    private fun buildTouchStrip(ctx: Context): View {
        val density = resources.displayMetrics.density
        val strip = TouchStripView(ctx)
        strip.layoutParams = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT, (56 * density).toInt(),
        ).apply { setMargins((4 * density).toInt(), (8 * density).toInt(), (4 * density).toInt(), 0) }
        strip.onMove = { dx, dy -> channel()?.send(InputEvent.PointerMove(dx.roundToInt(), dy.roundToInt())) }
        strip.onTap = {
            channel()?.send(InputEvent.PointerButton(ProtocolConsts.MouseButton.LEFT, down = true))
            channel()?.send(InputEvent.PointerButton(ProtocolConsts.MouseButton.LEFT, down = false))
        }
        return strip
    }

    private fun buildKeyRow(ctx: Context, row: List<KeyDef>): View {
        val density = resources.displayMetrics.density
        val layout = LinearLayout(ctx).apply { orientation = LinearLayout.HORIZONTAL }
        row.forEach { key ->
            val button = Button(ctx).apply {
                text = key.label
                layoutParams = LinearLayout.LayoutParams(0, (52 * density).toInt(), 1f)
                    .apply { setMargins(2, 2, 2, 2) }
                setOnClickListener { onKeyPressed(key) }
            }
            if (key.isMod) modButtons[key.label] = button
            layout.addView(button)
        }
        return layout
    }

    // ---- 按键处理 ----

    private fun onKeyPressed(key: KeyDef) {
        val channel = channel() ?: return
        if (key.isMod) {
            when (key.label) {
                "⇧" -> shiftOn = !shiftOn
                "^" -> ctrlOn = !ctrlOn
                "⎇" -> altOn = !altOn
            }
            refreshMods()
            return
        }

        val mods = buildMods()
        if (mode == Mode.PASSTHROUGH) {
            // 直通：KEY down→up（HID Usage），修饰键随帧携带
            channel.send(InputEvent.KeyPress(key.hidUsage, down = true, mods = mods))
            channel.send(InputEvent.KeyPress(key.hidUsage, down = false, mods = mods))
        } else {
            // 直注：字符走 TEXT（Unicode 直注）；Enter/Backspace 走 KEY（PC 端行为键）
            when (key.kind) {
                KeyKind.CHAR -> channel.send(InputEvent.TextInput(if (shiftOn) key.upper else key.label))
                KeyKind.ENTER, KeyKind.BACKSPACE -> {
                    channel.send(InputEvent.KeyPress(key.hidUsage, down = true, mods = 0))
                    channel.send(InputEvent.KeyPress(key.hidUsage, down = false, mods = 0))
                }
            }
        }

        // Shift 单次生效后复位
        if (shiftOn) {
            shiftOn = false
            refreshMods()
        }
        refreshStatus()
    }

    private fun buildMods(): Int {
        var mods = 0
        if (shiftOn) mods = mods or ProtocolConsts.KeyMods.SHIFT
        if (ctrlOn) mods = mods or ProtocolConsts.KeyMods.CTRL
        if (altOn) mods = mods or ProtocolConsts.KeyMods.ALT
        return mods
    }

    private fun refreshMods() {
        modButtons.forEach { (symbol, button) ->
            val on = when (symbol) {
                "⇧" -> shiftOn
                "^" -> ctrlOn
                else -> altOn
            }
            button.alpha = if (on) 1f else 0.45f
        }
    }

    private fun refreshStatus() {
        val connected = KeyboardChannelHolder.channel != null
        val injectable = KeyboardChannelHolder.caps.textInject
        statusView.text = when {
            !connected -> "未连接（打开 SimpleTouch App 并连接 PC）"
            mode == Mode.PASSTHROUGH -> "直通：按键经 PC 输入法组词（中文需先在 PC 切好输入法）"
            injectable -> "直注：文字直接注入 PC（Unicode，无需 PC 切输入法）"
            else -> "直注（PC 不支持 TEXT_INJECT，按键可能无效果）"
        }
        modeButton.text = if (mode == Mode.PASSTHROUGH) "切到直注" else "切到直通"
    }

    private fun channel(): InputChannel? {
        val ch = KeyboardChannelHolder.channel
        if (ch == null) Toast.makeText(this, "SimpleTouch 未连接", Toast.LENGTH_SHORT).show()
        return ch
    }
}

/** 触摸条：滑动=指针移动（1.0×），快速轻点=左键单击。 */
private class TouchStripView(context: Context) : View(context) {

    var onMove: (Float, Float) -> Unit = { _, _ -> }
    var onTap: () -> Unit = {}

    private var lastX = 0f
    private var lastY = 0f
    private var startAt = 0L
    private var totalMove = 0f

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                lastX = event.x; lastY = event.y
                startAt = event.eventTime
                totalMove = 0f
            }
            MotionEvent.ACTION_MOVE -> {
                val dx = event.x - lastX
                val dy = event.y - lastY
                totalMove += abs(dx) + abs(dy)
                lastX = event.x; lastY = event.y
                if (dx != 0f || dy != 0f) onMove(dx * STRIP_SENSITIVITY, dy * STRIP_SENSITIVITY)
            }
            MotionEvent.ACTION_UP -> {
                val duration = event.eventTime - startAt
                if (totalMove < 24 * resources.displayMetrics.density && duration < 250) onTap()
            }
        }
        return true
    }

    private companion object {
        const val STRIP_SENSITIVITY = 1.0f
    }
}

private enum class KeyKind { CHAR, ENTER, BACKSPACE }

private data class KeyDef(
    val label: String,
    val hidUsage: Int,
    val kind: KeyKind = KeyKind.CHAR,
    val isMod: Boolean = false,
) {
    val upper: String = label.uppercase()
}
