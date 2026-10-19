package app.arqon.core.torrent

import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

class ProtocolException(message: String) : IOException(message)

/** The messages two BitTorrent peers send each other once they have shaken hands. */
sealed interface PeerMessage {
    data object KeepAlive : PeerMessage
    data object Choke : PeerMessage
    data object Unchoke : PeerMessage
    data object Interested : PeerMessage
    data object NotInterested : PeerMessage
    data class Have(val piece: Int) : PeerMessage
    class Bitfield(val bits: ByteArray) : PeerMessage
    data class Request(val piece: Int, val begin: Int, val length: Int) : PeerMessage
    data class Cancel(val piece: Int, val begin: Int, val length: Int) : PeerMessage
    class Piece(val piece: Int, val begin: Int, val data: ByteArray) : PeerMessage
    class Extended(val id: Int, val payload: ByteArray) : PeerMessage
    data class Unknown(val id: Int) : PeerMessage
}

class Handshake(val infoHash: ByteArray, val peerId: ByteArray, val supportsExtensions: Boolean)

object PeerWire {
    private const val PROTOCOL = "BitTorrent protocol"
    const val MAX_MESSAGE = 4 * 1024 * 1024
    const val BLOCK_SIZE = 16 * 1024

    fun writeHandshake(out: OutputStream, infoHash: ByteArray, peerId: ByteArray) {
        require(infoHash.size == 20 && peerId.size == 20)
        val bytes = ByteArray(68)
        bytes[0] = PROTOCOL.length.toByte()
        PROTOCOL.toByteArray(Charsets.ISO_8859_1).copyInto(bytes, 1)
        bytes[25] = 0x10   // reserved bit 20: this client speaks the extension protocol (used for magnet links and peer exchange)
        infoHash.copyInto(bytes, 28)
        peerId.copyInto(bytes, 48)
        out.write(bytes)
        out.flush()
    }

    fun readHandshake(input: DataInputStream): Handshake {
        val length = input.readUnsignedByte()
        val protocol = ByteArray(length).also { input.readFully(it) }
        if (String(protocol, Charsets.ISO_8859_1) != PROTOCOL) throw ProtocolException("not a BitTorrent peer")
        val reserved = ByteArray(8).also { input.readFully(it) }
        val infoHash = ByteArray(20).also { input.readFully(it) }
        val peerId = ByteArray(20).also { input.readFully(it) }
        return Handshake(infoHash, peerId, (reserved[5].toInt() and 0x10) != 0)
    }

    fun write(out: DataOutputStream, message: PeerMessage) {
        synchronized(out) {
            when (message) {
                PeerMessage.KeepAlive -> out.writeInt(0)
                PeerMessage.Choke -> simple(out, 0)
                PeerMessage.Unchoke -> simple(out, 1)
                PeerMessage.Interested -> simple(out, 2)
                PeerMessage.NotInterested -> simple(out, 3)
                is PeerMessage.Have -> { out.writeInt(5); out.writeByte(4); out.writeInt(message.piece) }
                is PeerMessage.Bitfield -> { out.writeInt(1 + message.bits.size); out.writeByte(5); out.write(message.bits) }
                is PeerMessage.Request -> { out.writeInt(13); out.writeByte(6); out.writeInt(message.piece); out.writeInt(message.begin); out.writeInt(message.length) }
                is PeerMessage.Cancel -> { out.writeInt(13); out.writeByte(8); out.writeInt(message.piece); out.writeInt(message.begin); out.writeInt(message.length) }
                is PeerMessage.Piece -> {
                    out.writeInt(9 + message.data.size); out.writeByte(7); out.writeInt(message.piece); out.writeInt(message.begin); out.write(message.data)
                }
                is PeerMessage.Extended -> { out.writeInt(2 + message.payload.size); out.writeByte(20); out.writeByte(message.id); out.write(message.payload) }
                is PeerMessage.Unknown -> throw IllegalArgumentException("cannot send an unknown message")
            }
            out.flush()
        }
    }

    private fun simple(out: DataOutputStream, id: Int) {
        out.writeInt(1)
        out.writeByte(id)
    }

    fun read(input: DataInputStream): PeerMessage {
        val length = input.readInt()
        if (length == 0) return PeerMessage.KeepAlive
        if (length < 0 || length > MAX_MESSAGE) throw ProtocolException("message of $length bytes")
        val id = input.readUnsignedByte()
        val body = length - 1
        return when (id) {
            0 -> PeerMessage.Choke.also { skip(input, body) }
            1 -> PeerMessage.Unchoke.also { skip(input, body) }
            2 -> PeerMessage.Interested.also { skip(input, body) }
            3 -> PeerMessage.NotInterested.also { skip(input, body) }
            4 -> { need(body, 4); PeerMessage.Have(input.readInt()) }
            5 -> PeerMessage.Bitfield(ByteArray(body).also { input.readFully(it) })
            6 -> { need(body, 12); PeerMessage.Request(input.readInt(), input.readInt(), input.readInt()) }
            8 -> { need(body, 12); PeerMessage.Cancel(input.readInt(), input.readInt(), input.readInt()) }
            7 -> {
                if (body < 8) throw ProtocolException("short piece message")
                val piece = input.readInt()
                val begin = input.readInt()
                PeerMessage.Piece(piece, begin, ByteArray(body - 8).also { input.readFully(it) })
            }
            20 -> {
                if (body < 1) throw ProtocolException("short extended message")
                val extId = input.readUnsignedByte()
                PeerMessage.Extended(extId, ByteArray(body - 1).also { input.readFully(it) })
            }
            else -> PeerMessage.Unknown(id).also { skip(input, body) }
        }
    }

