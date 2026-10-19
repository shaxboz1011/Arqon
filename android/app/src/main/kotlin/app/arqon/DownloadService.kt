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
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import app.arqon.net.LinkState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * The foreground service. Its notification says which connections are combined right now (so it is visible in the notification
 * shade and the lock screen, also while a game is on) and keeps Android from stopping Arqon and its networks in the background.
 * While downloads run it also holds the CPU awake. The same text can float over other apps as a small label.
 */
class DownloadService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var loop: Job? = null
    private var overlay: StatusOverlay? = null
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val app = application as ArqonApp
        val first = notification(this, app.statusText(), app.statusDetail())
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, first, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        } else {
            startForeground(NOTIFICATION_ID, first)
        }
        if (loop == null) {
            overlay = StatusOverlay(this)
            loop = scope.launch {
                val manager = getSystemService(NotificationManager::class.java)
                while (isActive) {
                    val running = app.downloads.summary.value.running
                    if (!app.settings.keepStatus.value && running == 0 && !app.gameMode.state.value.running) {
                        stopSelf()
                        break
                    }
                    manager.notify(NOTIFICATION_ID, notification(this@DownloadService, app.statusText(), app.statusDetail()))
                    if (app.settings.overlay.value) overlay?.update(app.statusShort(), app.activeLinkCount() >= 2) else overlay?.hide()
                    holdAwake(running > 0)
                    delay(1000)
                }
            }
        }
        return START_NOT_STICKY
    }

    private fun holdAwake(needed: Boolean) {
        if (needed && wakeLock == null) {
            val power = getSystemService(PowerManager::class.java)
            wakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "arqon:downloads").also { it.acquire(6 * 60 * 60 * 1000L) }
        } else if (!needed) {
            wakeLock?.let { if (it.isHeld) it.release() }
            wakeLock = null
        }
    }

    override fun onDestroy() {
        loop?.cancel()
        scope.cancel()
        overlay?.hide()
        wakeLock?.let { if (it.isHeld) it.release() }
        super.onDestroy()
    }

    companion object {
        private const val CHANNEL = "status"
        private const val NOTIFICATION_ID = 1

        /** Starts the service (from a visible app, or when a download starts); Android may refuse from the background. */
        fun ensureRunning(context: Context) {
            try {
                ContextCompat.startForegroundService(context, Intent(context, DownloadService::class.java))
            } catch (_: Exception) {
                // downloads still run while the app is open
            }
        }

        private fun notification(context: Context, title: String, detail: String): Notification {
            val manager = context.getSystemService(NotificationManager::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                manager.createNotificationChannel(NotificationChannel(CHANNEL, context.getString(R.string.notification_channel), NotificationManager.IMPORTANCE_LOW))
            }
            val open = PendingIntent.getActivity(context, 0, Intent(context, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
            return NotificationCompat.Builder(context, CHANNEL)
                .setSmallIcon(android.R.drawable.stat_sys_download)
                .setContentTitle(title)
                .setContentText(detail)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setContentIntent(open)
                .build()
        }
    }
}

/** Link names that carry data now (on, and answered by Android). */
internal fun ArqonApp.activeNames(): List<String> =
    links.all.value.filter { it.state == LinkState.ACTIVE && it.enabled }.map { it.name.substringBefore(" ·") }

internal fun ArqonApp.activeLinkCount(): Int = activeNames().size

/** The notification title: which connections are combined. */
internal fun ArqonApp.statusText(): String {
    val names = activeNames()
    return when (names.size) {
        0 -> getString(R.string.status_none)
        1 -> getString(R.string.status_single, names[0])
        else -> getString(R.string.status_combined, names.joinToString(" + "))
    }
}

/** The notification text: speed and downloads. */
internal fun ArqonApp.statusDetail(): String {
    val speed = downloads.linkSpeeds.value.values.sum()
    val running = downloads.summary.value.running
    val parts = ArrayList<String>()
    parts += if (speed > 0) "↓ " + Format.speed(speed) else getString(R.string.status_idle)
    if (running > 0) parts += getString(R.string.status_downloads, running)
    val game = gameMode.state.value
    if (game.running) parts += getString(R.string.status_game, (game.linkName ?: "–") + (game.pingMillis?.let { " $it ms" } ?: ""))
    return parts.joinToString(" · ")
}

/** The label over other apps: short enough for a corner of a game's screen. */
internal fun ArqonApp.statusShort(): String {
    val names = activeNames()
    val speed = downloads.linkSpeeds.value.values.sum()
    val head = if (names.size >= 2) names.joinToString("+") else names.firstOrNull() ?: "–"
    return if (speed > 0) head + " ↓" + String.format(java.util.Locale.US, "%.1f", speed * 8 / 1_000_000.0) else head
}
