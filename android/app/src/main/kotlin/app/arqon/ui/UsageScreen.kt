package app.arqon.ui

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.widget.Toast
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.LifecycleOwner
import app.arqon.ArqonApp
import app.arqon.stats.Features
import app.arqon.Format
import app.arqon.R
import app.arqon.core.usage.Generation
import app.arqon.core.usage.Traffic
import app.arqon.meter.AppUsage

@Composable
fun UsageScreen(app: ArqonApp) {
    val meter by app.usage.state.collectAsState()
    val notification by app.settings.meterNotification.collectAsState()
    val inBytes by app.settings.speedInBytes.collectAsState()
    val limitMb by app.settings.dailyMobileLimitMb.collectAsState()
    val context = LocalContext.current

    // usage access is given in Android's own screen: look again each time the user comes back
    var access by remember { mutableStateOf(app.usage.hasUsageAccess()) }
    DisposableEffect(context) {
        val owner = context as? LifecycleOwner
        val observer = LifecycleEventObserver { _, event -> if (event == Lifecycle.Event.ON_RESUME) access = app.usage.hasUsageAccess() }
        owner?.lifecycle?.addObserver(observer)
        onDispose { owner?.lifecycle?.removeObserver(observer) }
    }
    var topApps by remember { mutableStateOf<List<AppUsage>>(emptyList()) }
    LaunchedEffect(access) {
        while (access) {
            topApps = app.usage.topAppsToday()
            kotlinx.coroutines.delay(20_000)
        }
    }

    LaunchedEffect(Unit) { Features.use("usage_screen") }

    LazyColumn(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            Spacer(Modifier.size(16.dp))
            Text(stringResource(R.string.usage_title), fontSize = 26.sp, fontWeight = FontWeight.SemiBold)
        }
        item {
            InfoCard {
                Text(stringResource(R.string.usage_speed), color = Muted, fontSize = 13.sp)
                val (downValue, downUnit) = Format.meter(meter.downBytesPerSecond, inBytes)
                val (upValue, upUnit) = Format.meter(meter.upBytesPerSecond, inBytes)
                Row(verticalAlignment = Alignment.Bottom) {
                    Text("↓ $downValue", fontSize = 34.sp, fontWeight = FontWeight.SemiBold, color = WifiColor)
                    Text(" $downUnit", color = Muted, modifier = Modifier.padding(start = 4.dp, bottom = 6.dp))
                }
                Text("↑ $upValue $upUnit", color = SimColor1, fontSize = 16.sp)
                Spacer(Modifier.size(6.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    if (!meter.signalIsWifi && meter.generation != Generation.UNKNOWN) Text(meter.generation.label, color = Accent, fontWeight = FontWeight.Medium)
                    meter.signalPercent?.let { Text(stringResource(R.string.usage_signal, it), color = Muted) }
                }
            }
        }
        item { TotalsCard(stringResource(R.string.usage_today), meter.todayWifi, meter.todayMobile) }
        item { TotalsCard(stringResource(R.string.usage_month), meter.monthWifi, meter.monthMobile) }
        item {
            Text(
                stringResource(if (meter.exact) R.string.usage_exact else R.string.usage_estimated),
                color = Muted, fontSize = 12.sp,
            )
        }
        item {
            InfoCard {
                Text(stringResource(R.string.usage_week), fontWeight = FontWeight.Medium)
                Spacer(Modifier.size(8.dp))
                WeekChart(meter.week)
            }
        }
        if (!access) item {
            InfoCard {
                Text(stringResource(R.string.usage_grant), fontWeight = FontWeight.Medium)
                Text(stringResource(R.string.usage_grant_body), color = Muted, fontSize = 13.sp)
                Spacer(Modifier.size(8.dp))
                Button(onClick = { openUsageAccess(context) }) { Text(stringResource(R.string.usage_grant_button)) }
            }
        }
        if (access && topApps.isNotEmpty()) item {
            InfoCard {
                Text(stringResource(R.string.usage_top_apps), fontWeight = FontWeight.Medium)
                Spacer(Modifier.size(6.dp))
                val biggest = topApps.first().bytes.coerceAtLeast(1)
                for (entry in topApps) {
                    Row(Modifier.fillMaxWidth().padding(vertical = 3.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(entry.label, fontSize = 14.sp, maxLines = 1)
                            Canvas(Modifier.fillMaxWidth().height(4.dp).padding(top = 2.dp)) {
                                drawRoundRect(Line, size = size, cornerRadius = CornerRadius(4f, 4f))
                                drawRoundRect(Accent, size = Size(size.width * entry.bytes / biggest, size.height), cornerRadius = CornerRadius(4f, 4f))
                            }
                        }
                        Text(Format.size(entry.bytes), color = Muted, fontSize = 13.sp, modifier = Modifier.padding(start = 12.dp))
                    }
                }
            }
        }
        item { NetworkModeCard(meter.generation, context) }
        item {
            InfoCard {
                Text(stringResource(R.string.usage_limit), fontWeight = FontWeight.Medium)
                Text(stringResource(R.string.usage_limit_body), color = Muted, fontSize = 13.sp)
                Spacer(Modifier.size(8.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    for (mb in listOf(0L, 500L, 1024L, 2048L, 5120L)) {
                        FilterChip(
                            selected = limitMb == mb, onClick = { app.settings.setDailyMobileLimitMb(mb) },
                            label = { Text(if (mb == 0L) stringResource(R.string.usage_limit_off) else Format.size(mb * 1024 * 1024), fontSize = 12.sp) },
                        )
                    }
                }
                if (limitMb > 0) {
                    Spacer(Modifier.size(6.dp))
                    Text(stringResource(R.string.usage_limit_used, Format.size(meter.todayMobile.total), Format.size(limitMb * 1024 * 1024)), color = Muted, fontSize = 13.sp)
                }
            }
        }
        item {
            InfoCard {
                Text(stringResource(R.string.usage_meter), fontWeight = FontWeight.Medium)
                Spacer(Modifier.size(6.dp))
                SwitchRow(stringResource(R.string.usage_meter_notification), stringResource(R.string.usage_meter_notification_body), notification) { app.settings.setMeterNotification(it) }
                Spacer(Modifier.size(6.dp))
                SwitchRow(stringResource(R.string.usage_bytes), stringResource(R.string.usage_bytes_body), inBytes) { app.settings.setSpeedInBytes(it) }
            }
        }
        item { Spacer(Modifier.size(16.dp)) }
    }
}

@Composable
private fun SwitchRow(title: String, body: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title)
            Text(body, color = Muted, fontSize = 12.sp)
        }
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

