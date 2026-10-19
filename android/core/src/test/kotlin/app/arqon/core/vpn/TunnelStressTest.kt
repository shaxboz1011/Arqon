package app.arqon.core.vpn

import app.arqon.core.net.Link
import app.arqon.core.net.LinkKind
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * Many connections at once, each moving a megabyte both ways through the tunnel, with a small stand-in for the phone's TCP that
 * obeys the window the tunnel announces. It shows that nothing is lost, reordered, stuck or mixed up between connections.
 */
class TunnelStressTest {
    private val toTunnel = LinkedBlockingQueue<ByteArray>()
    private val fromTunnel = LinkedBlockingQueue<ByteArray>()
    private val perPort = ConcurrentHashMap<Int, LinkedBlockingQueue<TcpSegment>>()
    private val loopback = (127 shl 24) or 1
    private val client = (10 shl 24) or 2
    private val link = Link("test", "Test", LinkKind.WIFI)

    private val input = object : InputStream() {
        override fun read(): Int = throw UnsupportedOperationException()
        override fun read(b: ByteArray): Int {
            val packet = toTunnel.poll(60, TimeUnit.SECONDS) ?: return -1
            if (packet.isEmpty()) return -1
            System.arraycopy(packet, 0, b, 0, packet.size)
            return packet.size
        }
    }
    private val output = object : OutputStream() {
        override fun write(b: Int) = throw UnsupportedOperationException()
        override fun write(b: ByteArray) { fromTunnel.add(b.copyOf()) }
    }

    private val tunnel = Tunnel(input, output, { link })
    private val runner = thread(name = "stress-tunnel") { tunnel.run() }
    private val router = thread(isDaemon = true, name = "stress-router") {
        while (true) {
            val packet = fromTunnel.take()
            if (packet.isEmpty()) break
            val ip = Ipv4(packet, packet.size)
            val tcp = TcpSegment(ip)
            if (ip.valid && tcp.valid) perPort.getOrPut(tcp.dstPort) { LinkedBlockingQueue() }.add(tcp)
        }
    }

    @AfterTest
    fun stop() {
        toTunnel.add(ByteArray(0))
        tunnel.close()
        fromTunnel.add(ByteArray(0))
        runner.join(5000)
    }

    /** One connection of the phone's side: the handshake, then sending (within the announced window) and receiving. */
    private inner class Phone(private val port: Int, private val serverPort: Int) {
        private val inbox = perPort.getOrPut(port) { LinkedBlockingQueue() }
        private var seq = 1000L + port
        private var ackNext = 0L
        private var window = 65535
        private var acked = seq + 1

        private fun send(flags: Int, data: ByteArray? = null) {
            val n = data?.size ?: 0
            toTunnel.add(PacketBuilder.tcp(client, port, loopback, serverPort, seq, ackNext, flags, 65535, data, 0, n))
        }

        fun connect() {
            send(SYN)
            seq++
            val synAck = take()
            check(synAck.flags and (SYN or ACK) == (SYN or ACK)) { "no SYN+ACK" }
            ackNext = (synAck.seq + 1) and 0xffffffffL
            window = synAck.window
            send(ACK)
        }

        private fun take(): TcpSegment = inbox.poll(30, TimeUnit.SECONDS) ?: error("the tunnel went quiet (port $port)")

        /** Sends [data] without ever having more than the announced window in flight. */
        fun upload(data: ByteArray) {
            var offset = 0
            while (offset < data.size) {
                val room = window - (seq - acked).toInt()
                if (room < 1) {
                    consumeOne()
                    continue
                }
                val n = minOf(1360, room, data.size - offset)
                send(ACK or PSH, data.copyOfRange(offset, offset + n))
                seq += n
                offset += n
                drain()
            }
            while (acked < seq) consumeOne()
        }

        private fun drain() {
            while (true) {
                val t = inbox.poll() ?: return
                handle(t)
            }
        }

        private fun consumeOne() = handle(take())

        private val received = ByteArrayOutputStream()
        private var finSeen = false

        private fun handle(t: TcpSegment) {
            if (t.flags and ACK != 0 && t.ack > acked) acked = t.ack
            window = t.window
            if (t.payloadLength > 0 && t.seq == ackNext) {
                received.write(t.ip.buf, t.payloadOffset, t.payloadLength)
                ackNext = (ackNext + t.payloadLength) and 0xffffffffL
                send(ACK)
            }
            if (t.flags and FIN != 0 && ((t.seq + t.payloadLength) and 0xffffffffL) == ackNext) {
                ackNext = (ackNext + 1) and 0xffffffffL
                finSeen = true
                send(ACK)
            }
        }

        /** Reads until the server closes its side. */
        fun receiveAll(): ByteArray {
            while (!finSeen) consumeOne()
            return received.toByteArray()
        }

        fun close() {
            send(FIN or ACK)
            seq++
        }
    }

