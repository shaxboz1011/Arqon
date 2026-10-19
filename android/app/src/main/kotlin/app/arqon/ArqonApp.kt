package app.arqon

import android.app.Application
import app.arqon.download.DownloadCenter
import app.arqon.net.AndroidLinkManager
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob

/** Holds the things that live as long as the process: the settings, the networks and the downloads. */
class ArqonApp : Application() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    lateinit var settings: AppSettings
        private set
    lateinit var links: AndroidLinkManager
        private set
    lateinit var downloads: DownloadCenter
        private set
    lateinit var speedTester: app.arqon.net.SpeedTester
        private set

    lateinit var gameMode: app.arqon.gamemode.GameMode
        private set

    lateinit var stats: app.arqon.stats.Stats
        private set

    lateinit var usage: app.arqon.meter.UsageMonitor
        private set

    override fun onCreate() {
        super.onCreate()
        settings = AppSettings(this)
        links = AndroidLinkManager(this, settings, scope)
        gameMode = app.arqon.gamemode.GameMode(this, settings, links, scope)
        downloads = DownloadCenter(this, links, settings, scope) { summary -> if (summary.running > 0) DownloadService.ensureRunning(this) }
        speedTester = app.arqon.net.SpeedTester(links, scope)
        usage = app.arqon.meter.UsageMonitor(this, settings, scope)
        usage.start()
        stats = app.arqon.stats.Stats(this, settings, links, scope)
        stats.start()
        links.start()
        downloads.start()
    }
}
