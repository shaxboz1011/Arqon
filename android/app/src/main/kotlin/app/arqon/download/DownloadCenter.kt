package app.arqon.download

import android.content.Context
import android.os.Build
import android.os.Environment
import app.arqon.AppSettings
import app.arqon.core.http.DownloadProgress
import app.arqon.core.http.RangeDownloader
import app.arqon.core.input.InputKind
import app.arqon.core.input.UserInput
import app.arqon.core.net.Link
import app.arqon.core.net.LinkKind
import app.arqon.core.torrent.Magnet
import app.arqon.core.torrent.MetaInfo
import app.arqon.core.torrent.Phase
import app.arqon.core.torrent.TorrentFileEntry
import app.arqon.core.torrent.TorrentSession
import app.arqon.core.torrent.TorrentSource
import app.arqon.net.AndroidLinkManager
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

enum class ItemKind { HTTP, TORRENT }

enum class ItemState { QUEUED, RUNNING, PAUSED, DONE, FAILED }

data class DownloadItem(
    val id: String,
    val kind: ItemKind,
    val title: String,
    /** A web link, a magnet link, or the path of the saved .torrent file. */
    val source: String,
    val headers: Map<String, String> = emptyMap(),
    val directory: String,
    val state: ItemState = ItemState.QUEUED,
    val doneBytes: Long = 0,
    val totalBytes: Long = 0,
    val speed: Long = 0,
    val perLink: Map<String, Long> = emptyMap(),
    val peers: Int = 0,
    val uploaded: Long = 0,
    val seeding: Boolean = false,
    val phase: Phase? = null,
    val error: String? = null,
    val files: List<TorrentFileEntry> = emptyList(),
    val selectedFiles: Set<Int>? = null,
)

data class Summary(val running: Int, val speed: Long)

/**
 * Keeps the list of downloads and runs them: plain files with [RangeDownloader], torrents with [TorrentSession]. Both use the
 * links that [AndroidLinkManager] currently offers, so every download spreads over Wi-Fi and the SIM cards.
 */