    private fun pattern(size: Int, salt: Int) = ByteArray(size) { ((it * 31 + salt) xor (it ushr 8)).toByte() }

    private fun sha(b: ByteArray) = MessageDigest.getInstance("SHA-256").digest(b)

    @Test
    fun `sixteen connections move a megabyte both ways without loss or mix-ups`() {
        val flows = 16
        val size = 1_000_000
        val server = ServerSocket(0, 64, InetAddress.getByName("127.0.0.1"))
        val serverPort = server.localPort
        val uploadedDigests = ConcurrentHashMap<Int, ByteArray>()

        // the server: reads everything the phone sends, remembers a digest, then sends its own megabyte and closes
        thread(isDaemon = true) {
            repeat(flows) {
                val s = server.accept()
                thread(isDaemon = true) {
                    s.use {
                        val got = ByteArrayOutputStream()
                        val buffer = ByteArray(32 * 1024)
                        val input = it.getInputStream()
                        while (got.size() < size) {
                            val n = input.read(buffer)
                            if (n < 0) break
                            got.write(buffer, 0, n)
                        }
                        // the first two bytes name the flow so the digest can be matched afterwards
                        val bytes = got.toByteArray()
                        uploadedDigests[(bytes[0].toInt() and 0xff) shl 8 or (bytes[1].toInt() and 0xff)] = sha(bytes)
                        it.getOutputStream().write(pattern(size, 7))
                        it.getOutputStream().flush()
                    }
                }
            }
        }

        val done = CountDownLatch(flows)
        val failures = ConcurrentHashMap<Int, String>()
        val expectedDown = sha(pattern(size, 7))
        for (i in 0 until flows) {
            thread(isDaemon = true) {
                try {
                    val phone = Phone(46000 + i, serverPort)
                    phone.connect()
                    val up = pattern(size, i).also { it[0] = (i shr 8).toByte(); it[1] = i.toByte() }
                    phone.upload(up)
                    val down = phone.receiveAll()
                    phone.close()
                    if (!sha(down).contentEquals(expectedDown)) failures[i] = "download differs (${down.size} bytes)"
                    if (!uploadedDigests.getOrDefault(i, ByteArray(0)).contentEquals(sha(up))) failures[i] = (failures[i] ?: "") + " upload differs"
                } catch (e: Throwable) {
                    failures[i] = e.toString()
                } finally {
                    done.countDown()
                }
            }
        }
        assertTrue(done.await(90, TimeUnit.SECONDS), "the connections did not finish in time")
        assertEquals(emptyMap(), failures.toMap())
        server.close()
    }

    @Test
    fun `a megabyte pattern really differs between connections`() {
        // a guard for the test itself: if every flow sent the same bytes, mix-ups could go unnoticed
        assertTrue(!sha(pattern(1000, 1)).contentEquals(sha(pattern(1000, 2))))
        assertContentEquals(pattern(50, 3), pattern(50, 3))
    }
}
