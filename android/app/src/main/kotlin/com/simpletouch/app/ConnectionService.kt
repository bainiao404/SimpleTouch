package com.simpletouch.app

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder

/**
 * M4 前台服务（connectedDevice 类型）：让 RFCOMM 监听在后台/息屏后持续存活（Android 11 API 30 适用）。
 * 会话状态由 Application 托管的 SessionController 持有，服务只做通知展示 + 进程保活：
 * - 状态经 SessionController.onStatusChanged 回调刷新通知文案
 * - 服务销毁时停止会话（系统回收兜底）；用户「停止监听」由 UI 调 SessionController.stop + stopService
 */
class ConnectionService : Service() {

    private val session get() = (application as SimpleTouchApp).session

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createChannel()
        session.onStatusChanged = { text ->
            notifyManager().notify(NOTIFICATION_ID, buildNotification(text))
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val notification = buildNotification(session.status.value)
        if (Build.VERSION.SDK_INT >= 29) {
            // connectedDevice 类型（API 29+ 起可用）；API 34+ 需 manifest 声明 FOREGROUND_SERVICE_CONNECTED_DEVICE 权限
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
        return START_STICKY
    }

    override fun onDestroy() {
        session.onStatusChanged = null
        // 系统回收兜底：前台服务被杀即停止监听，避免留下不可见的服务端
        session.stop()
        super.onDestroy()
    }

    private fun notifyManager() = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager

    private fun createChannel() {
        notifyManager().createNotificationChannel(
            NotificationChannel(CHANNEL_ID, "连接状态", NotificationManager.IMPORTANCE_LOW),
        )
    }

    private fun buildNotification(text: String): Notification =
        Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("SimpleTouch")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setOngoing(true)
            .build()

    companion object {
        private const val CHANNEL_ID = "connection"
        private const val NOTIFICATION_ID = 1

        /** startForegroundService：从 Activity 启动（前台交互期，不受后台启动限制）。 */
        fun start(context: Context) {
            context.startForegroundService(Intent(context, ConnectionService::class.java))
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, ConnectionService::class.java))
        }
    }
}
