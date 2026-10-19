package app.arqon.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.arqon.ArqonApp
import app.arqon.R

/** The one-time question, asked only when this build can send reports at all. */
@Composable
fun StatsQuestion(app: ArqonApp) {
    val choice by app.settings.statsShare.collectAsState()
    if (choice != null || !app.stats.canSend) return
    AlertDialog(
        onDismissRequest = { },
        title = { Text(stringResource(R.string.stats_ask_title)) },
        text = {
            Column {
                Text(stringResource(R.string.stats_ask_body), fontSize = 14.sp)
                Spacer(Modifier.size(8.dp))
                Text(stringResource(R.string.stats_ask_never), color = Muted, fontSize = 13.sp)
            }
        },
        confirmButton = { TextButton(onClick = { app.settings.setStatsShare(true) }) { Text(stringResource(R.string.stats_yes)) } },
        dismissButton = { TextButton(onClick = { app.settings.setStatsShare(false) }) { Text(stringResource(R.string.stats_no)) } },
    )
}

/** In the settings: the switch, and the exact text that would be sent, so nothing about it is a secret. */
@Composable
fun StatsCard(app: ArqonApp) {
    val choice by app.settings.statsShare.collectAsState()
    var showing by remember { mutableStateOf(false) }
    InfoCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(stringResource(R.string.stats_title), fontWeight = FontWeight.Medium)
                Text(
                    stringResource(if (app.stats.canSend) R.string.stats_body else R.string.stats_none_configured),
                    color = Muted, fontSize = 13.sp,
                )
            }
            if (app.stats.canSend) Switch(checked = choice == true, onCheckedChange = { app.settings.setStatsShare(it) })
        }
        TextButton(onClick = { showing = !showing }, modifier = Modifier.padding(top = 4.dp)) { Text(stringResource(R.string.stats_show)) }
        if (showing) {
            Text(app.stats.preview(), color = Muted, fontSize = 11.sp, fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace)
            Spacer(Modifier.size(6.dp))
            val totals = app.stats.totals()
            if (totals.isNotEmpty()) {
                Text(stringResource(R.string.stats_local), fontSize = 12.sp, color = Muted)
                Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    for ((feature, n) in totals) Text("$feature: $n", color = Muted, fontSize = 11.sp)
                }
            }
        }
    }
}
