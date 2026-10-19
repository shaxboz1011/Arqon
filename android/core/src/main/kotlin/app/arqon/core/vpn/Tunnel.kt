package app.arqon.core.vpn

import app.arqon.core.net.Link
import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.InetSocketAddress
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.concurrent.ThreadFactory
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

/** What the screen shows about the tunnel. */
data class TunnelStats(
    val tcpFlows: Int,
    val udpFlows: Int,
    val bytesUp: Long,
    val bytesDown: Long,
    /** When data (not just handshakes) last went through, in the clock's milliseconds; 0 when it never did. */
    val lastDataAt: Long,
    /** The link the newest connection or datagram was sent through. */
    val lastLinkId: String?,
)

/**
 * The heart of the game mode. The phone hands us every packet of the chosen apps (a "tunnel"); each TCP connection and each UDP
 * conversation is re-created as an ordinary socket on the link that [chooseLink] names at that moment, and the answers are
 * wrapped back into packets. The apps notice nothing, but their traffic now travels over the link we pick (the best one for
 * a game) while Arqon's own downloads keep to the other links.
 *
 * Reading blocks, so [run] belongs on its own thread. [close] ends everything.
 */
class Tunnel(
    private val input: InputStream,
    private val output: OutputStream,
    private val chooseLink: () -> Link?,
    private val mtu: Int = 1400,
    private val log: (String) -> Unit = {},
    private val clock: () -> Long = System::currentTimeMillis,
) : Closeable {
    private data class Key(val clientIp: Int, val clientPort: Int, val serverIp: Int, val serverPort: Int)

    private val tcp = ConcurrentHashMap<Key, TcpFlow>()
    private val udp = ConcurrentHashMap<Key, UdpFlow>()
    private val writeLock = ReentrantLock()
    private val threads = ThreadFactory { r -> Thread(r, "arqon-tunnel").apply { isDaemon = true } }
    private val executor = Executors.newCachedThreadPool(threads)
    private val sweeper = Executors.newSingleThreadScheduledExecutor(threads)

    private val up = AtomicLong()
    private val down = AtomicLong()
    @Volatile private var lastDataAt = 0L
    @Volatile private var lastLinkId: String? = null
    @Volatile private var closed = false

    private val sink = PacketSink { packet ->
        writeLock.withLock {
            try {
                output.write(packet)
                down.addAndGet(packet.size.toLong())
            } catch (_: IOException) {
                // the tunnel is going away
            }
        }
    }

    fun stats() = TunnelStats(tcp.size, udp.size, up.get(), down.get(), lastDataAt, lastLinkId)

    /** Reads packets until the tunnel is closed. */
    fun run() {
        sweeper.scheduleWithFixedDelay(::sweep, 250, 250, TimeUnit.MILLISECONDS)
        val buffer = ByteArray(32768)
        try {
            while (!closed) {
                val n = input.read(buffer)
                if (n < 0) break
                if (n == 0) continue
                up.addAndGet(n.toLong())
                try {
                    handle(buffer, n)
                } catch (e: RuntimeException) {
                    log("packet skipped: ${e.javaClass.simpleName}: ${e.message}")   // one bad packet must not end the game mode
                }
            }
        } catch (_: IOException) {
        } finally {
            close()
        }
    }

    private fun handle(buffer: ByteArray, length: Int) {
        val ip = Ipv4(buffer, length)
        if (!ip.valid) return
        when (ip.protocol) {
            PROTOCOL_TCP -> handleTcp(ip)
            PROTOCOL_UDP -> handleUdp(ip)
            else -> Unit   // pings and the like are not carried
        }
    }

    private fun handleTcp(ip: Ipv4) {
        val segment = TcpSegment(ip)
        if (!segment.valid) return
        val key = Key(ip.src, segment.srcPort, ip.dst, segment.dstPort)
        val opening = segment.flags and SYN != 0 && segment.flags and ACK == 0
        var flow = tcp[key]
        if (flow != null && !flow.isClosed && opening && flow.isReplacedBy(segment)) {
            flow.abort()   // the app reused the port for a new connection: the old one is over
            flow = null
        }
        if (flow != null && !flow.isClosed) {
            flow.onSegment(segment)
            if (segment.payloadLength > 0) lastDataAt = clock()
            return
        }
        if (opening) {
            val link = chooseLink()
            if (link == null || tcp.size >= MAX_FLOWS) {
                reset(ip, segment)
                return
            }
            lastLinkId = link.id
            val address = address(ip.dst)
            val created = TcpFlow(
                ip.src, segment.srcPort, ip.dst, segment.dstPort, sink, executor,
                connect = {
                    val socket = link.socketFactory.createSocket()
                    try {
                        socket.tcpNoDelay = true
                        socket.connect(InetSocketAddress(address, segment.dstPort), CONNECT_MILLIS)
                    } catch (e: Exception) {
                        try { socket.close() } catch (_: IOException) { }
                        throw e
                    }
                    socket
                },
                onClosed = { tcp.remove(key, it) },
                clock = clock,
            )
            tcp[key] = created
            created.start(segment)
        } else if (segment.flags and RST == 0) {
            reset(ip, segment)   // a connection we do not know: tell the app it is gone
        }
    }

    private fun handleUdp(ip: Ipv4) {
        val datagram = UdpDatagram(ip)
        if (!datagram.valid || datagram.payloadLength < 0) return
        val key = Key(ip.src, datagram.srcPort, ip.dst, datagram.dstPort)
        var flow = udp[key]
        if (flow == null || flow.closed) {
            val link = chooseLink() ?: return
            if (udp.size >= MAX_FLOWS) return
            flow = try {
                UdpFlow(ip.src, datagram.srcPort, ip.dst, datagram.dstPort, link, sink, mtu, { udp.remove(key, it) }, clock)
            } catch (e: Exception) {
                log("udp ${datagram.dstPort}: ${e.javaClass.simpleName}: ${e.message}")
                return
            }
            udp[key] = flow
            flow.start(executor)
        }
        lastLinkId = flow.link.id
        if (datagram.payloadLength > 0) {
            lastDataAt = clock()
            flow.send(ip.buf, datagram.payloadOffset, datagram.payloadLength)
        }
    }

    private fun reset(ip: Ipv4, segment: TcpSegment) {
        val packet = if (segment.flags and ACK != 0) {
            PacketBuilder.tcp(ip.dst, segment.dstPort, ip.src, segment.srcPort, segment.ack, 0, RST, 0)
        } else {
            val consumed = segment.payloadLength + (if (segment.flags and SYN != 0) 1 else 0) + (if (segment.flags and FIN != 0) 1 else 0)
            PacketBuilder.tcp(ip.dst, segment.dstPort, ip.src, segment.srcPort, 0, (segment.seq + consumed) and 0xffffffffL, RST or ACK, 0)
        }
        sink.send(packet)
    }

    private fun sweep() {
        val now = clock()
        for (flow in tcp.values) flow.tick(now)
        for ((key, flow) in udp) {
            val idle = now - flow.lastActivity
            if (idle > (if (flow.isDns) DNS_IDLE_MILLIS else UDP_IDLE_MILLIS)) {
                udp.remove(key, flow)
                flow.close()
            }
        }
    }

    private fun address(ip: Int): InetAddress {
        val octets = ByteArray(4)
        Bytes.putAddress(octets, 0, ip)
        return InetAddress.getByAddress(octets)
    }

    override fun close() {
        if (closed) return
        closed = true
        sweeper.shutdownNow()
        for (flow in tcp.values) flow.abort()
        for (flow in udp.values) flow.close()
        tcp.clear()
        udp.clear()
        executor.shutdownNow()
    }

    private companion object {
        const val MAX_FLOWS = 512
        const val CONNECT_MILLIS = 8000
        const val UDP_IDLE_MILLIS = 120_000L
        const val DNS_IDLE_MILLIS = 15_000L
    }
}
