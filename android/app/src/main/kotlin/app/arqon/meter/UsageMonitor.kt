package app.arqon.meter

import android.app.AppOpsManager
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.usage.NetworkStats
import android.app.usage.NetworkStatsManager
import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.TrafficStats
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Process
import android.telephony.TelephonyManager
import androidx.core.app.NotificationCompat
import app.arqon.AppSettings
import app.arqon.Format
import app.arqon.R
import app.arqon.core.usage.DayUsage
import app.arqon.core.usage.Generation
import app.arqon.core.usage.LimitState
import app.arqon.core.usage.Limits
import app.arqon.core.usage.Medium
import app.arqon.core.usage.NetworkTypes
import app.arqon.core.usage.Signal
import app.arqon.core.usage.Traffic
import app.arqon.core.usage.TrafficSampler
import app.arqon.core.usage.UsageLedger
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.time.LocalDate
import java.time.ZoneId

/** What the usage screen and the meter notification show. */
data class MeterState(
    val downBytesPerSecond: Long = 0,
    val upBytesPerSecond: Long = 0,
    /** 0-100, of Wi-Fi when it is connected, otherwise of the mobile network; null when the phone does not tell. */
    val signalPercent: Int? = null,
    val signalIsWifi: Boolean = false,
    val generation: Generation = Generation.UNKNOWN,
    val todayWifi: Traffic = Traffic(),
    val todayMobile: Traffic = Traffic(),
    val monthWifi: Traffic = Traffic(),
    val monthMobile: Traffic = Traffic(),
    val week: List<DayUsage> = emptyList(),
    /** True when the numbers come from Android's own counters (usage access allowed), false when Arqon counted them itself. */
    val exact: Boolean = false,
)

class AppUsage(val label: String, val bytes: Long)

/**
 * Watches the phone's traffic counters: speed now, how much went over Wi-Fi and over mobile data today and this month, the signal and
 * the type of the mobile network. Everything stays on the phone. With "usage access" Android's own counters are used (exact, and
 * complete even while Arqon was closed); without it Arqon adds up what it saw itself.
 */
