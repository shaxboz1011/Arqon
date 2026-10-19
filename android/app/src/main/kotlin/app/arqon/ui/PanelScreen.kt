package app.arqon.ui

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import kotlinx.coroutines.launch
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.arqon.ArqonApp
import app.arqon.Format
import app.arqon.R
import app.arqon.core.net.LinkKind
import app.arqon.net.LinkInfo
import app.arqon.net.LinkState

@Composable
fun PanelScreen(app: ArqonApp) {
    val links by app.links.all.collectAsState()
    val speeds by app.downloads.linkSpeeds.collectAsState()
    val active = links.filter { it.state == LinkState.ACTIVE && it.enabled }
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    val tests = androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateMapOf<String, String>() }
    val context = androidx.compose.ui.platform.LocalContext.current
    val askPhone = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        app.links.rebuildSlots()
    }

    LazyColumn(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            Spacer(Modifier.size(16.dp))
            Text(stringResource(R.string.panel_title), fontSize = 26.sp, fontWeight = FontWeight.SemiBold)
            val summary = when (active.size) {
                0 -> stringResource(R.string.panel_none)
                1 -> stringResource(R.string.panel_one)
                else -> stringResource(R.string.panel_many, active.size)
            }
            Text(summary, color = Muted)
            androidx.compose.material3.TextButton(onClick = {
                val clipboard = context.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
                clipboard.setPrimaryClip(android.content.ClipData.newPlainText("Arqon", app.links.report()))
                android.widget.Toast.makeText(context, context.getString(R.string.report_copied), android.widget.Toast.LENGTH_SHORT).show()
            }) { Text(stringResource(R.string.report_copy)) }
        }
        item { TotalCard(speeds.values.sum(), links, speeds) }
        items(links, key = { it.id }) { info ->
            LinkCard(
                info, speeds[info.id] ?: 0, tests[info.id],
                onToggle = { app.settings.setLinkEnabled(info.id, it) },
                onTest = { scope.launch { tests[info.id] = "…"; tests[info.id] = app.links.test(info) } },
            )
        }
        if (!app.links.hasPhonePermission()) {
            item {
                InfoCard {
                    Text(stringResource(R.string.panel_permission), color = Muted, fontSize = 14.sp)
                    Spacer(Modifier.size(8.dp))
                    Button(onClick = { askPhone.launch(Manifest.permission.READ_PHONE_STATE) }) { Text(stringResource(R.string.panel_permission_button)) }
                }
            }
        }
        if (links.any { it.kind == LinkKind.WIFI && it.state == LinkState.ACTIVE } &&
            links.none { it.kind == LinkKind.CELLULAR && it.state == LinkState.ACTIVE } &&
            links.any { it.kind == LinkKind.CELLULAR && it.state == LinkState.UNAVAILABLE }
        ) {
            item {
                InfoCard {
                    val off = app.links.mobileDataEnabled() == false
                    Text(stringResource(if (off) R.string.hint_data_off else R.string.hint_data_refused), color = Muted, fontSize = 14.sp)
                }
            }
        }
        item {
            InfoCard {
                if (active.size < 2) Text(stringResource(R.string.panel_hint_single), color = Muted, fontSize = 14.sp)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    Spacer(Modifier.size(6.dp))
                    Text(stringResource(R.string.panel_sim_note), color = Muted, fontSize = 13.sp)
                }
            }
            Spacer(Modifier.size(16.dp))
        }
    }
}

@Composable
private fun TotalCard(total: Long, links: List<LinkInfo>, speeds: Map<String, Long>) {
    InfoCard {
        Text(stringResource(R.string.panel_speed_total, Format.speed(total)), fontSize = 18.sp, fontWeight = FontWeight.Medium)
        Spacer(Modifier.size(8.dp))
        // one bar, split by what each link carries right now
        Row(Modifier.fillMaxWidth().height(10.dp).clip(RoundedCornerShape(5.dp)).background(Panel2)) {
            if (total > 0) {
                for (info in links) {
                    val part = speeds[info.id] ?: 0
                    if (part > 0) Box(Modifier.weight(part.toFloat()).fillMaxHeight().background(linkColor(info)))
                }
            }
        }
    }
}

@Composable
private fun LinkCard(info: LinkInfo, speed: Long, testResult: String?, onToggle: (Boolean) -> Unit, onTest: () -> Unit) {
    InfoCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(12.dp).clip(CircleShape).background(if (info.state == LinkState.ACTIVE && info.enabled) linkColor(info) else Muted))
            Spacer(Modifier.size(10.dp))
            Column(Modifier.weight(1f)) {
                Text(info.name, fontSize = 17.sp, fontWeight = FontWeight.Medium)
                val state = when {
                    !info.enabled -> stringResource(R.string.link_off)
                    info.state == LinkState.ACTIVE -> stringResource(R.string.link_active)
                    info.state == LinkState.WAITING -> stringResource(R.string.link_waiting)
                    else -> stringResource(R.string.link_unavailable)
                }
                Text(state, color = Muted, fontSize = 13.sp)
            }
            Switch(checked = info.enabled, onCheckedChange = onToggle)
        }
        if (info.state == LinkState.ACTIVE) {
            Spacer(Modifier.size(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                Text(if (info.pingMillis != null) stringResource(R.string.link_ping, info.pingMillis) else stringResource(R.string.link_ping_none), fontSize = 13.sp)
                Text(Format.speed(speed), fontSize = 13.sp, color = linkColor(info))
                Text(if (info.metered) stringResource(R.string.link_metered) else stringResource(R.string.link_free), fontSize = 13.sp, color = Muted)
            }
            androidx.compose.material3.TextButton(onClick = onTest) { Text(stringResource(R.string.link_test)) }
            if (testResult != null) Text(testResult, fontSize = 12.sp, color = if (testResult.startsWith("OK")) WifiColor else Muted)
        } else if (info.state == LinkState.UNAVAILABLE) {
            Spacer(Modifier.size(6.dp))
            Text(stringResource(R.string.link_unavailable_body), color = Muted, fontSize = 13.sp)
        }
    }
}

@Composable
fun InfoCard(content: @Composable () -> Unit) {
    Card(
        Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(containerColor = Panel),
        border = BorderStroke(1.dp, Line),
    ) {
        Column(Modifier.padding(14.dp)) { content() }
    }
}
