package app.arqon.core.vpn

import app.arqon.core.net.Link
import app.arqon.core.net.LinkKind
import java.io.InputStream
import java.io.OutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.ServerSocket
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/** A stand-in for the phone's tunnel device: whole packets go in one end and come out of the other. */
private class PacketPipe {
    val packets = LinkedBlockingQueue<ByteArray>()

    val reader = object : InputStream() {
        override fun read(): Int = throw UnsupportedOperationException()
        override fun read(b: ByteArray): Int {
            val packet = packets.poll(30, TimeUnit.SECONDS) ?: return -1
            if (packet.isEmpty()) return -1   // end marker
            System.arraycopy(packet, 0, b, 0, packet.size)
            return packet.size
        }
    }

    val writer = object : OutputStream() {
        override fun write(b: Int) = throw UnsupportedOperationException()
        override fun write(b: ByteArray) { packets.add(b.copyOf()) }
    }

    fun end() = packets.add(ByteArray(0))
}

class TunnelTest {
    private val toTunnel = PacketPipe()      // what the app "sends"
    private val fromTunnel = PacketPipe()    // what comes back to the app
    private val loopback = (127 shl 24) or 1
    private val client = (10 shl 24) or 2
    private val link = Link("test", "Test", LinkKind.WIFI)
    private val tunnel = Tunnel(toTunnel.reader, fromTunnel.writer, { link })
    private val runner = thread(name = "tunnel-under-test") { tunnel.run() }

    @AfterTest
    fun stop() {
        toTunnel.end()
        tunnel.close()
        runner.join(5000)
    }

    private fun next(): ByteArray = assertNotNull(fromTunnel.packets.poll(5, TimeUnit.SECONDS), "the tunnel sent nothing")

    private fun nextTcp(): TcpSegment {
        val packet = next()
        val ip = Ipv4(packet, packet.size)
        assertTrue(ip.valid)
        return TcpSegment(ip).also { assertTrue(it.valid) }
    }

    private fun send(packet: ByteArray) = toTunnel.packets.add(packet)

    @Test
    fun `a tcp connection is carried both ways and closed`() {
        val server = ServerSocket(0, 5, InetAddress.getByName("127.0.0.1"))
        val echo = thread(isDaemon = true) {
            server.accept().use { s ->
                val input = s.getInputStream()
                val buffer = ByteArray(100)
                val n = input.read(buffer)
                s.getOutputStream().write("echo:".toByteArray() + buffer.copyOf(n))
                s.getOutputStream().flush()
                while (input.read() >= 0) { }   // wait for the client to finish
            }
        }
        val port = server.localPort
        val clientPort = 40000

        // handshake
        send(PacketBuilder.tcp(client, clientPort, loopback, port, 1000, 0, SYN, 65535, mss = 1400))
        val synAck = nextTcp()
        assertEquals(SYN or ACK, synAck.flags)
        assertEquals(1001L, synAck.ack)
        assertEquals(1360, synAck.mssOption())
        val serverSeq = (synAck.seq + 1) and 0xffffffffL
        send(PacketBuilder.tcp(client, clientPort, loopback, port, 1001, serverSeq, ACK, 65535))

        // data to the server, and the answer back
        val hello = "hello".toByteArray()
        send(PacketBuilder.tcp(client, clientPort, loopback, port, 1001, serverSeq, ACK or PSH, 65535, hello, 0, hello.size))
        val acked = nextTcp()
        assertEquals(ACK, acked.flags and ACK)
        assertEquals(1006L, acked.ack)

        var answer: TcpSegment = acked
        while (answer.payloadLength == 0) answer = nextTcp()
        assertEquals("echo:hello", String(answer.ip.buf, answer.payloadOffset, answer.payloadLength))
        assertEquals(serverSeq, answer.seq)
        val afterAnswer = (answer.seq + answer.payloadLength) and 0xffffffffL
        send(PacketBuilder.tcp(client, clientPort, loopback, port, 1006, afterAnswer, ACK, 65535))

        // the app closes: the server sees the end, the tunnel closes its side as well
        send(PacketBuilder.tcp(client, clientPort, loopback, port, 1006, afterAnswer, FIN or ACK, 65535))
        var seen = nextTcp()
        while (seen.flags and FIN == 0) seen = nextTcp()
        assertEquals(1007L, seen.ack)
        send(PacketBuilder.tcp(client, clientPort, loopback, port, 1007, (seen.seq + 1) and 0xffffffffL, ACK, 65535))
        echo.join(5000)
        server.close()
    }

