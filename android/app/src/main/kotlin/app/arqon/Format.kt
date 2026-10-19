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

    /** A speed for the meter: the number and its unit apart (the notification icon shows them on two lines). */
    fun meter(bytesPerSecond: Long, inBytes: Boolean): Pair<String, String> {
        if (inBytes) {
            val b = bytesPerSecond.toDouble()
            return when {
                b >= 1024 * 1024 -> trim(b / (1024 * 1024)) to "MB/s"
                b >= 1024 -> trim(b / 1024) to "KB/s"
                else -> b.toLong().toString() to "B/s"
            }
        }
        val bits = bytesPerSecond * 8.0
        return when {
            bits >= 1_000_000 -> trim(bits / 1_000_000) to "Mbit/s"
            bits >= 1_000 -> trim(bits / 1_000) to "kbit/s"
            else -> bits.toLong().toString() to "bit/s"
        }
    }

    private fun trim(value: Double) = String.format(Locale.US, if (value >= 10) "%.0f" else "%.1f", value)

    fun percent(done: Long, total: Long): Int = if (total <= 0) 0 else ((done * 100) / total).toInt().coerceIn(0, 100)
}
