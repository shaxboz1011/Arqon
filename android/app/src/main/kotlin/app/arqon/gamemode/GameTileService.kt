package app.arqon.gamemode

import android.app.PendingIntent
import android.net.VpnService
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import android.content.Intent
import app.arqon.ArqonApp
import app.arqon.MainActivity
import app.arqon.stats.Features

/** The game mode as a tile in the quick settings: one touch to start or stop it, also while a game is on screen. */
class GameTileService : TileService() {
    override fun onStartListening() {
        super.onStartListening()
        refresh()
    }

    override fun onClick() {
        val app = application as ArqonApp
        Features.use("game_tile")
        if (app.gameMode.state.value.running) {
            app.gameMode.stop()
        } else if (app.settings.gamePackages.value.isEmpty() || VpnService.prepare(this) != null) {
            // nothing chosen yet, or Android's VPN question was never answered: that needs the app
            val open = Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                // Android 14 refuses the Intent form for apps like this one
                startActivityAndCollapse(PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_IMMUTABLE))
            } else {
                @Suppress("DEPRECATION")
                startActivityAndCollapse(open)
            }
        } else {
            app.gameMode.start()
        }
        refresh()
    }

    private fun refresh() {
        val tile = qsTile ?: return
        val running = (application as ArqonApp).gameMode.state.value.running
        tile.state = if (running) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
        tile.updateTile()
    }
}
