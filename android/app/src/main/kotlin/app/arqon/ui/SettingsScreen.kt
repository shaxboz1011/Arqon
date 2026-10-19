package app.arqon.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.LifecycleOwner
import app.arqon.ArqonApp
import app.arqon.BuildConfig
import app.arqon.R

@Composable
fun SettingsScreen(app: ArqonApp) {
    val desktop by app.settings.desktopMode.collectAsState()
    val seed by app.settings.seedAfterComplete.collectAsState()
    val parallel by app.settings.maxConcurrent.collectAsState()
    val cellularOnly by app.settings.cellularOnlyWithoutWifi.collectAsState()
    val keepStatus by app.settings.keepStatus.collectAsState()
    val overlay by app.settings.overlay.collectAsState()
    val context = LocalContext.current
    val notificationPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }

    // The choice is made in Android's own screen, so look again each time the user comes back.
    var unrestricted by remember { mutableStateOf(isUnrestricted(context)) }
    DisposableEffect(context) {
        val owner = context as? LifecycleOwner
        val observer = LifecycleEventObserver { _, event -> if (event == Lifecycle.Event.ON_RESUME) unrestricted = isUnrestricted(context) }
        owner?.lifecycle?.addObserver(observer)
        onDispose { owner?.lifecycle?.removeObserver(observer) }
    }

    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Spacer(Modifier.size(6.dp))
        Text(stringResource(R.string.settings_title), fontSize = 26.sp, fontWeight = FontWeight.SemiBold)

        InfoCard {
            ToggleRow(stringResource(R.string.settings_cellular_wifi), stringResource(R.string.settings_cellular_wifi_body), cellularOnly) { app.settings.setCellularOnlyWithoutWifi(it) }
        }
        InfoCard { ToggleRow(stringResource(R.string.settings_status), stringResource(R.string.settings_status_body), keepStatus) { app.settings.setKeepStatus(it) } }
        InfoCard {
            ToggleRow(stringResource(R.string.settings_overlay), stringResource(R.string.settings_overlay_body), overlay) { app.settings.setOverlay(it) }
            if (overlay && !android.provider.Settings.canDrawOverlays(context)) {
                Spacer(Modifier.size(8.dp))
                Button(onClick = {
                    context.startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:${context.packageName}")))
                }) { Text(stringResource(R.string.settings_overlay_grant)) }
            }
        }
        InfoCard { ToggleRow(stringResource(R.string.settings_desktop), stringResource(R.string.settings_desktop_body), desktop) { app.settings.setDesktopMode(it) } }
        InfoCard { ToggleRow(stringResource(R.string.settings_seed), stringResource(R.string.settings_seed_body), seed) { app.settings.setSeed(it) } }

        InfoCard {
            Text(stringResource(R.string.settings_parallel), fontWeight = FontWeight.Medium)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 8.dp)) {
                for (n in listOf(1, 2, 3, 4, 6)) FilterChip(selected = parallel == n, onClick = { app.settings.setMaxConcurrent(n) }, label = { Text("$n") })
            }
        }

        InfoCard {
            Text(stringResource(R.string.settings_folder), fontWeight = FontWeight.Medium)
            Text(stringResource(R.string.settings_folder_body, app.downloads.downloadDirectory().path), color = Muted, fontSize = 13.sp)
            if (!allFilesGranted()) {
                Spacer(Modifier.size(8.dp))
                Text(stringResource(R.string.settings_all_files_body), color = Muted, fontSize = 13.sp)
                Spacer(Modifier.size(8.dp))
                Button(onClick = {
                    val intent = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                        Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:${context.packageName}"))
                    } else {
                        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:${context.packageName}"))
                    }
                    context.startActivity(intent)
                }) { Text(stringResource(R.string.settings_all_files)) }
            }
        }

        InfoCard {
            Text(stringResource(R.string.settings_battery), fontWeight = FontWeight.Medium)
            if (unrestricted) {
                Text(stringResource(R.string.settings_battery_ok), color = Muted, fontSize = 13.sp)
            } else {
                Text(stringResource(R.string.settings_battery_body), color = Muted, fontSize = 13.sp)
                if (Build.MANUFACTURER.equals("samsung", ignoreCase = true)) {
                    Spacer(Modifier.size(6.dp))
                    Text(stringResource(R.string.settings_battery_samsung), color = Muted, fontSize = 13.sp)
                }
                Spacer(Modifier.size(8.dp))
                Button(onClick = { askBatteryExemption(context) }) { Text(stringResource(R.string.settings_battery_button)) }
            }
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            InfoCard {
                Text(stringResource(R.string.settings_notifications), fontWeight = FontWeight.Medium)
                Text(stringResource(R.string.settings_notifications_body), color = Muted, fontSize = 13.sp)
                Spacer(Modifier.size(8.dp))
                Button(onClick = { notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS) }) { Text(stringResource(R.string.panel_permission_button)) }
            }
        }
        Text(stringResource(R.string.settings_about, BuildConfig.VERSION_NAME), color = Muted, fontSize = 12.sp, modifier = Modifier.padding(vertical = 12.dp))
    }
}

private fun isUnrestricted(context: Context): Boolean =
    (context.getSystemService(Context.POWER_SERVICE) as PowerManager).isIgnoringBatteryOptimizations(context.packageName)

private fun askBatteryExemption(context: Context) {
    val direct = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:${context.packageName}"))
    try {
        context.startActivity(direct)
    } catch (_: Exception) {
        context.startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))
    }
}

private fun allFilesGranted(): Boolean =
    Build.VERSION.SDK_INT < Build.VERSION_CODES.R || Environment.isExternalStorageManager()

@Composable
private fun ToggleRow(title: String, body: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, fontWeight = FontWeight.Medium)
            Text(body, color = Muted, fontSize = 13.sp)
        }
        Switch(checked = checked, onCheckedChange = onChange)
    }
}
