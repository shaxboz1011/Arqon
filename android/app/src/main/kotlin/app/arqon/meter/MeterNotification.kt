package app.arqon.meter

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.graphics.drawable.IconCompat
import app.arqon.Format
import app.arqon.MainActivity
import app.arqon.R

/**
 * The speed-meter notification: the speed as the status-bar icon itself, and in the shade the speed both ways, the network type and
 * signal, and what today's data use is. It is a second, quiet notification, so it can be hidden on its own.
 */
object MeterNotification {
    private const val CHANNEL = "meter"
    const val ID = 2

    fun build(context: Context, state: MeterState, inBytes: Boolean): Notification {
        val manager = context.getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            manager.createNotificationChannel(NotificationChannel(CHANNEL, context.getString(R.string.meter_channel), NotificationManager.IMPORTANCE_LOW).apply {
                setShowBadge(false)
            })
        }
        val (downValue, downUnit) = Format.meter(state.downBytesPerSecond, inBytes)
        val (upValue, upUnit) = Format.meter(state.upBytesPerSecond, inBytes)
        val head = StringBuilder("↓ $downValue $downUnit   ↑ $upValue $upUnit")
        val network = buildList {
            if (!state.signalIsWifi && state.generation.label != "?") add(state.generation.label)
            state.signalPercent?.let { add("$it%") }
        }
        if (network.isNotEmpty()) head.append("   ").append(network.joinToString(" · "))
        val body = context.getString(R.string.meter_today, Format.size(state.todayMobile.total), Format.size(state.todayWifi.total))
        val open = PendingIntent.getActivity(context, 1, Intent(context, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        return NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(speedIcon(downValue, downUnit))
            .setContentTitle(head.toString())
            .setContentText(body)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setShowWhen(false)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_STATUS)
            .setContentIntent(open)
            .build()
    }

    /** The status-bar icon: the download speed written on it, number on top and unit below, in white (the system tints it). */
    private fun speedIcon(value: String, unit: String): IconCompat {
        val size = 96
        val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.WHITE
            typeface = Typeface.DEFAULT_BOLD
            textAlign = Paint.Align.CENTER
        }
        paint.textSize = if (value.length >= 4) 50f else if (value.length == 3) 58f else 70f
        canvas.drawText(value, size / 2f, 56f, paint)
        paint.textSize = 32f
        canvas.drawText(unit.removeSuffix("/s"), size / 2f, 92f, paint)
        return IconCompat.createWithBitmap(bitmap)
    }
}
