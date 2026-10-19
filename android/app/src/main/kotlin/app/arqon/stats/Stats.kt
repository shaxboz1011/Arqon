package app.arqon.stats

import android.content.Context
import android.os.Build
import app.arqon.AppSettings
import app.arqon.BuildConfig
import app.arqon.core.stats.StatsReport
import app.arqon.net.AndroidLinkManager
import app.arqon.net.LinkState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.time.LocalDate
import java.util.Locale
import java.util.concurrent.TimeUnit

/** Where features report that they were used: `Features.use("speed_test")`. Without a [Stats] behind it, it does nothing. */
object Features {
    @Volatile var sink: ((String) -> Unit)? = null

    fun use(name: String) {
        sink?.invoke(name)
    }
}

/**
 * Counts how often each feature is used, on the phone. The counts are only sent anywhere when the user agreed and this build has a
 * server address ([BuildConfig.STATS_URL], empty in the open-source build): one small report a day, built by [StatsReport]. Without
 * that agreement the counts never leave the phone and are only shown to the user in the settings.
 */
class Stats(
    context: Context,
    private val settings: AppSettings,
    private val links: AndroidLinkManager,
    private val scope: CoroutineScope,
) {
    private val prefs = context.getSharedPreferences("arqon-stats", Context.MODE_PRIVATE)
    private val http = OkHttpClient.Builder().callTimeout(15, TimeUnit.SECONDS).build()

    /** True when this build can send reports at all. */
    val canSend get() = BuildConfig.STATS_URL.isNotBlank()

    fun start() {
        Features.sink = ::count
        pruneBefore(LocalDate.now().minusDays(60).toString())
        if (!canSend) return
        scope.launch(Dispatchers.IO) {
            while (isActive) {
                if (settings.statsShare.value == true) sendIfDue()
                delay(30 * 60 * 1000L)
            }
        }
    }

    private fun key(day: String, feature: String) = "$day|$feature"

    fun count(feature: String) {
        val k = key(LocalDate.now().toString(), feature)
        prefs.edit().putInt(k, prefs.getInt(k, 0) + 1).apply()
    }

    /** The counts of one day, as the user can see them in the settings. */
    fun countsOf(day: String): Map<String, Int> =
        prefs.all.entries.filter { it.key.startsWith("$day|") && it.value is Int }.associate { it.key.substringAfter('|') to (it.value as Int) }

    /** All the counts so far, by feature (the settings list them). */
    fun totals(): Map<String, Int> {
        val sum = HashMap<String, Int>()
        for ((k, v) in prefs.all) if (v is Int && '|' in k) sum.merge(k.substringAfter('|'), v, Int::plus)
        return sum.toSortedMap()
    }

    /** What would be sent for [day]: shown in the settings, so the user can read exactly what leaves the phone. */
    fun preview(day: String = LocalDate.now().toString()): String = StatsReport.json(
        settings.installId(), BuildConfig.VERSION_NAME, "android", "${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})",
        "${Build.MANUFACTURER} ${Build.MODEL}", Locale.getDefault().language, day, countsOf(day), connectionSummary(),
    )

    private fun connectionSummary(): List<String> = links.all.value.map { info ->
        val kind = when {
            info.id == "wifi" -> "wifi"
            info.id == "ethernet" -> "ethernet"
            info.id == "mobile" -> "mobile"
            else -> "sim"
        }
        val state = when (info.state) { LinkState.ACTIVE -> "active"; LinkState.WAITING -> "waiting"; LinkState.UNAVAILABLE -> "refused" }
        "$kind:$state"
    }

    private fun sendIfDue() {
        // yesterday is complete; today is still counting
        val yesterday = LocalDate.now().minusDays(1).toString()
        if (prefs.getString("sentDay", null) == yesterday) return
        val counts = countsOf(yesterday)
        if (counts.isEmpty()) {
            prefs.edit().putString("sentDay", yesterday).apply()
            return
        }
        try {
            val body = preview(yesterday).toRequestBody("application/json".toMediaType())
            http.newCall(Request.Builder().url(BuildConfig.STATS_URL).post(body).build()).execute().use { response ->
                if (response.isSuccessful) {
                    prefs.edit().putString("sentDay", yesterday).apply()
                    pruneBefore(yesterday)
                }
            }
        } catch (_: Exception) {
            // no connection now: tried again later
        }
    }

    private fun pruneBefore(day: String) {
        val editor = prefs.edit()
        for (k in prefs.all.keys) if ('|' in k && k.substringBefore('|') < day) editor.remove(k)
        editor.apply()
    }
}
