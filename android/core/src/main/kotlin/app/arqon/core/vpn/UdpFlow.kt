package app.arqon.core.vpn

import app.arqon.core.net.Link
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.util.concurrent.Executor

/**
 * One UDP "conversation" of an app (a game's packets to its server, a DNS question ...), carried over a UDP socket that is bound to
 * the chosen network. Every datagram the server sends back is wrapped into a packet for the app.
 */
internal class UdpFlow(
    private val clientIp: Int,
    private val clientPort: Int,
    private val serverIp: Int,
    private val serverPort: Int,
    val link: Link,
    private val sink: PacketSink,
    private val mtu: Int,
    private val onClosed: (UdpFlow) -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private val socket = DatagramSocket()

    @Volatile var lastActivity = clock()
        private set
    @Volatile var closed = false
        private set

    init {
        try {
            link.bindDatagram(socket)
            val octets = ByteArray(4)
            Bytes.putAddress(octets, 0, serverIp)
            socket.connect(InetSocketAddress(InetAddress.getByAddress(octets), serverPort))   // only this server may answer
        } catch (e: Exception) {
            socket.close()
            throw e
        }
    }

    val isDns get() = serverPort == 53

    fun start(executor: Executor) {
        executor.execute(::receiveLoop)
    }

    /** A datagram of the app towards the server. Returns false when the link cannot send (the flow is then closed). */
    fun send(buffer: ByteArray, offset: Int, length: Int): Boolean {
        lastActivity = clock()
        return try {
            socket.send(DatagramPacket(buffer, offset, length))
            true
        } catch (_: IOException) {
            close()
            false
        }
    }

    private fun receiveLoop() {
        val buffer = ByteArray(65535)
        val packet = DatagramPacket(buffer, buffer.size)
        try {
            while (!closed) {
                packet.length = buffer.size
                socket.receive(packet)
                lastActivity = clock()
                // a datagram that does not fit the tunnel would need fragmenting; games keep their packets small
                if (packet.length + 28 <= mtu) {
                    sink.send(PacketBuilder.udp(serverIp, serverPort, clientIp, clientPort, buffer, 0, packet.length))
                }
            }
        } catch (_: IOException) {
        } finally {
            close()
        }
    }

    fun close() {
        if (closed) return
        closed = true
        socket.close()
        onClosed(this)
    }
}
