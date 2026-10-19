package app.arqon.ui

import android.view.ViewGroup
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import app.arqon.ArqonApp
import app.arqon.R
import app.arqon.browser.BrowserController

@Composable
fun BrowserScreen(app: ArqonApp, controller: BrowserController, active: Boolean) {
    val url by controller.url.collectAsState()
    val progress by controller.progress.collectAsState()
    val canBack by controller.canGoBack.collectAsState()
    val canForward by controller.canGoForward.collectAsState()
    val desktop by app.settings.desktopMode.collectAsState()
    var field by remember { mutableStateOf(url) }
    LaunchedEffect(url) { field = url }
    LaunchedEffect(desktop) { controller.applyMode(desktop) }
    BackHandler(enabled = active && canBack) { controller.goBack() }

    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = controller::goBack, enabled = canBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.browser_back)) }
            IconButton(onClick = controller::goForward, enabled = canForward) { Icon(Icons.AutoMirrored.Filled.ArrowForward, stringResource(R.string.browser_forward)) }
            OutlinedTextField(
                value = field, onValueChange = { field = it }, singleLine = true, modifier = Modifier.weight(1f),
                textStyle = androidx.compose.ui.text.TextStyle(fontSize = 14.sp),
                placeholder = { Text(stringResource(R.string.browser_hint), fontSize = 14.sp) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go),
                keyboardActions = KeyboardActions(onGo = { controller.load(field) }),
            )
            IconButton(onClick = controller::reload) { Icon(Icons.Filled.Refresh, stringResource(R.string.browser_reload)) }
            IconButton(onClick = { app.settings.setDesktopMode(!desktop) }) {
                Icon(Icons.Filled.Computer, stringResource(R.string.browser_desktop), tint = if (desktop) Accent else Muted)
            }
        }
        if (progress in 1..99) LinearProgressIndicator(progress = { progress / 100f }, modifier = Modifier.fillMaxWidth())
        AndroidView(
            modifier = Modifier.fillMaxSize(),
            factory = {
                // the same page view is used every time the tab is shown, so it must leave its old parent first
                (controller.webView.parent as? ViewGroup)?.removeView(controller.webView)
                controller.webView
            },
            update = { },
        )
    }
}
