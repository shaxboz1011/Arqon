package app.arqon.core.vpn

/** Reading and writing the raw IPv4 packets that a VPN tunnel hands over. Only what a TCP/UDP forwarder needs. */
internal object Bytes {
    fun u8(b: ByteArray, o: Int) = b[o].toInt() and 0xff
    fun u16(b: ByteArray, o: Int) = (u8(b, o) shl 8) or u8(b, o + 1)
    fun u32(b: ByteArray, o: Int): Long = (u16(b, o).toLong() shl 16) or u16(b, o + 2).toLong()
    fun i32(b: ByteArray, o: Int): Int = (u16(b, o) shl 16) or u16(b, o + 2)

    fun put16(b: ByteArray, o: Int, v: Int) {
        b[o] = (v shr 8).toByte(); b[o + 1] = v.toByte()
    }

    fun put32(b: ByteArray, o: Int, v: Long) {
        b[o] = (v shr 24).toByte(); b[o + 1] = (v shr 16).toByte(); b[o + 2] = (v shr 8).toByte(); b[o + 3] = v.toByte()
    }

    fun putAddress(b: ByteArray, o: Int, v: Int) = put32(b, o, v.toLong() and 0xffffffffL)

    /** Internet checksum over [len] bytes, continuing from [initial] (a sum that was not folded yet). */
    fun checksum(b: ByteArray, off: Int, len: Int, initial: Long = 0): Int {
        var sum = initial
        var i = off
        val end = off + len
        while (i + 1 < end) {
            sum += u16(b, i); i += 2
        }
        if (i < end) sum += u8(b, i) shl 8
        while (sum shr 16 != 0L) sum = (sum and 0xffff) + (sum shr 16)
        return sum.inv().toInt() and 0xffff
    }

    /** The unfolded sum of the pseudo header that TCP and UDP checksums start with. */
    fun pseudoHeader(src: Int, dst: Int, protocol: Int, length: Int): Long =
        (src ushr 16).toLong() + (src and 0xffff) + (dst ushr 16) + (dst and 0xffff) + protocol + length

    /** Signed distance from [b] to [a] on the 32-bit sequence circle: positive when [a] is ahead of [b]. */
    fun seqDiff(a: Long, b: Long): Int = (a - b).toInt()
}

const val PROTOCOL_TCP = 6
const val PROTOCOL_UDP = 17

/** A parsed IPv4 packet. [valid] is false for anything this forwarder does not carry (other versions, fragments, damaged ones). */
internal class Ipv4(val buf: ByteArray, length: Int) {
    val headerLength: Int
    val totalLength: Int
    val protocol: Int
    val src: Int
    val dst: Int
    val valid: Boolean

    init {
        if (length < 20 || (buf[0].toInt() shr 4) != 4) {
            headerLength = 0; totalLength = 0; protocol = 0; src = 0; dst = 0; valid = false
        } else {
            headerLength = (buf[0].toInt() and 0x0f) * 4
            totalLength = Bytes.u16(buf, 2)
            protocol = Bytes.u8(buf, 9)
            src = Bytes.i32(buf, 12)
            dst = Bytes.i32(buf, 16)
            val fragmented = (Bytes.u16(buf, 6) and 0x3fff) != 0   // more-fragments flag or a fragment offset
            valid = headerLength >= 20 && totalLength in headerLength..length && !fragmented
        }
    }

    val payloadOffset get() = headerLength
    val payloadLength get() = totalLength - headerLength
}

internal const val FIN = 0x01
internal const val SYN = 0x02
internal const val RST = 0x04
internal const val PSH = 0x08
internal const val ACK = 0x10

/** A parsed TCP segment inside an [Ipv4] packet. */
internal class TcpSegment(val ip: Ipv4) {
    private val b = ip.buf
    private val o = ip.payloadOffset

    val valid = ip.protocol == PROTOCOL_TCP && ip.payloadLength >= 20 && ((Bytes.u8(b, o + 12) shr 4) * 4).let { it >= 20 && it <= ip.payloadLength }
    val srcPort get() = Bytes.u16(b, o)
    val dstPort get() = Bytes.u16(b, o + 2)
    val seq get() = Bytes.u32(b, o + 4)
    val ack get() = Bytes.u32(b, o + 8)
    val headerLength get() = (Bytes.u8(b, o + 12) shr 4) * 4
    val flags get() = Bytes.u8(b, o + 13)
    val window get() = Bytes.u16(b, o + 14)
    val payloadOffset get() = o + headerLength
    val payloadLength get() = ip.payloadLength - headerLength

