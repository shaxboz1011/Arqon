package app.arqon.core.torrent

import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.BitSet
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong
import kotlin.concurrent.thread

/**
 * A BitTorrent seeder for tests, listening on the loopback address. It serves the pieces of a [TestTorrent] and, like a real
 * peer, hands out the torrent's metadata (BEP 9) to clients that only know a magnet link.
 */
class TestSeeder(
    private val torrent: TestTorrent,
    private val serveMetadata: Boolean = true,
    private val corruptPieces: Set<Int> = emptySet(),
    private val blockDelayMillis: Long = 0,
    private val haveOnly: Set<Int>? = null,
) : AutoCloseable {
    private val server = ServerSocket(0, 50, InetAddress.getByName("127.0.0.1"))
    private val sockets = java.util.Collections.synchronizedList(ArrayList<Socket>())
    val port: Int get() = server.localPort
    val connections = AtomicInteger()
    val blocksServed = AtomicInteger()
    val bytesServed = AtomicLong()

    init {
        thread(isDaemon = true) {
            try {
                while (true) {
                    val socket = server.accept()
                    sockets += socket
                    connections.incrementAndGet()
                    thread(isDaemon = true) { try { serve(socket) } catch (_: Exception) { } finally { socket.close() } }
                }
            } catch (_: Exception) {
                // closed
            }
        }
    }

    private fun serve(socket: Socket) {
        val input = DataInputStream(socket.getInputStream().buffered())
        val output = DataOutputStream(socket.getOutputStream().buffered())
        val theirs = PeerWire.readHandshake(input)
        if (!theirs.infoHash.contentEquals(torrent.meta.infoHash)) return
        PeerWire.writeHandshake(output, torrent.meta.infoHash, ByteArray(20) { 9 })
        if (theirs.supportsExtensions) {
            PeerWire.write(output, PeerMessage.Extended(Extensions.HANDSHAKE_ID, Extensions.handshake(if (serveMetadata) torrent.meta.infoBytes.size else null)))
        }
        val bits = BitSet(torrent.meta.pieceCount)
        for (i in 0 until torrent.meta.pieceCount) if (haveOnly == null || i in haveOnly) bits.set(i)
        val bytes = ByteArray((torrent.meta.pieceCount + 7) / 8)
        for (i in 0 until torrent.meta.pieceCount) if (bits[i]) bytes[i / 8] = (bytes[i / 8].toInt() or (0x80 shr (i % 8))).toByte()
        PeerWire.write(output, PeerMessage.Bitfield(bytes))
        var theirMetadataId: Int? = null

        while (true) {
            when (val message = PeerWire.read(input)) {
                PeerMessage.Interested -> PeerWire.write(output, PeerMessage.Unchoke)
                is PeerMessage.Request -> {
                    if (blockDelayMillis > 0) Thread.sleep(blockDelayMillis)
                    if (!bits[message.piece]) continue
                    val piece = torrent.piece(message.piece).copyOfRange(message.begin, message.begin + message.length)
                    if (message.piece in corruptPieces) piece[0] = (piece[0] + 1).toByte()
                    PeerWire.write(output, PeerMessage.Piece(message.piece, message.begin, piece))
                    blocksServed.incrementAndGet()
                    bytesServed.addAndGet(piece.size.toLong())
                }
                is PeerMessage.Extended -> {
                    if (message.id == Extensions.HANDSHAKE_ID) {
                        theirMetadataId = Extensions.readHandshake(message.payload).metadataId
                    } else if (message.id == Extensions.OUR_METADATA_ID) {
                        val request = Extensions.readMetadata(message.payload) ?: continue
                        val target = theirMetadataId ?: continue
                        val info = torrent.meta.infoBytes
                        val from = request.piece * Extensions.METADATA_PIECE
                        if (request.type == 0 && serveMetadata && from < info.size) {
                            val chunk = info.copyOfRange(from, minOf(from + Extensions.METADATA_PIECE, info.size))
                            PeerWire.write(output, PeerMessage.Extended(target, Extensions.metadataData(request.piece, info.size, chunk)))
                        } else {
                            PeerWire.write(output, PeerMessage.Extended(target, Extensions.metadataReject(request.piece)))
                        }
                    }
                }
                else -> {}
            }
        }
    }

    override fun close() {
        server.close()
        synchronized(sockets) { sockets.forEach { try { it.close() } catch (_: Exception) { } } }
    }
}

/**
 * The other side of an upload test: it accepts a connection from the client under test, declares itself a leecher and asks for a
 * piece, to see whether the client serves what it has.
 */
class TestLeecher(private val torrent: TestTorrent, private val pieceToAsk: Int) : AutoCloseable {
    private val server = ServerSocket(0, 5, InetAddress.getByName("127.0.0.1"))
    val port: Int get() = server.localPort

    @Volatile var received: ByteArray? = null

    @Volatile var finished = false

    init {
        thread(isDaemon = true) {
            try {
                val socket = server.accept()
                val input = DataInputStream(socket.getInputStream().buffered())
                val output = DataOutputStream(socket.getOutputStream().buffered())
                PeerWire.readHandshake(input)
                PeerWire.writeHandshake(output, torrent.meta.infoHash, ByteArray(20) { 7 })
                PeerWire.write(output, PeerMessage.Interested)
                var asked = false
                while (!finished) {
                    when (val m = PeerWire.read(input)) {
                        PeerMessage.Unchoke -> if (!asked) {
                            asked = true
                            PeerWire.write(output, PeerMessage.Request(pieceToAsk, 0, torrent.meta.pieceSize(pieceToAsk).coerceAtMost(PeerWire.BLOCK_SIZE)))
                        }
                        is PeerMessage.Piece -> { received = m.data; finished = true }
                        else -> {}
                    }
                }
                socket.close()
            } catch (_: Exception) {
                // closed
            }
        }
    }

    override fun close() = server.close()
}
