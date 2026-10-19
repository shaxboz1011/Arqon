package app.arqon.core.torrent

import app.arqon.core.net.Link
import app.arqon.core.net.LinkKind
import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import java.io.File
import java.net.InetSocketAddress
import java.nio.file.Files
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

class TorrentSessionTest {
    private val dir: File = Files.createTempDirectory("arqon-session").toFile()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    @AfterTest
    fun cleanup() {
        scope.cancel()
        dir.deleteRecursively()
    }

    private fun wifi() = Link("wifi", "Wi-Fi", LinkKind.WIFI)
    private fun sim(n: Int) = Link("sim$n", "SIM $n", LinkKind.CELLULAR)

    private fun session(
        source: TorrentSource,
        save: File = File(dir, "save"),
        links: () -> List<Link> = { listOf(wifi()) },
        selected: Set<Int>? = null,
        config: TorrentSession.Config = TorrentSession.Config(announceToPublicTrackers = false),
    ) = TorrentSession(source, save, links, scope, config, selected)

    private suspend fun TorrentSession.await(phase: Phase, seconds: Long = 30): TorrentStatus =
        withTimeout(seconds * 1000) { status.first { it.phase == phase || it.phase == Phase.ERROR } }

    @Test
    fun aTorrentFileIsDownloadedFromASeederIntact() = runBlocking {
        val torrent = TestTorrent.random("movie.bin" to 400_000)
        TestSeeder(torrent).use { seeder ->
            val s = session(TorrentSource.File(torrent.torrentBytes))
            s.start()
            s.addPeer("127.0.0.1", seeder.port)

            val done = s.await(Phase.COMPLETE)

            assertEquals(Phase.COMPLETE, done.phase, done.error)
            assertContentEquals(torrent.data, File(dir, "save/movie.bin").readBytes())
            assertEquals(torrent.data.size.toLong(), done.doneBytes)
            s.stop()
        }
    }

    @Test
    fun aMultiFileTorrentKeepsItsFolderStructure() = runBlocking {
        val torrent = TestTorrent.random("a.bin" to 60_000, "sub/b.bin" to 90_000, "c.bin" to 10_000)
        TestSeeder(torrent).use { seeder ->
            val s = session(TorrentSource.File(torrent.torrentBytes))
            s.start()
            s.addPeer("127.0.0.1", seeder.port)

            assertEquals(Phase.COMPLETE, s.await(Phase.COMPLETE).phase)

            for ((path, bytes) in torrent.files) assertContentEquals(bytes, File(dir, "save/pack/$path").readBytes(), path)
            s.stop()
        }
    }

    @Test
    fun onlyTheChosenFilesAreFetchedAndCounted() = runBlocking {
        val torrent = TestTorrent.random("a.bin" to 64 * 1024, "b.bin" to 64 * 1024, "c.bin" to 64 * 1024, pieceLength = 32 * 1024)
        TestSeeder(torrent).use { seeder ->
            val s = session(TorrentSource.File(torrent.torrentBytes), selected = setOf(0, 2))
            s.start()
            s.addPeer("127.0.0.1", seeder.port)

            val done = s.await(Phase.COMPLETE)

            assertEquals(Phase.COMPLETE, done.phase, done.error)
            assertEquals(128 * 1024L, done.totalBytes)
            assertContentEquals(torrent.files["a.bin"], File(dir, "save/pack/a.bin").readBytes())
            assertContentEquals(torrent.files["c.bin"], File(dir, "save/pack/c.bin").readBytes())
            assertFalse(File(dir, "save/pack/b.bin").exists(), "a file that was not chosen must not be created")
            assertEquals(4, seeder.blocksServed.get() / 2, "only the four pieces of the two chosen files are fetched (two blocks each)")
            s.stop()
        }
    }

    @Test
    fun aMagnetLinkFetchesItsMetadataFromPeersAndThenDownloads() = runBlocking {
        val torrent = TestTorrent.random("linux.iso" to 300_000)
        TestSeeder(torrent).use { seeder ->
            val magnet = Magnet.parse("magnet:?xt=urn:btih:${torrent.meta.infoHash.joinToString("") { "%02x".format(it) }}&dn=linux&x.pe=127.0.0.1:${seeder.port}")!!
            val s = session(TorrentSource.MagnetLink(magnet))
            s.start()

            val done = s.await(Phase.COMPLETE)

            assertEquals(Phase.COMPLETE, done.phase, done.error)
            assertEquals("linux.iso", done.name)
            assertContentEquals(torrent.data, File(dir, "save/linux.iso").readBytes())
            s.stop()
        }
    }

    @Test
    fun metadataThatDoesNotMatchTheInfoHashIsRefused() = runBlocking {
        val real = TestTorrent.random("real.bin" to 100_000, seed = 1)
        val fake = TestTorrent.random("fake.bin" to 100_000, seed = 2)
        // the seeder serves the fake torrent's metadata, but the magnet link names the real one
        TestSeeder(fake).use { liar ->
            val hash = real.meta.infoHash.joinToString("") { "%02x".format(it) }
            val session = session(TorrentSource.MagnetLink(Magnet.parse("magnet:?xt=urn:btih:$hash&x.pe=127.0.0.1:${liar.port}")!!))
            session.start()

            delay(1500)

            assertEquals(Phase.METADATA, session.status.value.phase, "wrong metadata must never be accepted")
            session.stop()
        }
    }

