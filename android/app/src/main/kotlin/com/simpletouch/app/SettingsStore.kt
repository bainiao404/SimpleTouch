package com.simpletouch.app

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.simpletouch.feature.touchpad.TouchpadConfig
import org.json.JSONObject
import java.io.File
import java.util.concurrent.Executors

/**
 * M2 设置持久化（执行文档 §5：JSON 持久化，双端各自一份）。
 * 存 filesDir/settings.json；写盘走单线程执行器，主线程只改内存态。
 */
class SettingsStore(context: Context) {

    var config by mutableStateOf(TouchpadConfig())
        private set

    private val file = File(context.filesDir, "settings.json")
    private val executor = Executors.newSingleThreadExecutor { r -> Thread(r, "settings-writer") }

    init {
        runCatching {
            if (file.exists()) {
                val json = JSONObject(file.readText())
                config = TouchpadConfig(
                    sensitivity = json.optDouble(KEY_SENSITIVITY, TouchpadConfig().sensitivity.toDouble())
                        .toFloat().coerceIn(MIN_SENSITIVITY, MAX_SENSITIVITY),
                    naturalScroll = json.optBoolean(KEY_NATURAL_SCROLL, false),
                    scrollThreshold = json.optDouble(KEY_SCROLL_THRESHOLD, TouchpadConfig().scrollThreshold.toDouble())
                        .toFloat().coerceIn(MIN_THRESHOLD, MAX_THRESHOLD),
                )
            }
        }.onFailure {
            // 坏文件不阻塞启动，保留默认值；下次保存时覆盖
            android.util.Log.w("SettingsStore", "settings.json 读取失败，使用默认值", it)
        }
    }

    /** 更新设置：立即生效（Compose 状态），异步落盘。 */
    fun update(
        sensitivity: Float = config.sensitivity,
        naturalScroll: Boolean = config.naturalScroll,
        scrollThreshold: Float = config.scrollThreshold,
    ) {
        val next = TouchpadConfig(
            sensitivity = sensitivity.coerceIn(MIN_SENSITIVITY, MAX_SENSITIVITY),
            naturalScroll = naturalScroll,
            scrollThreshold = scrollThreshold.coerceIn(MIN_THRESHOLD, MAX_THRESHOLD),
        )
        config = next
        executor.execute {
            runCatching {
                file.writeText(
                    JSONObject().apply {
                        put(KEY_SENSITIVITY, next.sensitivity.toDouble())
                        put(KEY_NATURAL_SCROLL, next.naturalScroll)
                        put(KEY_SCROLL_THRESHOLD, next.scrollThreshold.toDouble())
                    }.toString()
                )
            }
        }
    }

    private companion object {
        const val KEY_SENSITIVITY = "sensitivity"
        const val KEY_NATURAL_SCROLL = "naturalScroll"
        const val KEY_SCROLL_THRESHOLD = "scrollThreshold"
        const val MIN_SENSITIVITY = 0.5f
        const val MAX_SENSITIVITY = 3f
        const val MIN_THRESHOLD = 8f
        const val MAX_THRESHOLD = 64f
    }
}
