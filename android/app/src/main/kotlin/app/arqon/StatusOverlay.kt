package app.arqon

import android.content.Context
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.drawable.GradientDrawable
import android.provider.Settings
import android.view.Gravity
import android.view.WindowManager
import android.widget.TextView

/**
 * A small label that floats over every other app, games included, saying which connections are combined and how fast data
 * flows. It never takes touches or focus, so it cannot get in the way of a game. It needs the "display over other apps"
 * permission, which the user gives in Android's settings.
 */
class StatusOverlay(private val context: Context) {
    private val windowManager = context.getSystemService(WindowManager::class.java)
    private var view: TextView? = null

    fun canShow(): Boolean = Settings.canDrawOverlays(context)

    fun update(text: String, combined: Boolean) {
        if (!canShow()) {
            hide()
            return
        }
        val label = view ?: create().also { view = it }
        label.text = "● $text"
        label.setTextColor(if (combined) Color.rgb(0x4A, 0xDE, 0x80) else Color.rgb(0xF5, 0x9E, 0x0B))
    }

    private fun create(): TextView {
        val label = TextView(context).apply {
            textSize = 12f
            setPadding(22, 10, 22, 10)
            background = GradientDrawable().apply { cornerRadius = 40f; setColor(Color.argb(190, 15, 18, 24)) }
        }
        val params = WindowManager.LayoutParams(
            WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
            PixelFormat.TRANSLUCENT,
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            x = 16
            y = 90
        }
        try {
            windowManager.addView(label, params)
        } catch (_: Exception) {
            // Android refused (permission taken back meanwhile); the next update tries again
        }
        return label
    }

    fun hide() {
        view?.let { try { windowManager.removeView(it) } catch (_: Exception) { } }
        view = null
    }
}
