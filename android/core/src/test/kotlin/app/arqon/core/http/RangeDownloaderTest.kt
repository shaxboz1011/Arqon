package app.arqon.core.http

import app.arqon.core.net.Link
import app.arqon.core.net.LinkKind
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import java.io.File
import java.io.IOException
import java.net.InetAddress
import java.net.Socket
import java.nio.file.Files
import java.security.MessageDigest
import javax.net.SocketFactory
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class RangeDownloaderTest {
    private val dir: File = Files.createTempDirectory("arqon-dl").toFile()

    @AfterTest
    fun cleanup() {
        dir.deleteRecursively()
    }

    private fun link(id: String) = Link(id, id, LinkKind.WIFI)

    /** A link whose network is gone: every connection fails at once. */
    private fun deadLink(id: String) = Link(
        id, id, LinkKind.CELLULAR,
        socketFactory = object : SocketFactory() {
            override fun createSocket(): Socket = throw IOException("no network")
            override fun createSocket(host: String?, port: Int): Socket = throw IOException("no network")
            override fun createSocket(host: String?, port: Int, localHost: InetAddress?, localPort: Int): Socket = throw IOException("no network")
            override fun createSocket(host: InetAddress?, port: Int): Socket = throw IOException("no network")
            override fun createSocket(address: InetAddress?, port: Int, localAddress: InetAddress?, localPort: Int): Socket = throw IOException("no network")
        },
    )

    private fun sha(bytes: ByteArray) = MessageDigest.getInstance("SHA-256").digest(bytes)

    @Test
    fun aFileComesOverOneLinkIntact() = runBlocking {
        val payload = TestRangeServer.randomPayload(3_000_000)
        TestRangeServer(payload).use { server ->
            val out = RangeDownloader(chunkSize = 512 * 1024).download(server.url, File(dir, "a.bin"), { listOf(link("wifi")) })

            assertContentEquals(sha(payload), sha(out.readBytes()))
            assertFalse(File(dir, "a.bin.part").exists())
        }
    }

    @Test
    fun everyLinkCarriesChunksAndTheBytesAddUpToTheFile() = runBlocking {
        val payload = TestRangeServer.randomPayload(8_000_000)
        TestRangeServer(payload, delayMillis = 30).use { server ->
            var last: DownloadProgress? = null

            val out = RangeDownloader(chunkSize = 256 * 1024).download(
                server.url, File(dir, "b.bin"), { listOf(link("wifi"), link("sim1"), link("sim2")) },
            ) { last = it }

            assertContentEquals(sha(payload), sha(out.readBytes()))
            val perLink = last!!.perLink
            assertEquals(setOf("wifi", "sim1", "sim2"), perLink.keys)
            assertTrue(perLink.values.all { it > 0 })
            assertEquals(payload.size.toLong(), perLink.values.sum())
        }
    }

    @Test
    fun aDeadLinkDoesNotStopTheOthers() = runBlocking {
        val payload = TestRangeServer.randomPayload(4_000_000)
        TestRangeServer(payload).use { server ->
            val out = RangeDownloader(chunkSize = 256 * 1024, benchMillis = 200).download(
                server.url, File(dir, "c.bin"), { listOf(deadLink("sim2"), link("wifi")) },
            )

            assertContentEquals(sha(payload), sha(out.readBytes()))
        }
    }

    @Test
    fun aServerWithoutRangesStillGivesTheWholeFile() = runBlocking {
        val payload = TestRangeServer.randomPayload(1_500_000)
        TestRangeServer(payload, ranges = false).use { server ->
            val out = RangeDownloader(chunkSize = 256 * 1024).download(server.url, File(dir, "d.bin"), { listOf(link("wifi")) })

            assertContentEquals(sha(payload), sha(out.readBytes()))
        }
    }

    @Test
    fun aRateLimitedChunkIsRetriedAfterTheWait() = runBlocking {
        val payload = TestRangeServer.randomPayload(1_000_000)
        // request 1 is the probe; requests 2 and 3 (the first chunks) are refused with 429 and Retry-After: 1
        TestRangeServer(payload, refuse = 2..3).use { server ->
            val out = RangeDownloader(chunkSize = 256 * 1024).download(server.url, File(dir, "e.bin"), { listOf(link("wifi")) })

            assertContentEquals(sha(payload), sha(out.readBytes()))
            assertTrue(server.requests.get() > 5, "the refused chunks must have been requested again")
        }
    }

    @Test
    fun aProbeThatIsRefusedAtFirstIsRetried() = runBlocking {
        val payload = TestRangeServer.randomPayload(500_000)
        TestRangeServer(payload, refuse = 1..2).use { server ->
            val out = RangeDownloader(chunkSize = 256 * 1024).download(server.url, File(dir, "h.bin"), { listOf(link("wifi")) })

            assertContentEquals(sha(payload), sha(out.readBytes()))
        }
    }

    @Test
    fun aCancelledDownloadResumesAndDoesNotFetchTheFinishedChunksAgain() = runBlocking {
        val payload = TestRangeServer.randomPayload(6_000_000)
        TestRangeServer(payload, delayMillis = 60).use { server ->
            val destination = File(dir, "f.bin")
            val half = CompletableDeferred<Unit>()
            val job = launch(Dispatchers.Default) {
                try {
                    RangeDownloader(chunkSize = 256 * 1024, connectionsPerLink = 2).download(server.url, destination, { listOf(link("wifi")) }) {
                        if (it.done > payload.size / 3) half.complete(Unit)
                    }
                } catch (_: kotlinx.coroutines.CancellationException) {
                    // expected
                }
            }
            half.await()
            job.cancelAndJoin()
            val servedBefore = server.bytesServed.get()
            assertFalse(destination.exists())

            val out = RangeDownloader(chunkSize = 256 * 1024).download(server.url, destination, { listOf(link("wifi")) })

            assertContentEquals(sha(payload), sha(out.readBytes()))
            val second = server.bytesServed.get() - servedBefore
            assertTrue(second < payload.size, "the second run fetched $second bytes of ${payload.size}: it should skip what was finished")
        }
    }

    @Test
    fun anUnreachableServerIsReportedAsAnError() {
        val error = assertFailsWith<DownloadException> {
            runBlocking {
                RangeDownloader().download("http://127.0.0.1:1/nothing", File(dir, "g.bin"), { listOf(link("wifi")) })
            }
        }
        assertTrue(error.message!!.contains("server"))
    }
}