@Composable
private fun TotalsCard(title: String, wifi: Traffic, mobile: Traffic) {
    InfoCard {
        Text(title, fontWeight = FontWeight.Medium)
        Spacer(Modifier.size(6.dp))
        TotalRow(stringResource(R.string.usage_wifi), wifi, WifiColor)
        TotalRow(stringResource(R.string.usage_mobile), mobile, SimColor1)
    }
}

@Composable
private fun TotalRow(name: String, traffic: Traffic, color: androidx.compose.ui.graphics.Color) {
    Row(Modifier.fillMaxWidth().padding(vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(name, color = color, modifier = Modifier.weight(1f))
        Text("↓ ${Format.size(traffic.rx)}   ↑ ${Format.size(traffic.tx)}", color = Muted, fontSize = 13.sp)
        Text(Format.size(traffic.total), fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 12.dp))
    }
}

/** Seven bars, Wi-Fi under mobile data, each as tall as that day's use compared with the busiest day. */
@Composable
private fun WeekChart(days: List<app.arqon.core.usage.DayUsage>) {
    val biggest = (days.maxOfOrNull { it.total } ?: 0L).coerceAtLeast(1)
    Row(Modifier.fillMaxWidth().height(110.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.Bottom) {
        for (day in days) {
            Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                Canvas(Modifier.fillMaxWidth().height(80.dp)) {
                    val wifiHeight = size.height * day.wifi.total / biggest
                    val mobileHeight = size.height * day.mobile.total / biggest
                    drawRoundRect(Line, topLeft = Offset(0f, size.height - 2f), size = Size(size.width, 2f))
                    drawRect(WifiColor, topLeft = Offset(0f, size.height - wifiHeight), size = Size(size.width, wifiHeight))
                    drawRect(SimColor1, topLeft = Offset(0f, size.height - wifiHeight - mobileHeight), size = Size(size.width, mobileHeight))
                }
                Text(day.date.dayOfMonth.toString(), color = Muted, fontSize = 11.sp)
                Text(if (day.total > 0) Format.size(day.total) else "", color = Muted, fontSize = 9.sp, maxLines = 1)
            }
        }
    }
}

@Composable
private fun NetworkModeCard(generation: Generation, context: Context) {
    InfoCard {
        Text(stringResource(R.string.usage_network), fontWeight = FontWeight.Medium)
        Text(stringResource(R.string.usage_network_now, if (generation == Generation.UNKNOWN) "–" else generation.label), color = Accent, fontSize = 14.sp)
        Text(stringResource(R.string.usage_network_body), color = Muted, fontSize = 13.sp)
        Spacer(Modifier.size(8.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = { openNetworkSettings(context) }) { Text(stringResource(R.string.usage_network_settings), fontSize = 12.sp) }
            OutlinedButton(onClick = { openRadioInfo(context) }) { Text(stringResource(R.string.usage_network_radio), fontSize = 12.sp) }
        }
    }
}

private fun openUsageAccess(context: Context) {
    try {
        context.startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS, Uri.parse("package:${context.packageName}")))
    } catch (_: Exception) {
        context.startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS))
    }
}

/** The phone's own "mobile networks" screen, where the network mode (5G / 4G / 3G / 2G) is chosen. */
private fun openNetworkSettings(context: Context) {
    Features.use("network_settings")
    for (action in listOf(Settings.ACTION_NETWORK_OPERATOR_SETTINGS, Settings.ACTION_DATA_ROAMING_SETTINGS, Settings.ACTION_WIRELESS_SETTINGS)) {
        try {
            context.startActivity(Intent(action))
            return
        } catch (_: Exception) {
        }
    }
}

/** The hidden "phone information" screen of Android, where the preferred network type can be set; many makers block it. */
private fun openRadioInfo(context: Context) {
    val attempts = listOf(
        Intent().setClassName("com.android.phone", "com.android.phone.settings.RadioInfo"),
        Intent().setClassName("com.android.settings", "com.android.settings.RadioInfo"),
    )
    for (intent in attempts) {
        try {
            context.startActivity(intent)
            return
        } catch (_: Exception) {
        }
    }
    Toast.makeText(context, context.getString(R.string.usage_network_radio_failed), Toast.LENGTH_LONG).show()
}
