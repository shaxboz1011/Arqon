package app.arqon.gamemode

import android.content.Context
import android.content.Intent
import app.arqon.AppSettings
import app.arqon.core.net.Link
import app.arqon.core.vpn.GameLinkChooser
import app.arqon.core.vpn.Tunnel
import app.arqon.core.vpn.TunnelStats
import app.arqon.net.AndroidLinkManager
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/** What the game mode screen and the notification show. */
data class GameState(
    val running: Boolean = false,
    /** A game is sending data right now. */
    val live: Boolean = false,
    val linkId: String? = null,
    val linkName: String? = null,
    val pingMillis: Int? = null,
    val stats: TunnelStats? = null,
    val error: String? = null,
)

/**
 * The game mode: the chosen apps' traffic goes through a VPN tunnel ([GameVpnService]) and is sent over the best link, while Arqon's own
 * downloads keep to the other links, so a download can no longer fill the connection a game plays on.
 */
class GameMode(
    private val context: Context,
    private val settings: AppSettings,
    private val links: AndroidLinkManager,
    private val scope: CoroutineScope,
) {
    private val chooser = GameLinkChooser()
    private val _state = MutableStateFlow(GameState())
    val state: StateFlow<GameState> = _state

    @Volatile private var tunnel: Tunnel? = null
    private var watcher: Job? = null

    /** The link for a new game connection: the one chosen at the last check. */
    fun linkForGame(): Link? {
        val id = chooser.current() ?: chooser.choose(links.gameCandidates())
        return links.linkById(id) ?: links.linkById(chooser.choose(links.gameCandidates()))
    }

    /** Asks the service to build the tunnel (the user has already agreed to the VPN in Android's dialog). */
    fun start() {
        app.arqon.stats.Features.use("game_mode")
        if (settings.gamePackages.value.isEmpty()) {
            _state.value = GameState(error = "no apps")
            return
        }
        settings.setGameMode(true)
        _state.value = GameState()
        context.startService(Intent(context, GameVpnService::class.java).setAction(GameVpnService.ACTION_START))
    }

    fun stop() {
        settings.setGameMode(false)
        try {
            context.startService(Intent(context, GameVpnService::class.java).setAction(GameVpnService.ACTION_STOP))
        } catch (_: Exception) {
            tunnelEnded(null)
        }
    }

    fun tunnelStarted(started: Tunnel) {
        tunnel = started
        links.note("game mode: tunnel is up for ${settings.gamePackages.value.size} app(s)")
        watcher?.cancel()
        watcher = scope.launch {
            while (isActive && tunnel === started) {
                refresh(started)
                delay(1000)
            }
        }
        refresh(started)
    }

    fun tunnelEnded(ended: Tunnel?, error: String? = null) {
        if (ended != null && tunnel !== ended) return
        tunnel = null
        watcher?.cancel()
        links.reservedForGame = null
        links.note("game mode: tunnel is down" + (error?.let { " ($it)" } ?: ""))
        _state.value = GameState(error = error)
    }

    private fun refresh(t: Tunnel) {
        val id = chooser.choose(links.gameCandidates())
        val info = links.all.value.firstOrNull { it.id == id }
        val stats = t.stats()
        val live = stats.lastDataAt > 0 && System.currentTimeMillis() - stats.lastDataAt < LIVE_MILLIS
        links.reservedForGame = if (live) id else null
        _state.value = GameState(
            running = true, live = live, linkId = id,
            linkName = info?.name?.substringBefore(" ·"), pingMillis = info?.pingMillis, stats = stats,
        )
    }

    private companion object {
        const val LIVE_MILLIS = 8000L
    }
}
