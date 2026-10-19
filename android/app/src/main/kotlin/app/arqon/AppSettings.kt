package app.arqon

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/** The user's choices, kept in SharedPreferences and readable as flows so the screens update at once. */
class AppSettings(context: Context) {
    private val prefs = context.getSharedPreferences("arqon", Context.MODE_PRIVATE)

    private val _desktopMode = MutableStateFlow(prefs.getBoolean("desktopMode", true))
    val desktopMode: StateFlow<Boolean> = _desktopMode

    private val _seed = MutableStateFlow(prefs.getBoolean("seed", false))
    val seedAfterComplete: StateFlow<Boolean> = _seed

    private val _keepStatus = MutableStateFlow(prefs.getBoolean("keepStatus", true))

    /** Keep the notification (and Arqon running in the background) that says which connections are combined. */
    val keepStatus: StateFlow<Boolean> = _keepStatus

    private val _overlay = MutableStateFlow(prefs.getBoolean("overlay", false))

    /** Show a small label over other apps, games included. */
    val overlay: StateFlow<Boolean> = _overlay

    private val _maxConcurrent = MutableStateFlow(prefs.getInt("maxConcurrent", 3))
    val maxConcurrent: StateFlow<Int> = _maxConcurrent

    private val _disabledLinks = MutableStateFlow(prefs.getStringSet("disabledLinks", emptySet()).orEmpty().toSet())

    /** Ids of the links the user switched off (for example mobile data of a SIM with a small plan). */
    val disabledLinks: StateFlow<Set<String>> = _disabledLinks

    private val _downloadDir = MutableStateFlow(prefs.getString("downloadDir", null))
    val downloadDir: StateFlow<String?> = _downloadDir

    private val _cellularOnlyWhenWifiIsAbsent = MutableStateFlow(prefs.getBoolean("cellularOnlyWithoutWifi", false))

    /** Save mobile data: use the SIMs only while no Wi-Fi is available. */
    val cellularOnlyWithoutWifi: StateFlow<Boolean> = _cellularOnlyWhenWifiIsAbsent

    private val _gameMode = MutableStateFlow(prefs.getBoolean("gameMode", false))

    /** The user wants the game mode on: it is started again whenever Arqon opens. */
    val gameMode: StateFlow<Boolean> = _gameMode

    private val _gamePackages = MutableStateFlow(prefs.getStringSet("gamePackages", emptySet()).orEmpty().toSet())

    /** Package names of the apps whose traffic goes through the game mode. */
    val gamePackages: StateFlow<Set<String>> = _gamePackages

    fun setGameMode(value: Boolean) { prefs.edit().putBoolean("gameMode", value).apply(); _gameMode.value = value }

    fun setGamePackage(name: String, selected: Boolean) {
        val next = if (selected) _gamePackages.value + name else _gamePackages.value - name
        prefs.edit().putStringSet("gamePackages", next).apply()
        _gamePackages.value = next
    }

    private val _meterNotification = MutableStateFlow(prefs.getBoolean("meterNotification", true))

    /** A second notification with the live speed, the signal and today's data use (like the speed-meter apps). */
    val meterNotification: StateFlow<Boolean> = _meterNotification

    private val _speedInBytes = MutableStateFlow(prefs.getBoolean("speedInBytes", true))

    /** The meter shows KB/s and MB/s instead of Mbit/s. */
    val speedInBytes: StateFlow<Boolean> = _speedInBytes

    private val _dailyLimitMb = MutableStateFlow(prefs.getLong("dailyLimitMb", 0))

    /** Warn when today's mobile data reaches this many megabytes; 0 is no limit. */
    val dailyMobileLimitMb: StateFlow<Long> = _dailyLimitMb

    fun setMeterNotification(value: Boolean) { prefs.edit().putBoolean("meterNotification", value).apply(); _meterNotification.value = value }

    fun setSpeedInBytes(value: Boolean) { prefs.edit().putBoolean("speedInBytes", value).apply(); _speedInBytes.value = value }

    fun setDailyMobileLimitMb(value: Long) { prefs.edit().putLong("dailyLimitMb", value).apply(); _dailyLimitMb.value = value }

    private val _statsChoice = MutableStateFlow(if (prefs.contains("statsShare")) prefs.getBoolean("statsShare", false) else null)

    /** Whether the user agreed to share anonymous usage counts: null until they were asked. */
    val statsShare: StateFlow<Boolean?> = _statsChoice

    fun setStatsShare(value: Boolean) { prefs.edit().putBoolean("statsShare", value).apply(); _statsChoice.value = value }

    /** A random number made on first use. It stands for this installation only and says nothing about the person or the phone. */
    fun installId(): String =
        prefs.getString("installId", null) ?: java.util.UUID.randomUUID().toString().also { prefs.edit().putString("installId", it).apply() }

    fun prefsRaw() = prefs

    fun setDesktopMode(value: Boolean) { prefs.edit().putBoolean("desktopMode", value).apply(); _desktopMode.value = value }

    fun setKeepStatus(value: Boolean) { prefs.edit().putBoolean("keepStatus", value).apply(); _keepStatus.value = value }

    fun setOverlay(value: Boolean) { prefs.edit().putBoolean("overlay", value).apply(); _overlay.value = value }

    fun setSeed(value: Boolean) { prefs.edit().putBoolean("seed", value).apply(); _seed.value = value }

    fun setMaxConcurrent(value: Int) { prefs.edit().putInt("maxConcurrent", value).apply(); _maxConcurrent.value = value }

    fun setDownloadDir(value: String?) { prefs.edit().putString("downloadDir", value).apply(); _downloadDir.value = value }

    fun setCellularOnlyWithoutWifi(value: Boolean) {
        prefs.edit().putBoolean("cellularOnlyWithoutWifi", value).apply()
        _cellularOnlyWhenWifiIsAbsent.value = value
    }

    fun setLinkEnabled(id: String, enabled: Boolean) {
        val next = if (enabled) _disabledLinks.value - id else _disabledLinks.value + id
        prefs.edit().putStringSet("disabledLinks", next).apply()
        _disabledLinks.value = next
    }
}
