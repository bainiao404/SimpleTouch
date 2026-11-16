# SimpleTouch M4 release 混淆规则（发布检查清单：关闭调试日志、keep 协议模块）

# ---- 协议模块整体 keep（执行文档 §7 DoD）----
# 纯 Kotlin 无反射，理论上 R8 可自行裁剪；按清单要求显式 keep，避免帧编解码字段名/结构被
# 优化改动引入隐性回归，协议正确性以测试向量为唯一权威
-keep class com.simpletouch.protocol.** { *; }

# ---- 日志收敛：release 移除 Verbose/Debug/Info 级别 ----
# SettingsStore 仅用 Log.w（保留 w/e 用于现场排障）
-assumenosideeffects class android.util.Log {
    public static int v(...);
    public static int d(...);
    public static int i(...);
}

# ---- Compose / kotlinx 由 AGP 与官方规则覆盖，无需额外配置 ----
