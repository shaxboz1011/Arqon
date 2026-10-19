package app.arqon.ui

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.net.VpnService
import android.os.Build
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Checkbox
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.arqon.ArqonApp
import app.arqon.Format
import app.arqon.R
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

private class InstalledApp(val name: String, val label: String, val isGame: Boolean)

/** Apps with a launcher icon (without this app itself), games marked, in the order they are shown to the user. */
private fun installedApps(context: Context): List<InstalledApp> {
    val manager = context.packageManager
    val launcher = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
    val found = manager.queryIntentActivities(launcher, 0)
    return found.mapNotNull { resolved ->
        val info = resolved.activityInfo?.applicationInfo ?: return@mapNotNull null
        if (info.packageName == context.packageName) return@mapNotNull null
        val game = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            info.category == ApplicationInfo.CATEGORY_GAME
        } else {
            @Suppress("DEPRECATION") (info.flags and ApplicationInfo.FLAG_IS_GAME) != 0
        }
        InstalledApp(info.packageName, manager.getApplicationLabel(info).toString(), game)
    }.distinctBy { it.name }
}

@Composable
fun GameScreen(app: ArqonApp) {
    val state by app.gameMode.state.collectAsState()
    val chosen by app.settings.gamePackages.collectAsState()
    val context = LocalContext.current
    var query by rememberSaveable { mutableStateOf("") }
    val installed by produceState(emptyList<InstalledApp>()) { value = withContext(Dispatchers.Default) { installedApps(context) } }

    val consent = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        if (result.resultCode == Activity.RESULT_OK) app.gameMode.start()
    }

    fun turnOn() {
        if (chosen.isEmpty()) {
            Toast.makeText(context, context.getString(R.string.game_pick_first), Toast.LENGTH_LONG).show()
            return
        }
        val intent = VpnService.prepare(context)
        if (intent == null) app.gameMode.start() else consent.launch(intent)   // Android's own "connection request" dialog
    }

    val shown = installed
        .filter { query.isBlank() || it.label.contains(query, ignoreCase = true) }
        .sortedWith(compareByDescending<InstalledApp> { it.name in chosen }.thenByDescending { it.isGame }.thenBy { it.label.lowercase() })

    LazyColumn(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            Spacer(Modifier.size(16.dp))
            Text(stringResource(R.string.game_title), fontSize = 26.sp, fontWeight = FontWeight.SemiBold)
            Text(stringResource(R.string.game_intro), color = Muted, fontSize = 13.sp)
        }
        item {
            InfoCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(stringResource(R.string.game_switch), fontWeight = FontWeight.Medium)
                        Text(stringResource(R.string.game_switch_body), color = Muted, fontSize = 13.sp)
                    }
                    Switch(checked = state.running, onCheckedChange = { if (it) turnOn() else app.gameMode.stop() })
                }
                state.error?.let { error ->
                    Spacer(Modifier.size(8.dp))
                    Text(if (error == "no apps") stringResource(R.string.game_pick_first) else stringResource(R.string.game_error, error), color = Bad, fontSize = 13.sp)
                }
            }
        }
        if (state.running) item {
            InfoCard {
                val link = state.linkName ?: "–"
                Text(stringResource(R.string.game_link, link) + (state.pingMillis?.let { " · " + stringResource(R.string.link_ping, it) } ?: ""), fontWeight = FontWeight.Medium)
                Text(
                    stringResource(if (state.live) R.string.game_live else R.string.game_waiting),
                    color = if (state.live) WifiColor else Muted, fontSize = 13.sp,
                )
                state.stats?.let { stats ->
                    Spacer(Modifier.size(4.dp))
                    Text(stringResource(R.string.game_flows, stats.tcpFlows, stats.udpFlows), color = Muted, fontSize = 13.sp)
                    Text("↑ ${Format.size(stats.bytesUp)}   ↓ ${Format.size(stats.bytesDown)}", color = Muted, fontSize = 13.sp)
                }
            }
        }
        item {
            Text(stringResource(R.string.game_apps), fontSize = 18.sp, fontWeight = FontWeight.Medium)
            Text(stringResource(R.string.game_apps_body), color = Muted, fontSize = 13.sp)
            Spacer(Modifier.size(6.dp))
            OutlinedTextField(
                value = query, onValueChange = { query = it }, singleLine = true,
                label = { Text(stringResource(R.string.game_search)) }, modifier = Modifier.fillMaxWidth(),
            )
        }
        items(shown, key = { it.name }) { entry ->
            val selected = entry.name in chosen
            Row(
                Modifier.fillMaxWidth().clickable { app.settings.setGamePackage(entry.name, !selected) },
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Checkbox(checked = selected, onCheckedChange = { app.settings.setGamePackage(entry.name, it) })
                Column(Modifier.weight(1f)) {
                    Text(entry.label, fontWeight = if (entry.isGame) FontWeight.Medium else FontWeight.Normal)
                    Text(entry.name, color = Muted, fontSize = 11.sp)
                }
                if (entry.isGame) Text(stringResource(R.string.game_tag), color = Accent, fontSize = 12.sp)
            }
        }
        item { Spacer(Modifier.size(16.dp)) }
    }
}
