package app.arqon.core.torrent

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

class PeerWireTest {
    private fun roundTrip(message: PeerMessage): PeerMessage {
        val bytes = ByteArrayOutputStream()
        PeerWire.write(DataOutputStream(bytes), message)
        return PeerWire.read(DataInputStream(ByteArrayInputStream(bytes.toByteArray())))
    }

    @Test
    fun simpleMessagesRoundTrip() {
        assertEquals(PeerMessage.KeepAlive, roundTrip(PeerMessage.KeepAlive))
        assertEquals(PeerMessage.Choke, roundTrip(PeerMessage.Choke))
        assertEquals(PeerMessage.Unchoke, roundTrip(PeerMessage.Unchoke))
        assertEquals(PeerMessage.Interested, roundTrip(PeerMessage.Interested))
        assertEquals(PeerMessage.NotInterested, roundTrip(PeerMessage.NotInterested))
        assertEquals(PeerMessage.Have(77), roundTrip(PeerMessage.Have(77)))
        assertEquals(PeerMessage.Request(3, 16384, 16384), roundTrip(PeerMessage.Request(3, 16384, 16384)))
        assertEquals(PeerMessage.Cancel(3, 0, 100), roundTrip(PeerMessage.Cancel(3, 0, 100)))
    }

    @Test
    fun messagesWithDataKeepTheirBytes() {
        val bits = byteArrayOf(0b1010_0000.toByte(), 0x01)
        assertContentEquals(bits, (roundTrip(PeerMessage.Bitfield(bits)) as PeerMessage.Bitfield).bits)

        val data = ByteArray(1000) { it.toByte() }
        val piece = roundTrip(PeerMessage.Piece(9, 32768, data)) as PeerMessage.Piece
        assertEquals(9, piece.piece)
        assertEquals(32768, piece.begin)
        assertContentEquals(data, piece.data)

        val extended = roundTrip(PeerMessage.Extended(1, "payload".toByteArray())) as PeerMessage.Extended
        assertEquals(1, extended.id)
        assertContentEquals("payload".toByteArray(), extended.payload)
    }

    @Test
    fun theHandshakeCarriesTheInfoHashAndAnnouncesExtensionSupport() {
        val hash = ByteArray(20) { it.toByte() }
        val id = ByteArray(20) { (100 + it).toByte() }
        val out = ByteArrayOutputStream()

        PeerWire.writeHandshake(out, hash, id)
        val read = PeerWire.readHandshake(DataInputStream(ByteArrayInputStream(out.toByteArray())))

        assertEquals(68, out.size())
        assertContentEquals(hash, read.infoHash)
        assertContentEquals(id, read.peerId)
        assertTrue(read.supportsExtensions)
    }

    @Test
    fun somethingThatIsNotBitTorrentIsRefused() {
        val garbage = ByteArray(68) { 'x'.code.toByte() }.also { it[0] = 19 }
        assertFailsWith<ProtocolException> { PeerWire.readHandshake(DataInputStream(ByteArrayInputStream(garbage))) }
    }

    @Test
    fun anAbsurdMessageLengthIsRefusedInsteadOfAllocatingMemory() {
        val bytes = ByteArrayOutputStream().also { DataOutputStream(it).writeInt(Int.MAX_VALUE) }.toByteArray()
        assertFailsWith<ProtocolException> { PeerWire.read(DataInputStream(ByteArrayInputStream(bytes))) }
    }

    @Test
    fun aMessageWithTheWrongSizeIsRefused() {
        val bytes = ByteArrayOutputStream().also { DataOutputStream(it).apply { writeInt(3); writeByte(6); writeShort(1) } }.toByteArray()
        assertFailsWith<ProtocolException> { PeerWire.read(DataInputStream(ByteArrayInputStream(bytes))) }
    }

    @Test
    fun anUnknownMessageIsSkippedAndTheNextOneIsStillRead() {
        val bytes = ByteArrayOutputStream().also {
            DataOutputStream(it).apply { writeInt(4); writeByte(99); write(byteArrayOf(1, 2, 3)); writeInt(1); writeByte(1) }
        }.toByteArray()
        val input = DataInputStream(ByteArrayInputStream(bytes))

        assertIs<PeerMessage.Unknown>(PeerWire.read(input))
        assertEquals(PeerMessage.Unchoke, PeerWire.read(input))
    }

    @Test
    fun theExtensionHandshakeTellsWhichMessagesAPeerUnderstands() {
        val capabilities = Extensions.readHandshake(Extensions.handshake(metadataSize = 31337))

        assertEquals(Extensions.OUR_METADATA_ID, capabilities.metadataId)
        assertEquals(Extensions.OUR_PEX_ID, capabilities.pexId)
        assertEquals(31337, capabilities.metadataSize)
    }

    @Test
    fun metadataMessagesCarryTheRawDataAfterTheDictionary() {
        val data = ByteArray(500) { (it * 3).toByte() }

        val message = Extensions.readMetadata(Extensions.metadataData(2, 5000, data))

        assertNotNull(message)
        assertEquals(1, message.type)
        assertEquals(2, message.piece)
        assertContentEquals(data, message.data)
        assertEquals(0, Extensions.readMetadata(Extensions.metadataRequest(4))!!.type)
    }

    @Test
    fun compactPeersAreDecoded() {
        val bytes = byteArrayOf(10, 0, 0, 5, 0x1A, 0xE1.toByte(), 192.toByte(), 168.toByte(), 1, 9, 0, 80)

        assertEquals(listOf("10.0.0.5" to 6881, "192.168.1.9" to 80), Extensions.compactPeers(bytes))
    }
}
