package app.arqon.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import app.arqon.core.net.LinkKind
import app.arqon.net.LinkInfo

val Bg = Color(0xFF0F1218)
val Panel = Color(0xFF171B24)
val Panel2 = Color(0xFF1E2330)
val Line = Color(0xFF2A3040)
val Muted = Color(0xFF8B93A7)
val Accent = Color(0xFF6C8CFF)
val WifiColor = Color(0xFF4ADE80)
val SimColor1 = Color(0xFFF59E0B)
val SimColor2 = Color(0xFFC084FC)
val OtherColor = Color(0xFF38BDF8)
val Bad = Color(0xFFF87171)

private val scheme = darkColorScheme(
    primary = Accent, onPrimary = Color.White, background = Bg, onBackground = Color(0xFFE7EAF2),
    surface = Panel, onSurface = Color(0xFFE7EAF2), surfaceVariant = Panel2, onSurfaceVariant = Muted,
    outline = Line, error = Bad, secondaryContainer = Panel2, onSecondaryContainer = Color(0xFFE7EAF2),
)

@Composable
fun ArqonTheme(content: @Composable () -> Unit) {
    // The app is dark like the desktop version; the system theme does not change it.
    @Suppress("UNUSED_VARIABLE") val unused = isSystemInDarkTheme()
    MaterialTheme(colorScheme = scheme, content = content)
}

/** The same link always gets the same colour: Wi-Fi green, the first SIM amber, the second SIM purple. */
fun linkColor(id: String): Color = when {
    id == "wifi" -> WifiColor
    id.startsWith("sim") || id == "mobile" -> if (id.hashCode() % 2 == 0) SimColor1 else SimColor2
    else -> OtherColor
}

fun linkColor(info: LinkInfo): Color = when (info.kind) {
    LinkKind.WIFI -> WifiColor
    LinkKind.CELLULAR -> linkColor(info.id)
    else -> OtherColor
}