    private fun need(actual: Int, expected: Int) {
        if (actual != expected) throw ProtocolException("message has $actual bytes, expected $expected")
    }

    private fun skip(input: InputStream, count: Int) {
        var left = count
        val buffer = ByteArray(minOf(count, 4096).coerceAtLeast(1))
        while (left > 0) {
            val n = input.read(buffer, 0, minOf(left, buffer.size))
            if (n < 0) throw EOFException()
            left -= n
        }
    }
}

/** Messages of the extension protocol (BEP 10) that this client uses: metadata for magnet links (BEP 9) and peer exchange (BEP 11). */
object Extensions {
    const val HANDSHAKE_ID = 0
    const val UT_METADATA = "ut_metadata"
    const val UT_PEX = "ut_pex"
    const val OUR_METADATA_ID = 1
    const val OUR_PEX_ID = 2
    const val METADATA_PIECE = 16 * 1024

    fun handshake(metadataSize: Int?): ByteArray {
        val root = mutableMapOf<String, Any>(
            "m" to mapOf(UT_METADATA to OUR_METADATA_ID.toLong(), UT_PEX to OUR_PEX_ID.toLong()),
            "v" to "Arqon",
        )
        if (metadataSize != null) root["metadata_size"] = metadataSize.toLong()
        return Bencode.encode(root)
    }

    class PeerCapabilities(val metadataId: Int?, val pexId: Int?, val metadataSize: Int?)

    fun readHandshake(payload: ByteArray): PeerCapabilities {
        val root = try { Bencode.decode(payload) as? Map<*, *> } catch (_: BencodeException) { null } ?: return PeerCapabilities(null, null, null)
        val m = root["m"] as? Map<*, *>
        return PeerCapabilities(
            (m?.get(UT_METADATA) as? Long)?.toInt()?.takeIf { it > 0 },
            (m?.get(UT_PEX) as? Long)?.toInt()?.takeIf { it > 0 },
            (root["metadata_size"] as? Long)?.toInt()?.takeIf { it > 0 },
        )
    }

    fun metadataRequest(piece: Int): ByteArray = Bencode.encode(mapOf("msg_type" to 0L, "piece" to piece.toLong()))

    fun metadataReject(piece: Int): ByteArray = Bencode.encode(mapOf("msg_type" to 2L, "piece" to piece.toLong()))

    fun metadataData(piece: Int, totalSize: Int, data: ByteArray): ByteArray =
        Bencode.encode(mapOf("msg_type" to 1L, "piece" to piece.toLong(), "total_size" to totalSize.toLong())) + data

    class MetadataMessage(val type: Int, val piece: Int, val data: ByteArray)

    fun readMetadata(payload: ByteArray): MetadataMessage? {
        val (value, used) = try { Bencode.decodeFirst(payload) } catch (_: BencodeException) { return null }
        val dict = value as? Map<*, *> ?: return null
        val type = (dict["msg_type"] as? Long)?.toInt() ?: return null
        val piece = (dict["piece"] as? Long)?.toInt() ?: return null
        return MetadataMessage(type, piece, payload.copyOfRange(used, payload.size))
    }

    /** The IPv4 peers in a peer-exchange message ("added" list: 6 bytes each). */
    fun readPex(payload: ByteArray): List<Pair<String, Int>> {
        val root = try { Bencode.decode(payload) as? Map<*, *> } catch (_: BencodeException) { null } ?: return emptyList()
        val added = root["added"] as? ByteArray ?: return emptyList()
        return compactPeers(added)
    }

    fun compactPeers(bytes: ByteArray): List<Pair<String, Int>> {
        val peers = ArrayList<Pair<String, Int>>()
        var i = 0
        while (i + 6 <= bytes.size) {
            val host = "${bytes[i].toInt() and 0xFF}.${bytes[i + 1].toInt() and 0xFF}.${bytes[i + 2].toInt() and 0xFF}.${bytes[i + 3].toInt() and 0xFF}"
            val port = ((bytes[i + 4].toInt() and 0xFF) shl 8) or (bytes[i + 5].toInt() and 0xFF)
            if (port != 0) peers += host to port
            i += 6
        }
        return peers
    }
}
