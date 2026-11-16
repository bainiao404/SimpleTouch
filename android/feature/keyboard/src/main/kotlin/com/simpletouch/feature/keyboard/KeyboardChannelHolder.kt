package com.simpletouch.feature.keyboard

import com.simpletouch.protocol.Capabilities
import com.simpletouch.protocol.InputChannel

/**
 * App 进程内共享的通道持有者：SessionController 在连接建立/断开时更新，
 * SimpleTouchIme（独立组件，拿不到 Activity 引用）从这里读取。
 */
object KeyboardChannelHolder {
    @Volatile
    var channel: InputChannel? = null

    @Volatile
    var caps: Capabilities = Capabilities(textInject = false, clipboard = false, macros = false, consumer = false)
}
