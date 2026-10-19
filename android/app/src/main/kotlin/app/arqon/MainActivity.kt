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
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Language
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
import app.arqon.ui.Muted
import app.arqon.ui.Panel
import app.arqon.ui.PanelScreen
import app.arqon.ui.SettingsScreen
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
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

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handle(intent)
    }

    private fun say(text: String) = runOnUiThread { Toast.makeText(this, text, Toast.LENGTH_SHORT).show() }

    /** Magnet links, .torrent files and links opened from other apps. */
    private fun handle(intent: Intent?) {
        val data: Uri = intent?.data ?: return
        if (intent.action != Intent.ACTION_VIEW) return
        lifecycleScope.launch {
            val text = data.toString()
            val error = when {
                data.scheme.equals("magnet", ignoreCase = true) -> app.downloads.addMagnet(text)
                data.scheme == "content" || data.scheme == "file" -> {
                    val bytes = try { contentResolver.openInputStream(data)?.use { it.readBytes() } } catch (_: Exception) { null }
                    if (bytes == null) "bad torrent" else app.downloads.addTorrentBytes(bytes)
                }
                UserInput.classify(text) == InputKind.TORRENT_URL -> app.downloads.addText(text)
                else -> "not a link"
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
        Scaffold(
            containerColor = Bg,
            bottomBar = {
                NavigationBar(containerColor = Panel) {
                    val colors = NavigationBarItemDefaults.colors(
                        selectedIconColor = Color.White, selectedTextColor = Accent, indicatorColor = Accent,
                        unselectedIconColor = Muted, unselectedTextColor = Muted,
                    )
                    NavigationBarItem(tab == 0, { tab = 0 }, { Icon(Icons.Filled.Wifi, null) }, label = { Text(stringResource(R.string.nav_panel)) }, colors = colors)
                    NavigationBarItem(tab == 1, { tab = 1 }, { Icon(Icons.Filled.Language, null) }, label = { Text(stringResource(R.string.nav_browser)) }, colors = colors)
                    NavigationBarItem(tab == 2, { tab = 2 }, { Icon(Icons.Filled.Download, null) }, label = { Text(stringResource(R.string.nav_downloads)) }, colors = colors)
                    NavigationBarItem(tab == 3, { tab = 3 }, { Icon(Icons.Filled.Settings, null) }, label = { Text(stringResource(R.string.nav_settings)) }, colors = colors)
                }
            },
        ) { padding ->
            Box(Modifier.fillMaxSize().padding(padding)) {
                when (tab) {
                    0 -> PanelScreen(app)
                    1 -> BrowserScreen(app, controller, active = true)
                    2 -> DownloadsScreen(app) { text -> say(text) }
                    else -> SettingsScreen(app)
                }
            }
        }
    }
}
