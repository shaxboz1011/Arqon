package app.arqon.core.usage

import java.time.LocalDate
import java.util.TreeMap

enum class Medium { WIFI, MOBILE }

/** Bytes received and sent. */
data class Traffic(val rx: Long = 0, val tx: Long = 0) {
    val total get() = rx + tx
    operator fun plus(other: Traffic) = Traffic(rx + other.rx, tx + other.tx)
}

/**
 * How much data went over Wi-Fi and over mobile data on each day: what a data-usage screen shows. Days are "yyyy-MM-dd" text so the
 * ledger does not depend on the clock or the time zone; the caller names the day. It is kept as plain text that survives a restart.
 */
class UsageLedger {
    private val days = TreeMap<String, MutableMap<Medium, Traffic>>()

    @Synchronized
    fun add(day: String, medium: Medium, rx: Long, tx: Long) {
        if (rx <= 0 && tx <= 0) return
        val row = days.getOrPut(day) { HashMap() }
        row[medium] = (row[medium] ?: Traffic()) + Traffic(maxOf(rx, 0), maxOf(tx, 0))
    }

    @Synchronized
    fun day(day: String, medium: Medium): Traffic = days[day]?.get(medium) ?: Traffic()

    /** Everything in one month, "yyyy-MM". */
    @Synchronized
    fun month(yearMonth: String, medium: Medium): Traffic {
        var sum = Traffic()
        for (row in days.subMap(yearMonth, yearMonth + Char.MAX_VALUE.toString()).values) sum += row[medium] ?: Traffic()
        return sum
    }

    /** The last [count] days up to and including [last], oldest first; days with no data are there with zeros. */
    @Synchronized
    fun lastDays(last: LocalDate, count: Int): List<DayUsage> =
        (count - 1 downTo 0).map { back ->
            val date = last.minusDays(back.toLong())
            val key = date.toString()
            DayUsage(date, day(key, Medium.WIFI), day(key, Medium.MOBILE))
        }

    /** Forgets days older than [keep] days before [today]. */
    @Synchronized
    fun prune(today: LocalDate, keep: Int = 400) {
        val limit = today.minusDays(keep.toLong()).toString()
        days.headMap(limit).clear()
    }

    @Synchronized
    fun isEmpty() = days.isEmpty()

    @Synchronized
    fun serialize(): String = buildString {
        for ((day, row) in days) for ((medium, t) in row) append(day).append(',').append(medium.name).append(',').append(t.rx).append(',').append(t.tx).append('\n')
    }

    companion object {
        fun parse(text: String): UsageLedger {
            val ledger = UsageLedger()
            for (line in text.lineSequence()) {
                val parts = line.split(',')
                if (parts.size != 4) continue
                val medium = Medium.entries.firstOrNull { it.name == parts[1] } ?: continue
                val rx = parts[2].toLongOrNull() ?: continue
                val tx = parts[3].toLongOrNull() ?: continue
                ledger.add(parts[0], medium, rx, tx)
            }
            return ledger
        }
    }
}

data class DayUsage(val date: LocalDate, val wifi: Traffic, val mobile: Traffic) {
    val total get() = wifi.total + mobile.total
}

/** What one look at the phone's traffic counters tells: speed now, and how much each kind of network carried since the last look. */
data class TrafficSample(
    val downBytesPerSecond: Long,
    val upBytesPerSecond: Long,
    val wifi: Traffic,
    val mobile: Traffic,
)

/**
 * Turns the phone's running totals (total bytes and mobile bytes since boot) into speeds and into amounts per kind of network.
 * Wi-Fi is "everything that was not mobile". A counter that went down (the phone restarted, or the counters were reset) starts over
 * instead of producing a huge or negative number, and a counter the phone does not have (reported as -1) counts as zero.
 */
class TrafficSampler {
    private var lastAt = 0L
    private var lastTotalRx = 0L
    private var lastTotalTx = 0L
    private var lastMobileRx = 0L
    private var lastMobileTx = 0L
    private var started = false
    private var quietFirst = false

