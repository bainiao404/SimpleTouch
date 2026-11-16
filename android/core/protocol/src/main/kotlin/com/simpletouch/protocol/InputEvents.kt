package com.simpletouch.protocol

/**
 * 语义输入事件（与传输方式无关）。
 * 特征层（触摸板/IME/快捷面板）只产出这些事件，永不直接依赖 core-bluetooth/core-protocol 传输细节
 * （方案文档 §4 耦合控制）。
 */
sealed class InputEvent {
    data class PointerMove(val dx: Int, val dy: Int) : InputEvent()
    data class PointerButton(val button: Int, val down: Boolean) : InputEvent()
    data class PointerScroll(val dx: Int, val dy: Int) : InputEvent()
    data class KeyPress(val hidUsage: Int, val down: Boolean, val mods: Int) : InputEvent()

    /** 媒体/consumer 动作经 HOTKEY 通道：代理模式由 PC 端映射表转为 VK_MEDIA_*，HID 模式走 Consumer Page。 */
    data class ConsumerAction(val presetId: Int) : InputEvent()
    data class TextInput(val text: String) : InputEvent()

    /** 剪贴板同步（手机→PC 方向；PC→手机由会话层接收，不走输入事件）。 */
    data class ClipboardSet(val text: String) : InputEvent()
    data class HotkeyTrigger(val presetId: Int) : InputEvent()
}

/** 能力位集合（HELLO.caps 的结构化表达），UI 按能力渲染禁用/隐藏，避免散落 if(mode==…) 分支。 */
data class Capabilities(
    val textInject: Boolean,
    val clipboard: Boolean,
    val macros: Boolean,
    val consumer: Boolean,
) {
    fun toMask(): Int {
        var mask = 0
        if (textInject) mask = mask or ProtocolConsts.CapsBits.TEXT_INJECT
        if (clipboard) mask = mask or ProtocolConsts.CapsBits.CLIPBOARD
        if (macros) mask = mask or ProtocolConsts.CapsBits.MACROS
        if (consumer) mask = mask or ProtocolConsts.CapsBits.CONSUMER
        return mask
    }

    companion object {
        fun fromMask(mask: Int) = Capabilities(
            textInject = mask and ProtocolConsts.CapsBits.TEXT_INJECT != 0,
            clipboard = mask and ProtocolConsts.CapsBits.CLIPBOARD != 0,
            macros = mask and ProtocolConsts.CapsBits.MACROS != 0,
            consumer = mask and ProtocolConsts.CapsBits.CONSUMER != 0,
        )
    }
}

/**
 * 特征层唯一依赖的发送通道抽象；RfcommChannel 与 HidChannel 是同一接口的两个实现。
 * 实现方负责把事件编码为消息（TEXT 按 UTF-8 码点边界分帧）。
 * send 为非阻塞调用（内部排队/合并），可安全在手势回调（受限挂起作用域）与主线程使用。
 */
interface InputChannel {
    val capabilities: Capabilities
    fun send(event: InputEvent)
}
