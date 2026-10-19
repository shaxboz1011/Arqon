package app.arqon.core.http

import app.arqon.core.net.Link
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.isActive
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import java.io.File
import java.io.IOException
import java.io.RandomAccessFile
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong

class DownloadException(message: String, cause: Throwable? = null) : IOException(message, cause)

/** What a download reports while it runs: bytes of finished chunks per link always add up to [done]. */
data class DownloadProgress(val total: Long, val done: Long, val perLink: Map<String, Long>)

/**
 * Downloads one file over every link at once. The file is cut into chunks fetched with HTTP range requests; each link has a few
 * workers that take the next chunk, so a fast link simply takes more of them and a dead link only costs the chunk it was holding.
 * A file the server cannot cut into ranges is fetched in one piece over the first working link.
 */
class RangeDownloader(
    private val client: OkHttpClient = OkHttpClient(),
    private val chunkSize: Long = 4L * 1024 * 1024,
    private val connectionsPerLink: Int = 3,
    private val benchMillis: Long = 15_000,
) {
    /**
     * @param links the links to use; asked again whenever a worker needs one, so a network that appears later is used too
     * @param headers extra request headers (cookies, user agent, referer of a download started in the browser)
     */
    suspend fun download(
        url: String,
        destination: File,
        links: () -> List<Link>,
        headers: Map<String, String> = emptyMap(),
        onProgress: (DownloadProgress) -> Unit = {},
    ): File {
        destination.absoluteFile.parentFile?.mkdirs()
        val partial = File(destination.path + ".part")
        val stateFile = File(destination.path + ".state")

        val probe = probe(url, links, headers)
        if (probe.total <= 0 || !probe.ranges) {
            return singleStream(probe.finalUrl, destination, partial, links, headers, onProgress)
        }

        val total = probe.total
        val chunkCount = ((total + chunkSize - 1) / chunkSize).toInt()
        val done = BooleanArray(chunkCount)
        val resumed = loadState(stateFile, total, chunkSize, done)
        if (!partial.exists() || partial.length() != total) {
            done.fill(false)   // the data is gone, so the notes about it are worthless
            RandomAccessFile(partial, "rw").use { it.setLength(total) }
        }
        val state = Chunks(done, total, chunkSize)
        val perLink = ConcurrentHashMap<String, AtomicLong>()
        val doneBytes = AtomicLong(state.doneBytes())
        if (resumed) onProgress(DownloadProgress(total, doneBytes.get(), emptyMap()))

        val file = RandomAccessFile(partial, "rw")
        val benchedUntil = ConcurrentHashMap<String, Long>()
        try {
            coroutineScope {
                val workers = ArrayList<kotlinx.coroutines.Deferred<Unit>>()
                val seen = HashSet<String>()
                // Links are looked at again every second, so a SIM or Wi-Fi that shows up mid-download joins in.
                val supervisor = async {
                    while (isActive && !state.finished()) {
                        for (link in links()) {
                            if (!seen.add(link.id)) continue
                            repeat(connectionsPerLink) {
                                workers += async(Dispatchers.IO) {
                                    worker(probe.finalUrl, link, state, file, headers, benchedUntil, perLink, doneBytes) {
                                        onProgress(DownloadProgress(total, doneBytes.get(), perLink.mapValues { it.value.get() }))
                                    }
                                }
                            }
                        }
                        delay(1000)
                    }
                }
                supervisor.await()
                workers.awaitAll()
            }
        } finally {
            file.close()
            saveState(stateFile, total, chunkSize, state.snapshot())
        }
        if (!state.finished()) throw DownloadException("the download was not finished")
        if (destination.exists()) destination.delete()
        if (!partial.renameTo(destination)) throw DownloadException("could not move the finished file into place")
        stateFile.delete()
        return destination
    }

    private class Probe(val finalUrl: String, val total: Long, val ranges: Boolean)

    private suspend fun probe(url: String, links: () -> List<Link>, headers: Map<String, String>): Probe {
        var lastError: Throwable? = null
        repeat(3) { round ->
            for (link in links().ifEmpty { listOf(null) }) {
                try {
                    val http = link?.httpClient(client) ?: client
                    val request = Request.Builder().url(url).header("Range", "bytes=0-0").apply { headers.forEach { (k, v) -> header(k, v) } }.build()
                    return execute(http, request) { response ->
                        val finalUrl = response.request.url.toString()
                        when (response.code) {
                            206 -> {
                                val total = response.header("Content-Range")?.substringAfter('/')?.toLongOrNull() ?: -1
                                Probe(finalUrl, total, total > 0)
                            }
                            200 -> Probe(finalUrl, response.header("Content-Length")?.toLongOrNull() ?: -1, false)
                            429, 503 -> throw DownloadException("the server asks to slow down (${response.code})")
                            else -> throw DownloadException("the server answered ${response.code}")
                        }
                    }
                } catch (e: CancellationException) {
                    throw e
                } catch (e: IOException) {
                    lastError = e
                }
            }
            delay(300L * (round + 1))
        }
        throw DownloadException("could not reach the server", lastError)
    }

    private suspend fun singleStream(
        url: String,
        destination: File,
        partial: File,
        links: () -> List<Link>,
        headers: Map<String, String>,
        onProgress: (DownloadProgress) -> Unit,
    ): File {
        var lastError: Throwable? = null
        for (link in links().ifEmpty { listOf(null) }) {
            try {
                val http = link?.httpClient(client) ?: client
                val request = Request.Builder().url(url).apply { headers.forEach { (k, v) -> header(k, v) } }.build()
                execute(http, request) { response ->
                    if (!response.isSuccessful) throw DownloadException("the server answered ${response.code}")
                    val total = response.header("Content-Length")?.toLongOrNull() ?: -1
                    var read = 0L
                    partial.outputStream().use { out ->
                        val buffer = ByteArray(64 * 1024)
                        val body = (response.body ?: throw DownloadException("the server sent no data")).source()
                        while (true) {
                            val n = body.read(buffer)
                            if (n < 0) break
                            out.write(buffer, 0, n)
                            read += n
                            onProgress(DownloadProgress(total, read, mapOf((link?.id ?: "default") to read)))
                        }
                    }
                }
                if (destination.exists()) destination.delete()
                if (!partial.renameTo(destination)) throw DownloadException("could not move the finished file into place")
                return destination
            } catch (e: CancellationException) {
                throw e
            } catch (e: IOException) {
                lastError = e
            }
        }
        throw DownloadException("could not download the file", lastError)
    }

    private suspend fun worker(
        url: String,
        link: Link,
        state: Chunks,
        file: RandomAccessFile,
        headers: Map<String, String>,
        benchedUntil: ConcurrentHashMap<String, Long>,
        perLink: ConcurrentHashMap<String, AtomicLong>,
        doneBytes: AtomicLong,
        report: () -> Unit,
    ) {
        val http = link.httpClient(client)
        var failures = 0
        while (currentCoroutineContext().isActive) {
            val wait = (benchedUntil[link.id] ?: 0) - System.currentTimeMillis()
            if (wait > 0) {
                delay(minOf(wait, 500))
                if (state.finished()) return
                continue
            }
            val index = state.take() ?: if (state.finished()) return else { delay(100); continue }
            val start = index * chunkSize
            val end = minOf(start + chunkSize, state.total) - 1
            try {
                val data = fetch(http, url, start, end, headers)
                synchronized(file) {
                    file.seek(start)
                    file.write(data)
                }
                state.markDone(index)
                doneBytes.addAndGet(data.size.toLong())
                perLink.getOrPut(link.id) { AtomicLong() }.addAndGet(data.size.toLong())
                failures = 0
                report()
            } catch (e: CancellationException) {
                state.giveBack(index)
                throw e
            } catch (e: Exception) {
                state.giveBack(index)
                failures++
                if (e is RateLimited) {
                    benchedUntil[link.id] = System.currentTimeMillis() + e.waitMillis
                } else if (failures >= 3) {
                    benchedUntil[link.id] = System.currentTimeMillis() + benchMillis   // this link is struggling: let the others work
                    failures = 0
                }
                delay(200)
            }
        }
    }

    private class RateLimited(val waitMillis: Long) : IOException("the server asks to slow down")

    private suspend fun fetch(http: OkHttpClient, url: String, start: Long, end: Long, headers: Map<String, String>): ByteArray {
        val request = Request.Builder().url(url).header("Range", "bytes=$start-$end").apply { headers.forEach { (k, v) -> header(k, v) } }.build()
        return execute(http, request) { response ->
            when (response.code) {
                206 -> {
                    val bytes = (response.body ?: throw DownloadException("the server sent no data")).bytes()
                    if (bytes.size.toLong() != end - start + 1) throw DownloadException("the server sent a short chunk")
                    bytes
                }
                429, 503 -> throw RateLimited((response.header("Retry-After")?.toLongOrNull() ?: 5).coerceIn(1, 120) * 1000)
                else -> throw DownloadException("the server answered ${response.code} to a range request")
            }
        }
    }

    /** Runs a request on the IO threads; cancelling the coroutine cancels the call too, so a stuck read does not hang. */
    private suspend fun <T> execute(http: OkHttpClient, request: Request, handle: (Response) -> T): T {
        val call = http.newCall(request)
        return coroutineScope {
            val watcher = launch { try { awaitCancellation() } finally { call.cancel() } }
            try {
                withContext(Dispatchers.IO) { call.execute().use(handle) }
            } finally {
                watcher.cancel()
            }
        }
    }

    /** Which chunks are finished or being fetched. */
    internal class Chunks(private val done: BooleanArray, val total: Long, private val chunkSize: Long) {
        private val taken = BooleanArray(done.size)

        @Synchronized
        fun take(): Int? {
            for (i in done.indices) if (!done[i] && !taken[i]) { taken[i] = true; return i }
            return null
        }

        @Synchronized
        fun giveBack(i: Int) { taken[i] = false }

        @Synchronized
        fun markDone(i: Int) { done[i] = true }

        @Synchronized
        fun finished() = done.all { it }

        @Synchronized
        fun snapshot() = done.copyOf()

        @Synchronized
        fun doneBytes(): Long {
            var sum = 0L
            for (i in done.indices) if (done[i]) sum += minOf(chunkSize, total - i * chunkSize)
            return sum
        }
    }

    private fun loadState(file: File, total: Long, chunkSize: Long, into: BooleanArray): Boolean {
        if (!file.exists()) return false
        return try {
            val lines = file.readLines()
            if (lines.firstOrNull() != "$total:$chunkSize") return false
            for (line in lines.drop(1)) line.toIntOrNull()?.takeIf { it in into.indices }?.let { into[it] = true }
            true
        } catch (_: IOException) {
            false
        }
    }

    private fun saveState(file: File, total: Long, chunkSize: Long, done: BooleanArray) {
        try {
            file.writeText(buildString {
                append("$total:$chunkSize\n")
                done.forEachIndexed { i, d -> if (d) append(i).append('\n') }
            })
        } catch (_: IOException) {
            // the notes only speed up a later resume
        }
    }
}
