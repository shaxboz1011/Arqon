package app.arqon.core.vpn

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class PacketsTest {
    private val client = ip(10, 0, 0, 2)
    private val server = ip(93, 184, 216, 34)

    private fun ip(a: Int, b: Int, c: Int, d: Int) = (a shl 24) or (b shl 16) or (c shl 8) or d

    /** A correct packet adds up to zero when its own checksum is included. */
    private fun sumIsZero(packet: ByteArray, off: Int, len: Int, initial: Long = 0) = Bytes.checksum(packet, off, len, initial) == 0

    @Test
    fun `the checksum of a known header`() {
        // a textbook IPv4 header: 4500 0073 0000 4000 4011 [b861] c0a8 0001 c0a8 00c7
        val header = intArrayOf(0x45, 0x00, 0x00, 0x73, 0x00, 0x00, 0x40, 0x00, 0x40, 0x11, 0x00, 0x00, 0xc0, 0xa8, 0x00, 0x01, 0xc0, 0xa8, 0x00, 0xc7)
            .map { it.toByte() }.toByteArray()
        assertEquals(0xb861, Bytes.checksum(header, 0, 20))
    }

    @Test
    fun `a built tcp packet reads back and its checksums hold`() {
        val data = "hello".toByteArray()
        val packet = PacketBuilder.tcp(client, 40000, server, 443, 1000, 2000, ACK or PSH, 65535, data, 0, data.size)
        assertTrue(sumIsZero(packet, 0, 20))
        assertTrue(sumIsZero(packet, 20, packet.size - 20, Bytes.pseudoHeader(client, server, PROTOCOL_TCP, packet.size - 20)))

        val ip = Ipv4(packet, packet.size)
        assertTrue(ip.valid)
        assertEquals(client, ip.src)
        assertEquals(server, ip.dst)
        val tcp = TcpSegment(ip)
        assertTrue(tcp.valid)
        assertEquals(40000, tcp.srcPort)
        assertEquals(443, tcp.dstPort)
        assertEquals(1000L, tcp.seq)
        assertEquals(2000L, tcp.ack)
        assertEquals(ACK or PSH, tcp.flags)
        assertEquals(65535, tcp.window)
        assertEquals(5, tcp.payloadLength)
        assertEquals("hello", String(packet, tcp.payloadOffset, tcp.payloadLength))
    }

    @Test
    fun `sequence numbers past the 32 bit edge survive`() {
        val packet = PacketBuilder.tcp(client, 1, server, 2, 0xfffffff0L, 0xffffffffL, ACK, 100)
        val tcp = TcpSegment(Ipv4(packet, packet.size))
        assertEquals(0xfffffff0L, tcp.seq)
        assertEquals(0xffffffffL, tcp.ack)
    }

    @Test
    fun `the segment size offered in a SYN is found`() {
        val packet = PacketBuilder.tcp(client, 1, server, 2, 5, 0, SYN, 100, mss = 1360)
        val tcp = TcpSegment(Ipv4(packet, packet.size))
        assertEquals(1360, tcp.mssOption())
        assertEquals(0, tcp.payloadLength)
        val plain = PacketBuilder.tcp(client, 1, server, 2, 5, 0, SYN, 100)
        assertEquals(null, TcpSegment(Ipv4(plain, plain.size)).mssOption())
    }

    @Test
    fun `a built udp packet reads back and its checksums hold`() {
        val data = ByteArray(33) { it.toByte() }
        val packet = PacketBuilder.udp(server, 27015, client, 50000, data, 0, data.size)
        assertTrue(sumIsZero(packet, 0, 20))
        assertTrue(sumIsZero(packet, 20, packet.size - 20, Bytes.pseudoHeader(server, client, PROTOCOL_UDP, packet.size - 20)))
        val udp = UdpDatagram(Ipv4(packet, packet.size))
        assertTrue(udp.valid)
        assertEquals(27015, udp.srcPort)
        assertEquals(50000, udp.dstPort)
        assertEquals(33, udp.payloadLength)
        assertEquals(data.toList(), packet.copyOfRange(udp.payloadOffset, udp.payloadOffset + 33).toList())
    }

    @Test
    fun `things the forwarder does not carry are not valid`() {
        assertFalse(Ipv4(ByteArray(10), 10).valid)                    // too short
        val v6 = ByteArray(40).also { it[0] = 0x60 }
        assertFalse(Ipv4(v6, 40).valid)                               // not IPv4
        val packet = PacketBuilder.udp(client, 1, server, 2, ByteArray(4), 0, 4)
        packet[6] = 0x20                                              // "more fragments"
        assertFalse(Ipv4(packet, packet.size).valid)
        val whole = PacketBuilder.udp(client, 1, server, 2, ByteArray(4), 0, 4)
        assertFalse(Ipv4(whole, whole.size - 2).valid)                // cut short
    }

    @Test
    fun `sequence differences wrap around`() {
        assertEquals(10, Bytes.seqDiff(5, 0xfffffffbL))
        assertEquals(-10, Bytes.seqDiff(0xfffffffbL, 5))
        assertEquals(0, Bytes.seqDiff(7, 7))
    }
}
