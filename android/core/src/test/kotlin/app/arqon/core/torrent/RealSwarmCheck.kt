package app.arqon.core.torrent

import app.arqon.core.net.Link
import app.arqon.core.net.LinkKind
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import okhttp3.OkHttpClient
import okhttp3.Request
import org.junit.jupiter.api.Assumptions.assumeTrue
import java.nio.file.Files
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * A manual check against a real swarm on the internet; it is skipped unless ARQON_REAL_TORRENT_URL names a .torrent file
 * (set ARQON_REAL_SECONDS to change how long it runs). It proves that the engine talks to real peers and trackers, which the
 * seeders of the other tests cannot prove.
 */
class RealSwarmCheck {
    @Test
    fun downloadsSomethingFromARealSwarm() = runBlocking {
        val url = System.getenv("ARQON_REAL_TORRENT_URL")
        assumeTrue(!url.isNullOrBlank(), "set ARQON_REAL_TORRENT_URL to run this check")
        val seconds = System.getenv("ARQON_REAL_SECONDS")?.toIntOrNull() ?: 40

        val bytes = OkHttpClient().newCall(Request.Builder().url(url!!).build()).execute().use { it.body!!.bytes() }
        val dir = Files.createTempDirectory("arqon-real").toFile()
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        try {
            val session = TorrentSession(TorrentSource.File(bytes), dir, { listOf(Link("wifi", "Wi-Fi", LinkKind.WIFI)) }, scope)
            session.start()
            val end = System.currentTimeMillis() + seconds * 1000L
            while (System.currentTimeMillis() < end) {
                delay(5000)
                val s = session.status.value
                println("REAL ${s.phase} peers=${s.peers} done=${s.doneBytes / 1024} KiB speed=${s.downloadBytesPerSecond / 1024} KiB/s links=${s.perLink}")
            }
            val last = session.status.value
            session.stop()
            assertTrue(last.doneBytes > 0, "no verified data arrived from the real swarm in $seconds s: $last")
        } finally {
            scope.cancel()
            dir.deleteRecursively()
        }
    }
}