class UsageMonitor(
    private val context: Context,
    private val settings: AppSettings,
    private val scope: CoroutineScope,
) {
    private val file = File(context.filesDir, "usage.csv")
    private val ledger = try { UsageLedger.parse(file.takeIf { it.exists() }?.readText().orEmpty()) } catch (_: Exception) { UsageLedger() }
    private val sampler = TrafficSampler()
    private val prefs = settings.prefsRaw()
    private val cm = context.getSystemService(ConnectivityManager::class.java)
    private val _state = MutableStateFlow(MeterState())
    val state: StateFlow<MeterState> = _state

    @Volatile private var started = false

    fun start() {
        if (started) return
        started = true
        restoreCounters()
        scope.launch(Dispatchers.Default) {
            var tick = 0
            var signal: Triple<Int?, Boolean, Generation> = Triple(null, false, Generation.UNKNOWN)
            var exact: Exact? = null
            while (isActive) {
                val now = System.currentTimeMillis()
                val sample = sampler.sample(
                    now, TrafficStats.getTotalRxBytes(), TrafficStats.getTotalTxBytes(),
                    TrafficStats.getMobileRxBytes(), TrafficStats.getMobileTxBytes(),
                )
                val today = LocalDate.now()
                val key = today.toString()
                ledger.add(key, Medium.WIFI, sample.wifi.rx, sample.wifi.tx)
                ledger.add(key, Medium.MOBILE, sample.mobile.rx, sample.mobile.tx)

                if (tick % 5 == 0) signal = readSignal()
                if (tick % 15 == 0) {
                    exact = readExact(today)
                    saveLedger(today)
                    checkLimit(today, exact)
                }
                tick++

                val ownToday = { m: Medium -> ledger.day(key, m) }
                val ownMonth = { m: Medium -> ledger.month(key.substring(0, 7), m) }
                _state.value = MeterState(
                    downBytesPerSecond = sample.downBytesPerSecond, upBytesPerSecond = sample.upBytesPerSecond,
                    signalPercent = signal.first, signalIsWifi = signal.second, generation = signal.third,
                    todayWifi = exact?.todayWifi ?: ownToday(Medium.WIFI), todayMobile = exact?.todayMobile ?: ownToday(Medium.MOBILE),
                    monthWifi = exact?.monthWifi ?: ownMonth(Medium.WIFI), monthMobile = exact?.monthMobile ?: ownMonth(Medium.MOBILE),
                    week = exact?.week ?: ledger.lastDays(today, 7),
                    exact = exact != null,
                )
                delay(1000)
            }
        }
    }

    private fun restoreCounters() {
        if (!prefs.contains("meterAt")) return
        sampler.restore(
            prefs.getLong("meterAt", 0), prefs.getLong("meterTrx", 0), prefs.getLong("meterTtx", 0),
            prefs.getLong("meterMrx", 0), prefs.getLong("meterMtx", 0),
        )
    }

    private fun saveLedger(today: LocalDate) {
        try {
            ledger.prune(today)
            file.writeText(ledger.serialize())
            prefs.edit()
                .putLong("meterAt", System.currentTimeMillis())
                .putLong("meterTrx", TrafficStats.getTotalRxBytes()).putLong("meterTtx", TrafficStats.getTotalTxBytes())
                .putLong("meterMrx", TrafficStats.getMobileRxBytes()).putLong("meterMtx", TrafficStats.getMobileTxBytes())
                .apply()
        } catch (_: Exception) {
            // the numbers are kept in memory and written again next time
        }
    }

    // ---- signal and network type ---------------------------------------------------------------------------------------

    @Suppress("DEPRECATION")
    private fun readSignal(): Triple<Int?, Boolean, Generation> {
        var percent: Int? = null
        var wifi = false
        try {
            val onWifi = cm.allNetworks.any { cm.getNetworkCapabilities(it)?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) == true }
            if (onWifi) {
                val manager = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
                val rssi = manager.connectionInfo?.rssi
                if (rssi != null && rssi > -127) {
                    percent = Signal.percentFromRssi(rssi)
                    wifi = true
                }
            }
        } catch (_: Exception) {
        }
        var generation = Generation.UNKNOWN
        try {
            val telephony = context.getSystemService(TelephonyManager::class.java)
            generation = NetworkTypes.generation(telephony.dataNetworkType)
            if (percent == null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                telephony.signalStrength?.let { percent = Signal.percentFromLevel(it.level) }
            }
        } catch (_: SecurityException) {
            // no phone permission: the network type stays unknown
        } catch (_: Exception) {
        }
        return Triple(percent, wifi, generation)
    }

    // ---- Android's own counters ----------------------------------------------------------------------------------------

    private class Exact(
        val todayWifi: Traffic, val todayMobile: Traffic, val monthWifi: Traffic, val monthMobile: Traffic, val week: List<DayUsage>,
    )

    fun hasUsageAccess(): Boolean = try {
        val ops = context.getSystemService(AppOpsManager::class.java)
        val mode = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            ops.unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), context.packageName)
        } else {
            @Suppress("DEPRECATION") ops.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), context.packageName)
        }
        mode == AppOpsManager.MODE_ALLOWED
    } catch (_: Exception) {
        false
    }

    private fun startOf(date: LocalDate) = date.atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()

    private fun device(type: Int, from: Long, to: Long): Traffic? = try {
        val manager = context.getSystemService(NetworkStatsManager::class.java)
        val bucket = manager.querySummaryForDevice(type, null, from, to)
        Traffic(bucket.rxBytes, bucket.txBytes)
    } catch (_: Exception) {
        null
    }

    /** Mobile counters can be asked without the subscriber id only from Android 10; before that Arqon's own numbers are used. */
    private fun readExact(today: LocalDate): Exact? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q || !hasUsageAccess()) return null
        val end = System.currentTimeMillis()
        fun both(from: Long, to: Long): Pair<Traffic, Traffic>? {
            val wifi = device(ConnectivityManager.TYPE_WIFI, from, to) ?: return null
            val mobile = device(ConnectivityManager.TYPE_MOBILE, from, to) ?: return null
            return wifi to mobile
        }
        val day = both(startOf(today), end) ?: return null
        val month = both(startOf(today.withDayOfMonth(1)), end) ?: return null
        val week = (6 downTo 0).map { back ->
            val date = today.minusDays(back.toLong())
            val pair = both(startOf(date), if (back == 0) end else startOf(date.plusDays(1))) ?: (Traffic() to Traffic())
            DayUsage(date, pair.first, pair.second)
        }
        return Exact(day.first, day.second, month.first, month.second, week)
    }

    /** The apps that used the most data today (wifi and mobile together). Needs usage access; returns nothing without it. */
    suspend fun topAppsToday(limit: Int = 8): List<AppUsage> = withContext(Dispatchers.IO) {
        if (!hasUsageAccess()) return@withContext emptyList()
        val manager = context.getSystemService(NetworkStatsManager::class.java)
        val from = startOf(LocalDate.now())
        val to = System.currentTimeMillis()
        val perUid = HashMap<Int, Long>()
        for (type in listOf(ConnectivityManager.TYPE_WIFI, ConnectivityManager.TYPE_MOBILE)) {
            try {
                val stats = manager.querySummary(type, null, from, to)
                val bucket = NetworkStats.Bucket()
                while (stats.hasNextBucket()) {
                    stats.getNextBucket(bucket)
                    perUid[bucket.uid] = (perUid[bucket.uid] ?: 0) + bucket.rxBytes + bucket.txBytes
                }
                stats.close()
            } catch (_: Exception) {
            }
        }
        val packages = context.packageManager
        perUid.entries.filter { it.value > 0 }.sortedByDescending { it.value }.take(limit).map { (uid, bytes) ->
            val label = when (uid) {
                1000 -> context.getString(R.string.usage_android_system)
                NetworkStats.Bucket.UID_REMOVED -> context.getString(R.string.usage_removed_apps)
                NetworkStats.Bucket.UID_TETHERING -> context.getString(R.string.usage_hotspot)
                else -> packages.getPackagesForUid(uid)?.firstOrNull()?.let { name ->
                    try { packages.getApplicationLabel(packages.getApplicationInfo(name, 0)).toString() } catch (_: Exception) { name }
                } ?: "uid $uid"
            }
            AppUsage(label, bytes)
        }
    }

    // ---- the daily limit -----------------------------------------------------------------------------------------------

    private fun checkLimit(today: LocalDate, exact: Exact?) {
        val limit = settings.dailyMobileLimitMb.value * 1024 * 1024
        val used = (exact?.todayMobile ?: ledger.day(today.toString(), Medium.MOBILE)).total
        val level = Limits.state(used, limit)
        if (level != LimitState.NEAR && level != LimitState.REACHED) return
        val marker = "$today:$level"
        if (prefs.getString("limitNotified", null) == marker) return
        prefs.edit().putString("limitNotified", marker).apply()
        try {
            val manager = context.getSystemService(NotificationManager::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                manager.createNotificationChannel(NotificationChannel(ALERT_CHANNEL, context.getString(R.string.limit_channel), NotificationManager.IMPORTANCE_DEFAULT))
            }
            val title = context.getString(if (level == LimitState.REACHED) R.string.limit_reached else R.string.limit_near)
            manager.notify(
                ALERT_ID,
                NotificationCompat.Builder(context, ALERT_CHANNEL)
                    .setSmallIcon(android.R.drawable.stat_sys_warning)
                    .setContentTitle(title)
                    .setContentText(context.getString(R.string.limit_body, Format.size(used), Format.size(limit)))
                    .setAutoCancel(true)
                    .build(),
            )
        } catch (_: SecurityException) {
            // notifications are not allowed
        }
    }

    private companion object {
        const val ALERT_CHANNEL = "alerts"
        const val ALERT_ID = 3
    }
}
