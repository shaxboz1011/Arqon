package app.arqon.download

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import app.arqon.MainActivity
import app.arqon.R

/** "Download finished": one notification per finished download, so a big file does not have to be watched. */
object DoneNotification {
    private const val CHANNEL = "done"

    fun show(context: Context, name: String) {
        try {
            val manager = context.getSystemService(NotificationManager::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                manager.createNotificationChannel(NotificationChannel(CHANNEL, context.getString(R.string.done_channel), NotificationManager.IMPORTANCE_DEFAULT))
            }
            val open = PendingIntent.getActivity(context, 2, Intent(context, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
            manager.notify(
                name.hashCode() or 0x1000,
                NotificationCompat.Builder(context, CHANNEL)
                    .setSmallIcon(android.R.drawable.stat_sys_download_done)
                    .setContentTitle(context.getString(R.string.done_title))
                    .setContentText(name)
                    .setAutoCancel(true)
                    .setContentIntent(open)
                    .build(),
            )
        } catch (_: SecurityException) {
            // notifications are not allowed: the list in the app still shows it
        }
    }
}
