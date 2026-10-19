package app.arqon.core.torrent

import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.runBlocking
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.nio.ByteBuffer
import kotlin.concurrent.thread
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

class TrackerTest {
    private val infoHash = ByteArray(20) { (it * 7).toByte() }
    private val peerId = ByteArray(20) { (200 + it).toByte() }

    @Test
    fun anHttpTrackerAnswerWithCompactPeersIsRead() = runBlocking {
        var query = ""
        val server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/announce") { ex ->
            query = ex.requestURI.rawQuery
            val body = Bencode.encode(mapOf("interval" to 900L, "peers" to byteArrayOf(10, 1, 2, 3, 0x1A, 0xE1.toByte(), 127, 0, 0, 1, 0x1F, 0x90.toByte())))
            ex.sendResponseHeaders(200, body.size.toLong())
            ex.responseBody.use { it.write(body) }
        }
        server.start()
        try {
            val result = TrackerClient().announce("http://127.0.0.1:${server.address.port}/announce", null, infoHash, peerId, 6881, 0, 0, 1000, TrackerEvent.STARTED)

            assertEquals(listOf("10.1.2.3" to 6881, "127.0.0.1" to 8080), result.peers)
            assertEquals(900, result.intervalSeconds)
            assertTrue(query.contains("info_hash=" + infoHash.joinToString("") { "%%%02X".format(it) }), "the hash is sent as raw percent-encoded bytes: $query")
            assertTrue(query.contains("event=started") && query.contains("left=1000") && query.contains("compact=1"))
        } finally {
            server.stop(0)
        }
    }

    @Test
    fun aTrackerThatRefusesIsReportedWithItsReason() {
        val server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/announce") { ex ->
            val body = Bencode.encode(mapOf("failure reason" to "unregistered torrent"))
            ex.sendResponseHeaders(200, body.size.toLong())
            ex.responseBody.use { it.write(body) }
        }
        server.start()
        try {
            val error = assertFailsWith<TrackerException> {
                runBlocking { TrackerClient().announce("http://127.0.0.1:${server.address.port}/announce", null, infoHash, peerId, 6881, 0, 0, 0, TrackerEvent.NONE) }
            }
            assertTrue(error.message!!.contains("unregistered torrent"))
        } finally {
            server.stop(0)
        }
    }

    @Test
    fun aUdpTrackerIsAskedInTwoStepsAndItsPeersAreRead() = runBlocking {
        val socket = DatagramSocket(0, java.net.InetAddress.getByName("127.0.0.1"))
        var announced: ByteArray? = null
        val worker = thread(isDaemon = true) {
            val buffer = ByteArray(2048)
            repeat(2) {
                val packet = DatagramPacket(buffer, buffer.size)
                socket.receive(packet)
                val request = ByteBuffer.wrap(buffer, 0, packet.length)
                val first = request.long
                val action = request.int
                val transaction = request.int
                val reply = if (action == 0) {
                    check(first == 0x41727101980L)
                    ByteBuffer.allocate(16).putInt(0).putInt(transaction).putLong(0x1122334455667788L).array()
                } else {
                    check(first == 0x1122334455667788L) { "the connection id must be echoed" }
                    announced = buffer.copyOf(packet.length)
                    ByteBuffer.allocate(20 + 12).putInt(1).putInt(transaction).putInt(1200).putInt(3).putInt(7)
                        .put(byteArrayOf(10, 9, 8, 7, 0x1A, 0xE1.toByte(), 1, 2, 3, 4, 0x00, 0x50)).array()
                }
                socket.send(DatagramPacket(reply, reply.size, packet.socketAddress))
            }
        }
        try {
            val result = TrackerClient().announce("udp://127.0.0.1:${socket.localPort}/announce", null, infoHash, peerId, 6881, 5, 6, 7, TrackerEvent.STARTED)
            worker.join(2000)

            assertEquals(listOf("10.9.8.7" to 6881, "1.2.3.4" to 80), result.peers)
            assertEquals(1200, result.intervalSeconds)
            val sent = ByteBuffer.wrap(announced!!)
            sent.position(16)
            val hash = ByteArray(20).also { sent.get(it) }
            assertContentEquals(infoHash, hash)
        } finally {
            socket.close()
        }
    }

    @Test
    fun aUdpTrackerThatNeverAnswersTimesOutInsteadOfHanging() {
        val silent = DatagramSocket(0, java.net.InetAddress.getByName("127.0.0.1"))
        try {
            assertFailsWith<TrackerException> {
                runBlocking { TrackerClient(timeoutMillis = 300).announce("udp://127.0.0.1:${silent.localPort}/a", null, infoHash, peerId, 6881, 0, 0, 0, TrackerEvent.NONE) }
            }
        } finally {
            silent.close()
        }
    }
}
