package com.simpletouch.feature.touchpad

/**
 * 触摸板可调参数（M2：设置持久化，App 层由 SettingsStore 加载/保存 JSON）。
 */
data class TouchpadConfig(
    /** 指针移动放大系数（手机像素增量 × 此值；Windows 指针加速会再叠加，综合系数实测 ≈2.2×） */
    val sensitivity: Float = 1.5f,
    /** true=自然滚动（内容随手指示方向），false=传统滚轮 */
    val naturalScroll: Boolean = false,
    /** 双指滑动每格滚动的累计位移阈值（像素） */
    val scrollThreshold: Float = 24f,
)