class DownloadCenter(
    private val context: Context,
    private val links: AndroidLinkManager,
    private val settings: AppSettings,
    private val scope: CoroutineScope,
    private val onActivity: (Summary) -> Unit = {},
) {
    private val http = OkHttpClient()
    private val file = File(context.filesDir, "downloads.json")
    private val torrentsDir = File(context.filesDir, "torrents").also { it.mkdirs() }

    private val _items = MutableStateFlow<List<DownloadItem>>(emptyList())
    val items: StateFlow<List<DownloadItem>> = _items

    private val _summary = MutableStateFlow(Summary(0, 0))
    val summary: StateFlow<Summary> = _summary

    private val _linkSpeeds = MutableStateFlow<Map<String, Long>>(emptyMap())

    /** Bytes per second received over each link right now (all downloads together). */
    val linkSpeeds: StateFlow<Map<String, Long>> = _linkSpeeds

    private val jobs = HashMap<String, Job>()
    private val sessions = HashMap<String, TorrentSession>()
    private var lastPerLink = HashMap<String, Long>()
    private var lastDone = HashMap<String, Long>()

    fun start() {
        _items.value = load().map { if (it.state == ItemState.RUNNING) it.copy(state = ItemState.QUEUED, speed = 0) else it.copy(speed = 0) }
        scope.launch {
            while (isActive) {
                delay(1000)
                tick()
            }
        }
        schedule()
    }

    // ---- adding ----------------------------------------------------------------------------------------------------

    /** Adds whatever the user pasted or the browser handed over. Returns an error text for the screen, or null when it was accepted. */
    suspend fun addText(text: String, headers: Map<String, String> = emptyMap()): String? = when (UserInput.classify(text)) {
        InputKind.MAGNET -> addMagnet(text)
        InputKind.TORRENT_URL -> addTorrentFromUrl(text.trim(), headers)
        InputKind.HTTP_URL -> { addHttp(text.trim(), headers, null); null }
        InputKind.OTHER -> "not a link"
    }

    fun addHttp(url: String, headers: Map<String, String>, fileName: String?) {
        val name = fileName?.takeIf { it.isNotBlank() } ?: UserInput.fileNameFromUrl(url)
        add(DownloadItem(UUID.randomUUID().toString(), ItemKind.HTTP, name, url, headers, downloadDirectory().path))
    }

    fun addMagnet(text: String): String? {
        val magnet = Magnet.parse(text) ?: return "bad magnet"
        if (_items.value.any { it.kind == ItemKind.TORRENT && it.id == magnet.infoHashHex }) return "already added"
        add(DownloadItem(magnet.infoHashHex, ItemKind.TORRENT, magnet.name ?: magnet.infoHashHex, text.trim(), emptyMap(), downloadDirectory().path))
        return null
    }

    fun addTorrentBytes(bytes: ByteArray): String? {
        val meta = try { MetaInfo.parse(bytes) } catch (e: Exception) { return "bad torrent" }
        val hash = meta.infoHash.joinToString("") { "%02x".format(it) }
        if (_items.value.any { it.id == hash }) return "already added"
        val saved = File(torrentsDir, "$hash.torrent").also { it.writeBytes(bytes) }
        add(DownloadItem(hash, ItemKind.TORRENT, meta.name, saved.path, emptyMap(), downloadDirectory().path, files = meta.files))
        return null
    }

    private suspend fun addTorrentFromUrl(url: String, headers: Map<String, String>): String? = try {
        val bytes = withContext(Dispatchers.IO) {
            val client = (links.activeLinks().firstOrNull()?.httpClient(http)) ?: http
            val request = Request.Builder().url(url).apply { headers.forEach { (k, v) -> header(k, v) } }.build()
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) throw java.io.IOException("the server answered ${response.code}")
                response.body?.bytes() ?: throw java.io.IOException("nothing was sent")
            }
        }
        addTorrentBytes(bytes)
    } catch (e: Exception) {
        e.message ?: "could not download the .torrent file"
    }

    private fun add(item: DownloadItem) {
        app.arqon.stats.Features.use(if (item.kind == ItemKind.TORRENT) "download_torrent" else "download_http")
        _items.update { it + item }
        save()
        schedule()
    }

    // ---- control ---------------------------------------------------------------------------------------------------

    fun pause(id: String) {
        stopJob(id)
        update(id) { it.copy(state = ItemState.PAUSED, speed = 0, seeding = false) }
        schedule()
    }

    fun resume(id: String) {
        update(id) { it.copy(state = ItemState.QUEUED, error = null) }
        schedule()
    }

    fun remove(id: String, deleteFiles: Boolean) {
        val item = _items.value.firstOrNull { it.id == id } ?: return
        stopJob(id)
        _items.update { list -> list.filterNot { it.id == id } }
        File(torrentsDir, "$id.torrent").delete()
        if (deleteFiles) {
            scope.launch(Dispatchers.IO) {
                try {
                    when (item.kind) {
                        ItemKind.HTTP -> { File(item.directory, item.title).delete(); File(item.directory, item.title + ".part").delete(); File(item.directory, item.title + ".state").delete() }
                        ItemKind.TORRENT -> item.files.firstOrNull()?.path?.substringBefore('/')?.let { top ->
                            if (item.files.size > 1) File(item.directory, top).deleteRecursively() else File(item.directory, top).delete()
                        }
                    }
                } catch (_: Exception) {
                    // the files stay; the list entry is gone either way
                }
            }
        }
        save()
        schedule()
    }

    fun selectFiles(id: String, files: Set<Int>?) {
        update(id) { it.copy(selectedFiles = files) }
        sessions[id]?.selectFiles(files)
        save()
    }

    private fun stopJob(id: String) {
        jobs.remove(id)?.cancel()
        sessions.remove(id)?.let { session -> scope.launch { session.stop() } }
    }

    // ---- running ---------------------------------------------------------------------------------------------------

    private fun schedule() {
        val running = _items.value.count { it.state == ItemState.RUNNING }
        var room = settings.maxConcurrent.value - running
        for (item in _items.value) {
            if (room <= 0) break
            if (item.state == ItemState.QUEUED && item.id !in jobs) {
                room--
                launch(item)
            }
        }
        publishSummary()
    }

    private fun linksOrDefault(): List<Link> = links.activeLinks().ifEmpty { listOf(Link("default", "Default", LinkKind.OTHER)) }

    private fun launch(item: DownloadItem) {
        update(item.id) { it.copy(state = ItemState.RUNNING, error = null) }
        jobs[item.id] = scope.launch(Dispatchers.Default) {
            try {
                when (item.kind) {
                    ItemKind.HTTP -> runHttp(item)
                    ItemKind.TORRENT -> runTorrent(item)
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                update(item.id) { it.copy(state = ItemState.FAILED, error = e.message ?: e.toString(), speed = 0) }
            } finally {
                jobs.remove(item.id)
                save()
                schedule()
            }
        }
    }

    private suspend fun runHttp(item: DownloadItem) {
        val dir = File(item.directory).also { it.mkdirs() }
        RangeDownloader(http).download(item.source, File(dir, item.title), { linksOrDefault() }, item.headers) { p: DownloadProgress ->
            update(item.id) { it.copy(doneBytes = p.done, totalBytes = p.total, perLink = p.perLink) }
        }
        update(item.id) { it.copy(state = ItemState.DONE, speed = 0) }
    }

    private suspend fun runTorrent(item: DownloadItem) {
        val source = if (item.source.startsWith("magnet:", ignoreCase = true)) {
            TorrentSource.MagnetLink(Magnet.parse(item.source) ?: throw IllegalStateException("bad magnet"))
        } else {
            TorrentSource.File(File(item.source).readBytes())
        }
        val seed = settings.seedAfterComplete.value
        val session = TorrentSession(
            source, File(item.directory).also { it.mkdirs() }, { linksOrDefault() }, scope,
            TorrentSession.Config(seedAfterComplete = seed, httpClient = http), item.selectedFiles,
        )
        sessions[item.id] = session
        session.start()
        session.status.collect { s ->
            update(item.id) {
                it.copy(
                    title = s.name ?: it.title, doneBytes = s.doneBytes, totalBytes = s.totalBytes, speed = s.downloadBytesPerSecond,
                    perLink = s.perLink, peers = s.peers, uploaded = s.uploaded, phase = s.phase, files = s.files.ifEmpty { it.files },
                )
            }
            when (s.phase) {
                Phase.COMPLETE -> {
                    update(item.id) { it.copy(state = ItemState.DONE, seeding = seed) }
                    if (!seed) { sessions.remove(item.id); throw CancellationException("finished") }
                }
                Phase.ERROR -> throw IllegalStateException(s.error ?: "torrent error")
                else -> Unit
            }
        }
    }

    private fun update(id: String, change: (DownloadItem) -> DownloadItem) {
        _items.update { list -> list.map { if (it.id == id) change(it) else it } }
    }

    private fun tick() {
        // speed of plain downloads from the bytes that arrived since the last second
        val now = HashMap<String, Long>()
        for (item in _items.value) for ((link, bytes) in item.perLink) now[link] = (now[link] ?: 0) + bytes
        _linkSpeeds.value = now.mapValues { (k, v) -> maxOf(0, v - (lastPerLink[k] ?: v)) }
        lastPerLink = now
        val doneNow = HashMap<String, Long>()
        _items.update { list ->
            list.map {
                doneNow[it.id] = it.doneBytes
                if (it.kind == ItemKind.HTTP && it.state == ItemState.RUNNING) it.copy(speed = maxOf(0, it.doneBytes - (lastDone[it.id] ?: it.doneBytes))) else it
            }
        }
        lastDone = doneNow
        publishSummary()
    }

    private fun publishSummary() {
        val running = _items.value.count { it.state == ItemState.RUNNING || it.seeding }
        val summary = Summary(running, _items.value.sumOf { if (it.state == ItemState.RUNNING) it.speed else 0 })
        if (summary != _summary.value) {
            _summary.value = summary
            onActivity(summary)
        }
    }

    // ---- where files go and how the list is remembered ---------------------------------------------------------------

    fun downloadDirectory(): File {
        settings.downloadDir.value?.let { return File(it) }
        val publicAllowed = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) Environment.isExternalStorageManager() else
            context.checkSelfPermission(android.Manifest.permission.WRITE_EXTERNAL_STORAGE) == android.content.pm.PackageManager.PERMISSION_GRANTED
        return if (publicAllowed) File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "Arqon")
        else context.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS) ?: File(context.filesDir, "downloads")
    }

    private fun save() {
        try {
            val array = JSONArray()
            for (i in _items.value) {
                array.put(JSONObject().apply {
                    put("id", i.id); put("kind", i.kind.name); put("title", i.title); put("source", i.source); put("dir", i.directory)
                    put("state", i.state.name); put("done", i.doneBytes); put("total", i.totalBytes)
                    put("headers", JSONObject(i.headers))
                    i.selectedFiles?.let { put("selected", JSONArray(it.toList())) }
                })
            }
            file.writeText(array.toString())
        } catch (_: Exception) {
            // the list is a convenience
        }
    }

    private fun load(): List<DownloadItem> = try {
        if (!file.exists()) emptyList() else {
            val array = JSONArray(file.readText())
            (0 until array.length()).map { n ->
                val o = array.getJSONObject(n)
                val headers = o.optJSONObject("headers")?.let { h -> h.keys().asSequence().associateWith { k -> h.getString(k) } }.orEmpty()
                DownloadItem(
                    id = o.getString("id"), kind = ItemKind.valueOf(o.getString("kind")), title = o.getString("title"), source = o.getString("source"),
                    headers = headers, directory = o.getString("dir"), state = ItemState.valueOf(o.getString("state")),
                    doneBytes = o.optLong("done"), totalBytes = o.optLong("total"),
                    selectedFiles = o.optJSONArray("selected")?.let { a -> (0 until a.length()).map { a.getInt(it) }.toSet() },
                )
            }
        }
    } catch (_: Exception) {
        emptyList()
    }
}