    /** Continues from counters saved earlier (before Arqon was closed): what passed meanwhile is counted, but gives no speed. */
    @Synchronized
    fun restore(atMillis: Long, totalRx: Long, totalTx: Long, mobileRx: Long, mobileTx: Long) {
        remember(atMillis, totalRx.coerceAtLeast(0), totalTx.coerceAtLeast(0), mobileRx.coerceAtLeast(0), mobileTx.coerceAtLeast(0))
        started = true
        quietFirst = true
    }

    @Synchronized
    fun sample(nowMillis: Long, totalRx: Long, totalTx: Long, mobileRx: Long, mobileTx: Long): TrafficSample {
        val trx = totalRx.coerceAtLeast(0)
        val ttx = totalTx.coerceAtLeast(0)
        val mrx = mobileRx.coerceAtLeast(0)
        val mtx = mobileTx.coerceAtLeast(0)
        if (!started) {
            started = true
            remember(nowMillis, trx, ttx, mrx, mtx)
            return TrafficSample(0, 0, Traffic(), Traffic())
        }
        val dRx = step(trx, lastTotalRx)
        val dTx = step(ttx, lastTotalTx)
        val mRx = minOf(step(mrx, lastMobileRx), dRx)
        val mTx = minOf(step(mtx, lastMobileTx), dTx)
        val seconds = (nowMillis - lastAt) / 1000.0
        remember(nowMillis, trx, ttx, mrx, mtx)
        val quiet = quietFirst
        quietFirst = false
        val down = if (seconds > 0 && !quiet) (dRx / seconds).toLong() else 0
        val up = if (seconds > 0 && !quiet) (dTx / seconds).toLong() else 0
        return TrafficSample(down, up, Traffic(dRx - mRx, dTx - mTx), Traffic(mRx, mTx))
    }

    private fun step(now: Long, before: Long) = if (now >= before) now - before else now   // a counter that restarted counts from zero

    private fun remember(at: Long, trx: Long, ttx: Long, mrx: Long, mtx: Long) {
        lastAt = at; lastTotalRx = trx; lastTotalTx = ttx; lastMobileRx = mrx; lastMobileTx = mtx
    }
}

enum class Generation(val label: String) { G5("5G"), G4("4G"), G3("3G"), G2("2G"), UNKNOWN("?") }

/** Android's mobile network type codes (`TelephonyManager.NETWORK_TYPE_*`) as the generation people know. */
object NetworkTypes {
    fun generation(type: Int): Generation = when (type) {
        20 -> Generation.G5                                   // NR
        13, 19 -> Generation.G4                               // LTE, LTE carrier aggregation
        3, 5, 6, 8, 9, 10, 12, 14, 15, 17 -> Generation.G3    // UMTS, EV-DO, HSPA family, eHRPD, TD-SCDMA
        1, 2, 4, 7, 11, 16 -> Generation.G2                   // GPRS, EDGE, CDMA, 1xRTT, iDEN, GSM
        else -> Generation.UNKNOWN
    }
}

/** Signal strength as a percentage. */
object Signal {
    /** Wi-Fi RSSI in dBm: -50 or better is full, -100 or worse is nothing. */
    fun percentFromRssi(rssi: Int): Int = ((rssi + 100) * 2).coerceIn(0, 100)

    /** Android's "level" for mobile signals, 0 (none) to 4 (great). */
    fun percentFromLevel(level: Int): Int = (level.coerceIn(0, 4) * 25)
}

/** Daily data limits: how far use has come towards the limit. */
object Limits {
    /** 0 means no limit; otherwise how far [used] is towards [limit]: below 80%, 80% and more, or reached. */
    fun state(used: Long, limit: Long): LimitState = when {
        limit <= 0 -> LimitState.NONE
        used >= limit -> LimitState.REACHED
        used * 100 >= limit * 80 -> LimitState.NEAR
        else -> LimitState.OK
    }
}

enum class LimitState { NONE, OK, NEAR, REACHED }
