package app.arqon

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.DataUsage
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.SportsEsports
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.lifecycle.lifecycleScope
import app.arqon.browser.BrowserController
import app.arqon.core.input.InputKind
import app.arqon.core.input.UserInput
import app.arqon.ui.Accent
import app.arqon.ui.ArqonTheme
import app.arqon.ui.Bg
import app.arqon.ui.BrowserScreen
import app.arqon.ui.DownloadsScreen
import app.arqon.ui.GameScreen
import app.arqon.ui.Muted
import app.arqon.ui.Panel
import app.arqon.ui.PanelScreen
import app.arqon.ui.SettingsScreen
import app.arqon.ui.StatsQuestion
import app.arqon.ui.UsageScreen
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    private companion object {
        val LINK = Regex("(magnet:\\?\\S+|https?://\\S+)", RegexOption.IGNORE_CASE)
    }

    private val app get() = application as ArqonApp
    private var browser: BrowserController? = null
    private var tab by mutableIntStateOf(0)

    private val askNotifications = registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED
        ) {
            askNotifications.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        setContent {
            ArqonTheme {
                Root()
            }
        }
        handle(intent)
    }

    override fun onStart() {
        super.onStart()
        if (app.settings.keepStatus.value || app.settings.meterNotification.value) DownloadService.ensureRunning(this)
        // the game mode was on when Arqon was closed: bring it back (Android already has the user's consent for the VPN)
        if (app.settings.gameMode.value && !app.gameMode.state.value.running && android.net.VpnService.prepare(this) == null) {
            app.gameMode.start()
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handle(intent)
    }

    private fun say(text: String) = runOnUiThread { Toast.makeText(this, text, Toast.LENGTH_SHORT).show() }

    /** Magnet links, .torrent files and links opened from, or shared by, other apps. */
    private fun handle(intent: Intent?) {
        if (intent == null) return
        val data: Uri? = if (intent.action == Intent.ACTION_VIEW) intent.data else null
        // "Share" from a browser or a chat: the text may carry words around the link
        val shared: String? = if (intent.action == Intent.ACTION_SEND) intent.getStringExtra(Intent.EXTRA_TEXT) else null
        if (data == null && shared == null) return
        lifecycleScope.launch {
            val error = when {
                data != null && data.scheme.equals("magnet", ignoreCase = true) -> app.downloads.addMagnet(data.toString())
                data != null && (data.scheme == "content" || data.scheme == "file") -> {
                    val bytes = try { contentResolver.openInputStream(data)?.use { it.readBytes() } } catch (_: Exception) { null }
                    if (bytes == null) "bad torrent" else app.downloads.addTorrentBytes(bytes)
                }
                data != null -> if (UserInput.classify(data.toString()) == InputKind.TORRENT_URL) app.downloads.addText(data.toString()) else "not a link"
                else -> {
                    val link = LINK.find(shared.orEmpty())?.value
                    if (link == null) "not a link" else app.downloads.addText(link)
                }
            }
            say(if (error == null) getString(R.string.added) else describe(error))
            if (error == null) tab = 2
        }
    }

    private fun describe(error: String) = when (error) {
        "not a link" -> getString(R.string.err_bad_link)
        "bad magnet" -> getString(R.string.err_bad_magnet)
        "bad torrent" -> getString(R.string.err_bad_torrent)
        "already added" -> getString(R.string.err_already)
        else -> error
    }

    @Composable
    private fun Root() {
        val controller = remember { browser ?: BrowserController(this, app) { text -> say(text) }.also { browser = it } }
        StatsQuestion(app)
        Scaffold(
            containerColor = Bg,
            bottomBar = {
                NavigationBar(containerColor = Panel) {
                    val colors = NavigationBarItemDefaults.colors(
                        selectedIconColor = Color.White, selectedTextColor = Accent, indicatorColor = Accent,
                        unselectedIconColor = Muted, unselectedTextColor = Muted,
                    )
                    NavigationBarItem(tab == 0, { tab = 0 }, { Icon(Icons.Filled.Wifi, null) }, label = { Text(stringResource(R.string.nav_panel), maxLines = 1) }, alwaysShowLabel = false, colors = colors)
                    NavigationBarItem(tab == 1, { tab = 1 }, { Icon(Icons.Filled.Language, null) }, label = { Text(stringResource(R.string.nav_browser), maxLines = 1) }, alwaysShowLabel = false, colors = colors)
                    NavigationBarItem(tab == 2, { tab = 2 }, { Icon(Icons.Filled.Download, null) }, label = { Text(stringResource(R.string.nav_downloads), maxLines = 1) }, alwaysShowLabel = false, colors = colors)
                    NavigationBarItem(tab == 5, { tab = 5 }, { Icon(Icons.Filled.DataUsage, null) }, label = { Text(stringResource(R.string.nav_usage), maxLines = 1) }, alwaysShowLabel = false, colors = colors)
                    NavigationBarItem(tab == 4, { tab = 4 }, { Icon(Icons.Filled.SportsEsports, null) }, label = { Text(stringResource(R.string.nav_game), maxLines = 1) }, alwaysShowLabel = false, colors = colors)
                    NavigationBarItem(tab == 3, { tab = 3 }, { Icon(Icons.Filled.Settings, null) }, label = { Text(stringResource(R.string.nav_settings), maxLines = 1) }, alwaysShowLabel = false, colors = colors)
                }
            },
        ) { padding ->
            Box(Modifier.fillMaxSize().padding(padding)) {
                when (tab) {
                    0 -> PanelScreen(app)
                    1 -> BrowserScreen(app, controller, active = true)
                    2 -> DownloadsScreen(app) { text -> say(text) }
                    4 -> GameScreen(app)
                    5 -> UsageScreen(app)
                    else -> SettingsScreen(app)
                }
            }
        }
    }
}
