package app.arqon.gamemode

import android.content.Intent
import android.content.pm.PackageManager
import android.net.VpnService
import android.os.Build
import android.os.ParcelFileDescriptor
import app.arqon.ArqonApp
import app.arqon.DownloadService
import app.arqon.core.vpn.Tunnel
import java.io.FileInputStream
import java.io.FileOutputStream

/**
 * Android's VPN service, used here only as a way to receive the chosen apps' packets (Android has no other way to see a game's
 * traffic). Nothing leaves the phone through a server of ours: [Tunnel] sends every connection straight out over the best
 * network of the phone.
 */
class GameVpnService : VpnService() {
    private var descriptor: ParcelFileDescriptor? = null
    private var tunnel: Tunnel? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            shutDown(null)
            stopSelf()
        } else {
            startTunnel()
        }
        return START_NOT_STICKY
    }

    private fun startTunnel() {
        if (tunnel != null) return
        val app = application as ArqonApp
        val builder = Builder()
            .setSession("Arqon")
            .setMtu(MTU)
            .addAddress("10.77.0.2", 32)
            .addRoute("0.0.0.0", 0)
            .addDnsServer("1.1.1.1")
            .addDnsServer("8.8.8.8")
            .setBlocking(true)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) builder.setMetered(false)

        // Only the chosen apps go through the tunnel; everything else (and Arqon itself) keeps its normal way.
        var added = 0
        for (name in app.settings.gamePackages.value) {
            try {
                builder.addAllowedApplication(name)
                added++
            } catch (_: PackageManager.NameNotFoundException) {
                // uninstalled since it was chosen
            }
        }
        if (added == 0) {
            fail(app, "no apps")
            return
        }
        val opened = try { builder.establish() } catch (e: Exception) { null }
        if (opened == null) {
            fail(app, "Android did not allow the tunnel")
            return
        }
        descriptor = opened
        DownloadService.ensureRunning(this)   // keeps Arqon alive in the background while a game is on

        val started = Tunnel(
            FileInputStream(opened.fileDescriptor), FileOutputStream(opened.fileDescriptor),
            chooseLink = { app.gameMode.linkForGame() }, mtu = MTU,
            log = { app.links.note("game: $it") },
        )
        tunnel = started
        app.gameMode.tunnelStarted(started)
        Thread({
            started.run()
            if (tunnel === started) shutDown(null)   // the tunnel ended by itself (for example Android took the VPN away)
        }, "arqon-game-tunnel").apply { isDaemon = true }.start()
    }

    private fun fail(app: ArqonApp, reason: String) {
        app.settings.setGameMode(false)
        app.gameMode.tunnelEnded(null, reason)
        stopSelf()
    }

    private fun shutDown(reason: String?) {
        val ended = tunnel
        tunnel = null
        try { ended?.close() } catch (_: Exception) { }
        try { descriptor?.close() } catch (_: Exception) { }
        descriptor = null
        (application as ArqonApp).gameMode.tunnelEnded(ended, reason)
    }

    /** Another VPN app took over, or the user switched Arqon's VPN off in Android's settings. */
    override fun onRevoke() {
        (application as ArqonApp).settings.setGameMode(false)
        shutDown("another VPN took over")
        stopSelf()
        super.onRevoke()
    }

    override fun onDestroy() {
        shutDown(null)
        super.onDestroy()
    }

    companion object {
        const val ACTION_START = "app.arqon.GAME_START"
        const val ACTION_STOP = "app.arqon.GAME_STOP"
        private const val MTU = 1400
    }
}
