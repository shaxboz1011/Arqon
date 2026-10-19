package app.arqon

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import app.arqon.download.Summary

/**
 * A foreground service with a notification, so that Android keeps downloading (and keeps the SIM and Wi-Fi networks up) while
 * the app is in the background. It starts when a download starts and stops by itself when nothing is running.
 */
class DownloadService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val running = intent?.getIntExtra(EXTRA_RUNNING, 0) ?: 0
        val speed = intent?.getLongExtra(EXTRA_SPEED, 0) ?: 0
        if (running == 0) {
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }
        val notification = notification(this, running, speed)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
        return START_NOT_STICKY
    }

    companion object {
        private const val CHANNEL = "downloads"
        private const val NOTIFICATION_ID = 1
        private const val EXTRA_RUNNING = "running"
        private const val EXTRA_SPEED = "speed"

        fun update(context: Context, summary: Summary) {
            val intent = Intent(context, DownloadService::class.java)
                .putExtra(EXTRA_RUNNING, summary.running).putExtra(EXTRA_SPEED, summary.speed)
            try {
                if (summary.running > 0) ContextCompat.startForegroundService(context, intent) else context.startService(intent)
            } catch (_: Exception) {
                // Android may refuse to start a foreground service from the background; downloads still run while the app is open
            }
        }

        private fun notification(context: Context, running: Int, speed: Long): Notification {
            val manager = context.getSystemService(NotificationManager::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                manager.createNotificationChannel(NotificationChannel(CHANNEL, context.getString(R.string.notification_channel), NotificationManager.IMPORTANCE_LOW))
            }
            val open = PendingIntent.getActivity(context, 0, Intent(context, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
            return NotificationCompat.Builder(context, CHANNEL)
                .setSmallIcon(android.R.drawable.stat_sys_download)
                .setContentTitle(context.getString(R.string.notification_title, running))
                .setContentText(if (speed > 0) context.getString(R.string.notification_speed, Format.speed(speed)) else context.getString(R.string.notification_waiting))
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setContentIntent(open)
                .build()
        }
    }
}
