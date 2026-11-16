package com.simpletouch.app

import android.app.Application

/**
 * 进程级托管：MainActivity 与 SimpleTouchIme（IME 服务）同进程，共用一个 SessionController。
 */
class SimpleTouchApp : Application() {

    lateinit var session: SessionController
        private set
    lateinit var settingsStore: SettingsStore
        private set

    override fun onCreate() {
        super.onCreate()
        session = SessionController(this)
        settingsStore = SettingsStore(this)
    }
}
