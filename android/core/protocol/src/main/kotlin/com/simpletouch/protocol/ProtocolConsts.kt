package com.simpletouch.protocol

/** 协议常量，与 docs/protocol.md v1 对应。 */
object ProtocolConsts {
    const val PROTOCOL_VERSION = 1
    const val MAX_PAYLOAD = 4096
    const val FRAME_HEADER_SIZE = 4 // len:u16 + type:u8 + seq:u8

    const val RFCOMM_SERVICE_NAME = "SimpleTouch"
    const val RFCOMM_SERVICE_UUID = "8b6d7c1a-2f4e-4c9b-9a3d-5e1f0a2b3c4d"

    object MsgType {
        const val HELLO = 0x01
        const val READY = 0x02
        const val MOUSE_MOVE = 0x10
        const val MOUSE_BUTTON = 0x11
        const val MOUSE_SCROLL = 0x12
        const val KEY = 0x20
        const val TEXT = 0x21
        const val HOTKEY = 0x22
        const val CLIPBOARD_SET = 0x30
        const val PING = 0x40
        const val PONG = 0x41
        const val BYE = 0x50
    }

    object MouseButton {
        const val LEFT = 0
        const val RIGHT = 1
        const val MIDDLE = 2
    }

    object PressAction {
        const val DOWN = 0
        const val UP = 1
    }

    object KeyMods {
        const val CTRL = 0x01
        const val SHIFT = 0x02
        const val ALT = 0x04
        const val META = 0x08
    }

    object ByeReason {
        const val NORMAL = 0
        const val APP_CLOSING = 1
        const val SESSION_REPLACED = 2
        const val VERSION_MISMATCH = 3
        const val PROTOCOL_ERROR = 4
    }

    object CapsBits {
        const val TEXT_INJECT = 0x01
        const val CLIPBOARD = 0x02
        const val MACROS = 0x04
        const val CONSUMER = 0x08
    }

    /**
     * HOTKEY presetId 枚举表（docs/protocol.md §4 HOTKEY，M2 定稿）。
     * 0x0010–0x00FF 预留给 PC 映射表自定义；未收录值 PC 端忽略（兼容规则）。
     */
    object HotkeyPreset {
        const val VOLUME_UP = 0x0001
        const val VOLUME_DOWN = 0x0002
        const val VOLUME_MUTE = 0x0003
        const val PLAY_PAUSE = 0x0004
        const val MEDIA_PREV = 0x0005
        const val MEDIA_NEXT = 0x0006
        const val PAGE_UP = 0x0007
        const val PAGE_DOWN = 0x0008
        const val F5 = 0x0009
        const val ESC = 0x000A
        const val WIN_D = 0x000B
    }
}
