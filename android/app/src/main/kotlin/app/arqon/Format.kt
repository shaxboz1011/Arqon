package app.arqon

import java.util.Locale

/** Sizes and speeds for the screens. Speeds are shown in Mbit/s, the unit internet plans are sold in. */
object Format {
    fun size(bytes: Long): String {
        if (bytes < 1024) return "$bytes B"
        val units = arrayOf("KB", "MB", "GB", "TB")
        var value = bytes.toDouble()
        var unit = -1
        while (value >= 1024 && unit < units.lastIndex) { value /= 1024; unit++ }
        return String.format(Locale.US, if (value >= 100) "%.0f %s" else "%.1f %s", value, units[unit])
    }

    fun speed(bytesPerSecond: Long): String = String.format(Locale.US, "%.1f Mbit/s", bytesPerSecond * 8 / 1_000_000.0)

    fun percent(done: Long, total: Long): Int = if (total <= 0) 0 else ((done * 100) / total).toInt().coerceIn(0, 100)
}