    @Test
    fun aPieceThatFailsItsHashCheckIsFetchedAgainFromAnotherPeer() = runBlocking {
        val torrent = TestTorrent.random("data.bin" to 200_000)
        TestSeeder(torrent, corruptPieces = setOf(3)).use { bad ->
            TestSeeder(torrent).use { good ->
                val s = session(TorrentSource.File(torrent.torrentBytes))
                s.start()
                s.addPeer("127.0.0.1", bad.port)
                delay(300)
                s.addPeer("127.0.0.1", good.port)

                val done = s.await(Phase.COMPLETE)

                assertEquals(Phase.COMPLETE, done.phase, done.error)
                assertContentEquals(torrent.data, File(dir, "save/data.bin").readBytes())
                s.stop()
            }
        }
    }

    @Test
    fun filesAlreadyOnDiskAreRecognisedAndNothingIsDownloaded() = runBlocking {
        val torrent = TestTorrent.random("old.bin" to 150_000)
        torrent.writeTo(File(dir, "save"))
        val s = session(TorrentSource.File(torrent.torrentBytes))
        s.start()

        val done = s.await(Phase.COMPLETE)

        assertEquals(Phase.COMPLETE, done.phase, done.error)
        assertEquals(0L, done.perLink.values.sum(), "no peers were contacted for data")
        s.stop()
    }

    @Test
    fun connectionsAreSpreadOverEveryLinkAndEachLinkCarriesData() = runBlocking {
        val torrent = TestTorrent.random("big.bin" to 1_500_000)
        TestSeeder(torrent, blockDelayMillis = 2).use { a ->
            TestSeeder(torrent, blockDelayMillis = 2).use { b ->
                val s = session(TorrentSource.File(torrent.torrentBytes), links = { listOf(wifi(), sim(1)) })
                s.start()
                s.addPeer("127.0.0.1", a.port)
                s.addPeer("127.0.0.1", b.port)

                val done = s.await(Phase.COMPLETE)

                assertEquals(Phase.COMPLETE, done.phase, done.error)
                assertEquals(setOf("wifi", "sim1"), done.perLink.keys)
                assertTrue(done.perLink.values.all { it > 0 })
                assertEquals(torrent.data.size.toLong(), done.perLink.values.sum())
                s.stop()
            }
        }
    }

    @Test
    fun peersAreFoundThroughAnHttpTracker() = runBlocking {
        val torrent = TestTorrent.random("tracked.bin" to 120_000)
        TestSeeder(torrent).use { seeder ->
            val tracker = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
            tracker.createContext("/announce") { ex ->
                val port = seeder.port
                val body = Bencode.encode(mapOf("interval" to 60L, "peers" to byteArrayOf(127, 0, 0, 1, (port shr 8).toByte(), port.toByte())))
                ex.sendResponseHeaders(200, body.size.toLong())
                ex.responseBody.use { it.write(body) }
            }
            tracker.start()
            try {
                // the tracker address is not part of the info hash, so the same seeder serves the torrent that names the tracker
                val named = TestTorrent(torrent.files, trackers = listOf("http://127.0.0.1:${tracker.address.port}/announce"))
                val s = session(TorrentSource.File(named.torrentBytes))
                s.start()

                val done = s.await(Phase.COMPLETE)

                assertEquals(Phase.COMPLETE, done.phase, done.error)
                assertContentEquals(torrent.data, File(dir, "save/tracked.bin").readBytes())
                s.stop()
            } finally {
                tracker.stop(0)
            }
        }
    }

    @Test
    fun whatWeHaveIsServedToAPeerThatAsksForIt() = runBlocking {
        val torrent = TestTorrent.random("share.bin" to 100_000)
        torrent.writeTo(File(dir, "save"))
        TestLeecher(torrent, pieceToAsk = 2).use { leecher ->
            val s = session(TorrentSource.File(torrent.torrentBytes), config = TorrentSession.Config(announceToPublicTrackers = false, seedAfterComplete = true))
            s.start()
            s.addPeer("127.0.0.1", leecher.port)

            withTimeout(15_000) { while (!leecher.finished) delay(50) }

            assertContentEquals(torrent.piece(2).copyOf(PeerWire.BLOCK_SIZE.coerceAtMost(torrent.meta.pieceSize(2))), leecher.received)
            assertTrue(s.status.value.uploaded > 0)
            s.stop()
        }
    }

    @Test
    fun stoppingASessionCloseseItsConnections() = runBlocking {
        val torrent = TestTorrent.random("stop.bin" to 800_000)
        TestSeeder(torrent, blockDelayMillis = 20).use { seeder ->
            val s = session(TorrentSource.File(torrent.torrentBytes))
            s.start()
            s.addPeer("127.0.0.1", seeder.port)
            withTimeout(10_000) { while (seeder.blocksServed.get() < 3) delay(20) }

            s.stop()
            delay(300)
            val servedAfterStop = seeder.blocksServed.get()
            delay(500)

            assertEquals(servedAfterStop, seeder.blocksServed.get(), "a stopped session must not keep downloading")
            assertEquals(Phase.STOPPED, s.status.value.phase)
            assertNotNull(s.status.value)
        }
    }
}