    @Test
    fun `a large answer is cut into segments and arrives whole`() {
        val payload = ByteArray(20_000) { (it * 7).toByte() }
        val server = ServerSocket(0, 5, InetAddress.getByName("127.0.0.1"))
        thread(isDaemon = true) {
            server.accept().use { s ->
                s.getOutputStream().write(payload)
                s.getOutputStream().flush()
                s.shutdownOutput()
                while (s.getInputStream().read() >= 0) { }
            }
        }
        val port = server.localPort
        send(PacketBuilder.tcp(client, 41000, loopback, port, 500, 0, SYN, 65535, mss = 1400))
        val synAck = nextTcp()
        var expect = (synAck.seq + 1) and 0xffffffffL
        send(PacketBuilder.tcp(client, 41000, loopback, port, 501, expect, ACK, 65535))

        val received = java.io.ByteArrayOutputStream()
        var finished = false
        while (!finished) {
            val t = nextTcp()
            if (t.payloadLength > 0) {
                assertEquals(expect, t.seq)
                assertTrue(t.payloadLength <= 1360)
                received.write(t.ip.buf, t.payloadOffset, t.payloadLength)
                expect = (expect + t.payloadLength) and 0xffffffffL
                send(PacketBuilder.tcp(client, 41000, loopback, port, 501, expect, ACK, 65535))
            }
            if (t.flags and FIN != 0) {
                expect = (expect + 1) and 0xffffffffL
                send(PacketBuilder.tcp(client, 41000, loopback, port, 501, expect, FIN or ACK, 65535))
                finished = true
            }
        }
        assertEquals(payload.toList(), received.toByteArray().toList())
        server.close()
    }

    @Test
    fun `a window of zero holds the data back until the app opens it`() {
        val server = ServerSocket(0, 5, InetAddress.getByName("127.0.0.1"))
        thread(isDaemon = true) {
            server.accept().use { s ->
                s.getOutputStream().write(ByteArray(3000))
                s.getOutputStream().flush()
                while (s.getInputStream().read() >= 0) { }
            }
        }
        val port = server.localPort
        send(PacketBuilder.tcp(client, 42000, loopback, port, 10, 0, SYN, 0, mss = 1400))   // the app offers no room at all
        val synAck = nextTcp()
        val expect = (synAck.seq + 1) and 0xffffffffL
        send(PacketBuilder.tcp(client, 42000, loopback, port, 11, expect, ACK, 0))
        assertEquals(null, fromTunnel.packets.poll(700, TimeUnit.MILLISECONDS), "data was sent into a closed window")
        send(PacketBuilder.tcp(client, 42000, loopback, port, 11, expect, ACK, 65535))   // window update
        val first = nextTcp()
        assertTrue(first.payloadLength > 0)
        server.close()
    }

    @Test
    fun `a connection that cannot be opened is refused with a reset`() {
        val closed = ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")).also { it.close() }
        send(PacketBuilder.tcp(client, 43000, loopback, closed.localPort, 77, 0, SYN, 65535))
        val reply = nextTcp()
        assertEquals(RST, reply.flags and RST)
        assertEquals(78L, reply.ack)
    }

    @Test
    fun `a packet for a connection nobody knows is answered with a reset`() {
        send(PacketBuilder.tcp(client, 44000, loopback, 9, 5, 99, ACK, 65535, ByteArray(3), 0, 3))
        val reply = nextTcp()
        assertEquals(RST, reply.flags and RST)
        assertEquals(99L, reply.seq)
    }

    @Test
    fun `udp goes to the server and the answer comes back to the same port`() {
        val server = DatagramSocket(0, InetAddress.getByName("127.0.0.1"))
        thread(isDaemon = true) {
            val packet = DatagramPacket(ByteArray(100), 100)
            server.receive(packet)
            val answer = "pong:".toByteArray() + packet.data.copyOf(packet.length)
            server.send(DatagramPacket(answer, answer.size, packet.socketAddress))
        }
        val ping = "ping".toByteArray()
        send(PacketBuilder.udp(client, 50000, loopback, server.localPort, ping, 0, ping.size))
        val packet = next()
        val ip = Ipv4(packet, packet.size)
        val udp = UdpDatagram(ip)
        assertTrue(ip.valid && udp.valid)
        assertEquals(loopback, ip.src)
        assertEquals(client, ip.dst)
        assertEquals(server.localPort, udp.srcPort)
        assertEquals(50000, udp.dstPort)
        assertEquals("pong:ping", String(packet, udp.payloadOffset, udp.payloadLength))
        assertTrue(tunnel.stats().udpFlows == 1)
        server.close()
    }

    @Test
    fun `without a link nothing is carried`() {
        val none = PacketPipe()
        val back = PacketPipe()
        val lonely = Tunnel(none.reader, back.writer, { null })
        val t = thread { lonely.run() }
        none.packets.add(PacketBuilder.tcp(client, 1, loopback, 2, 5, 0, SYN, 100))
        val reply = back.packets.poll(5, TimeUnit.SECONDS)
        assertNotNull(reply)
        assertEquals(RST, TcpSegment(Ipv4(reply, reply.size)).flags and RST)
        none.end(); lonely.close(); t.join(5000)
    }
}