    /** The maximum segment size the sender offered in a SYN, or null. */
    fun mssOption(): Int? {
        var i = o + 20
        val end = o + headerLength
        while (i < end) {
            when (val kind = Bytes.u8(b, i)) {
                0 -> return null
                1 -> i++
                else -> {
                    if (i + 1 >= end) return null
                    val size = Bytes.u8(b, i + 1)
                    if (size < 2) return null
                    if (kind == 2 && size == 4 && i + 4 <= end) return Bytes.u16(b, i + 2)
                    i += size
                }
            }
        }
        return null
    }
}

internal class UdpDatagram(val ip: Ipv4) {
    private val b = ip.buf
    private val o = ip.payloadOffset
    val valid = ip.protocol == PROTOCOL_UDP && ip.payloadLength >= 8
    val srcPort get() = Bytes.u16(b, o)
    val dstPort get() = Bytes.u16(b, o + 2)
    val payloadOffset get() = o + 8
    val payloadLength get() = minOf(Bytes.u16(b, o + 4), ip.payloadLength) - 8
}

internal object PacketBuilder {
    private const val IP_HEADER = 20
    private var identification = 0

    @Synchronized
    private fun nextId(): Int = (++identification) and 0xffff

    private fun ipHeader(b: ByteArray, total: Int, protocol: Int, src: Int, dst: Int) {
        b[0] = 0x45
        b[1] = 0
        Bytes.put16(b, 2, total)
        Bytes.put16(b, 4, nextId())
        Bytes.put16(b, 6, 0x4000)   // don't fragment
        b[8] = 64
        b[9] = protocol.toByte()
        Bytes.put16(b, 10, 0)
        Bytes.putAddress(b, 12, src)
        Bytes.putAddress(b, 16, dst)
        Bytes.put16(b, 10, Bytes.checksum(b, 0, IP_HEADER))
    }

    fun tcp(
        src: Int, srcPort: Int, dst: Int, dstPort: Int,
        seq: Long, ack: Long, flags: Int, window: Int,
        data: ByteArray? = null, dataOffset: Int = 0, dataLength: Int = 0,
        mss: Int? = null,
    ): ByteArray {
        val tcpHeader = if (mss != null) 24 else 20
        val total = IP_HEADER + tcpHeader + dataLength
        val b = ByteArray(total)
        ipHeader(b, total, PROTOCOL_TCP, src, dst)
        val o = IP_HEADER
        Bytes.put16(b, o, srcPort)
        Bytes.put16(b, o + 2, dstPort)
        Bytes.put32(b, o + 4, seq and 0xffffffffL)
        Bytes.put32(b, o + 8, ack and 0xffffffffL)
        b[o + 12] = ((tcpHeader / 4) shl 4).toByte()
        b[o + 13] = flags.toByte()
        Bytes.put16(b, o + 14, window.coerceIn(0, 0xffff))
        if (mss != null) {
            b[o + 20] = 2; b[o + 21] = 4
            Bytes.put16(b, o + 22, mss)
        }
        if (data != null && dataLength > 0) System.arraycopy(data, dataOffset, b, o + tcpHeader, dataLength)
        val length = tcpHeader + dataLength
        Bytes.put16(b, o + 16, Bytes.checksum(b, o, length, Bytes.pseudoHeader(src, dst, PROTOCOL_TCP, length)))
        return b
    }

    fun udp(src: Int, srcPort: Int, dst: Int, dstPort: Int, data: ByteArray, dataOffset: Int, dataLength: Int): ByteArray {
        val length = 8 + dataLength
        val total = IP_HEADER + length
        val b = ByteArray(total)
        ipHeader(b, total, PROTOCOL_UDP, src, dst)
        val o = IP_HEADER
        Bytes.put16(b, o, srcPort)
        Bytes.put16(b, o + 2, dstPort)
        Bytes.put16(b, o + 4, length)
        System.arraycopy(data, dataOffset, b, o + 8, dataLength)
        val sum = Bytes.checksum(b, o, length, Bytes.pseudoHeader(src, dst, PROTOCOL_UDP, length))
        Bytes.put16(b, o + 6, if (sum == 0) 0xffff else sum)
        return b
    }
}
