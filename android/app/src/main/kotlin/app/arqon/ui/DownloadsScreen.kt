package app.arqon.ui

import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ContentPaste
import androidx.compose.material.icons.filled.FileOpen
import androidx.compose.material.icons.filled.FolderOpen
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.arqon.ArqonApp
import app.arqon.Format
import app.arqon.R
import app.arqon.core.torrent.Phase
import app.arqon.download.DownloadItem
import app.arqon.download.ItemKind
import app.arqon.download.ItemState
import kotlinx.coroutines.launch

@Composable
fun DownloadsScreen(app: ArqonApp, messages: (String) -> Unit) {
    val items by app.downloads.items.collectAsState()
    var text by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    var removing by remember { mutableStateOf<DownloadItem?>(null) }
    var choosing by remember { mutableStateOf<DownloadItem?>(null) }

    val added = stringResource(R.string.added)
    val badLink = stringResource(R.string.err_bad_link)
    val badMagnet = stringResource(R.string.err_bad_magnet)
    val badTorrent = stringResource(R.string.err_bad_torrent)
    val already = stringResource(R.string.err_already)

    fun explain(error: String?) {
        messages(
            when (error) {
                null -> added
                "not a link" -> badLink
                "bad magnet" -> badMagnet
                "bad torrent" -> badTorrent
                "already added" -> already
                else -> error
            },
        )
    }

    val pick = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri: Uri? ->
        if (uri != null) {
            val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
            explain(if (bytes == null) "bad torrent" else app.downloads.addTorrentBytes(bytes))
        }
    }

    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
        Spacer(Modifier.size(16.dp))
        Text(stringResource(R.string.downloads_title), fontSize = 26.sp, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.size(10.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = text, onValueChange = { text = it }, modifier = Modifier.weight(1f), singleLine = true,
                placeholder = { Text(stringResource(R.string.downloads_hint), color = Muted, fontSize = 14.sp) },
            )
            IconButton(onClick = { text = clipboardText(context) }) { Icon(Icons.Filled.ContentPaste, stringResource(R.string.downloads_paste)) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 8.dp)) {
            Button(onClick = {
                val value = text.trim()
                if (value.isNotEmpty()) scope.launch { explain(app.downloads.addText(value)); text = "" }
            }) { Text(stringResource(R.string.downloads_add)) }
            TextButton(onClick = { pick.launch(arrayOf("application/x-bittorrent", "application/octet-stream", "*/*")) }) {
                Icon(Icons.Filled.FileOpen, null)
                Spacer(Modifier.size(6.dp))
                Text(stringResource(R.string.downloads_torrent_file))
            }
        }
        Text(stringResource(R.string.downloads_saved_to, app.downloads.downloadDirectory().path), color = Muted, fontSize = 12.sp, modifier = Modifier.padding(vertical = 8.dp))

        if (items.isEmpty()) {
            Text(stringResource(R.string.downloads_empty), color = Muted, modifier = Modifier.padding(top = 24.dp))
        }
        LazyColumn(verticalArrangement = Arrangement.spacedBy(10.dp)) {
            items(items.asReversed(), key = { it.id }) { item ->
                DownloadCard(
                    item,
                    onPause = { app.downloads.pause(item.id) },
                    onResume = { app.downloads.resume(item.id) },
                    onRemove = { removing = item },
                    onFiles = { choosing = item },
                )
            }
            item { Spacer(Modifier.size(16.dp)) }
        }
    }

    removing?.let { item ->
        AlertDialog(
            onDismissRequest = { removing = null },
            title = { Text(item.title, maxLines = 2, overflow = TextOverflow.Ellipsis) },
            text = {
                Column {
                    TextButton(onClick = { app.downloads.remove(item.id, false); removing = null }) { Text(stringResource(R.string.action_remove)) }
                    TextButton(onClick = { app.downloads.remove(item.id, true); removing = null }) { Text(stringResource(R.string.action_remove_files), color = Bad) }
                }
            },
            confirmButton = { TextButton(onClick = { removing = null }) { Text(stringResource(R.string.action_cancel)) } },
        )
    }
    choosing?.let { item -> FilesDialog(item, onDone = { app.downloads.selectFiles(item.id, it); choosing = null }, onCancel = { choosing = null }) }
}

private fun clipboardText(context: Context): String =
    (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).primaryClip?.getItemAt(0)?.text?.toString().orEmpty()

@Composable
private fun FilesDialog(item: DownloadItem, onDone: (Set<Int>?) -> Unit, onCancel: () -> Unit) {
    var selected by remember { mutableStateOf(item.selectedFiles ?: item.files.map { it.index }.toSet()) }
    AlertDialog(
        onDismissRequest = onCancel,
        title = { Text(stringResource(R.string.files_title)) },
        text = {
            LazyColumn {
                items(item.files, key = { it.index }) { f ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(checked = f.index in selected, onCheckedChange = { on -> selected = if (on) selected + f.index else selected - f.index })
                        Column(Modifier.weight(1f)) {
                            Text(f.path.substringAfter('/'), fontSize = 13.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
                            Text(Format.size(f.length), color = Muted, fontSize = 12.sp)
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(enabled = selected.isNotEmpty(), onClick = { onDone(if (selected.size == item.files.size) null else selected) }) { Text(stringResource(R.string.action_ok)) }
        },
        dismissButton = { TextButton(onClick = onCancel) { Text(stringResource(R.string.action_cancel)) } },
    )
}

@Composable
private fun DownloadCard(item: DownloadItem, onPause: () -> Unit, onResume: () -> Unit, onRemove: () -> Unit, onFiles: () -> Unit) {
    InfoCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(item.title, fontSize = 16.sp, fontWeight = FontWeight.Medium, maxLines = 2, overflow = TextOverflow.Ellipsis)
                Text(stateText(item), color = if (item.state == ItemState.FAILED) Bad else Muted, fontSize = 13.sp)
            }
            if (item.kind == ItemKind.TORRENT && item.files.size > 1 && item.state != ItemState.DONE) {
                IconButton(onClick = onFiles) { Icon(Icons.Filled.FolderOpen, stringResource(R.string.action_files)) }
            }
            when (item.state) {
                ItemState.RUNNING, ItemState.QUEUED -> IconButton(onClick = onPause) { Icon(Icons.Filled.Pause, stringResource(R.string.action_pause)) }
                ItemState.PAUSED, ItemState.FAILED -> IconButton(onClick = onResume) { Icon(Icons.Filled.PlayArrow, stringResource(R.string.action_resume)) }
                ItemState.DONE -> if (item.seeding) IconButton(onClick = onPause) { Icon(Icons.Filled.Pause, stringResource(R.string.action_pause)) }
            }
            IconButton(onClick = onRemove) { Icon(Icons.Filled.Close, stringResource(R.string.action_remove)) }
        }
        Spacer(Modifier.size(8.dp))
        SplitBar(item)
        Spacer(Modifier.size(6.dp))
        Row(horizontalArrangement = Arrangement.SpaceBetween, modifier = Modifier.fillMaxWidth()) {
            Text("${Format.size(item.doneBytes)} / ${Format.size(item.totalBytes)}  (${Format.percent(item.doneBytes, item.totalBytes)}%)", fontSize = 12.sp, color = Muted)
            if (item.state == ItemState.RUNNING) Text(Format.speed(item.speed), fontSize = 12.sp)
        }
        if (item.kind == ItemKind.TORRENT && item.state == ItemState.RUNNING) Text(stringResource(R.string.peers, item.peers), fontSize = 12.sp, color = Muted)
        if (item.error != null && item.state == ItemState.FAILED) Text(item.error, color = Bad, fontSize = 12.sp)
    }
}

@Composable
private fun stateText(item: DownloadItem): String = when {
    item.state == ItemState.RUNNING && item.phase == Phase.METADATA -> stringResource(R.string.state_metadata)
    item.state == ItemState.RUNNING && item.phase == Phase.CHECKING -> stringResource(R.string.state_checking)
    item.state == ItemState.RUNNING -> stringResource(R.string.state_running)
    item.state == ItemState.QUEUED -> stringResource(R.string.state_queued)
    item.state == ItemState.PAUSED -> stringResource(R.string.state_paused)
    item.state == ItemState.FAILED -> stringResource(R.string.state_failed)
    item.seeding -> stringResource(R.string.state_seeding, Format.size(item.uploaded))
    else -> stringResource(R.string.state_done)
}

/** The progress bar is coloured by how much each link has carried, so the split between Wi-Fi and the SIMs is visible. */
@Composable
private fun SplitBar(item: DownloadItem) {
    val total = item.totalBytes.coerceAtLeast(1)
    Row(Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)).background(Panel2)) {
        val carried = item.perLink.values.sum().coerceAtLeast(1)
        val doneFraction = (item.doneBytes.toFloat() / total).coerceIn(0f, 1f)
        if (doneFraction > 0f) {
            Row(Modifier.weight(doneFraction).fillMaxHeight()) {
                if (item.perLink.isEmpty()) Box(Modifier.weight(1f).fillMaxHeight().background(Accent))
                for ((id, bytes) in item.perLink) if (bytes > 0) Box(Modifier.weight(bytes.toFloat() / carried).fillMaxHeight().background(linkColor(id)))
            }
        }
        if (doneFraction < 1f) Box(Modifier.weight(1f - doneFraction).fillMaxHeight())
    }
}
