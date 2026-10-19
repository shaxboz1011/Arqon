package app.arqon.core.torrent

import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class BencodeTest {
    private fun decode(text: String) = Bencode.decode(text.toByteArray(Charsets.ISO_8859_1))

    @Test
    fun decodesIntegersStringsListsAndDictionaries() {
        assertEquals(42L, decode("i42e"))
        assertEquals(-7L, decode("i-7e"))
        assertContentEquals("spam".toByteArray(), decode("4:spam") as ByteArray)
        assertEquals(2, (decode("li1ei2ee") as List<*>).size)
        val dict = decode("d3:cow3:moo4:spam4:eggse") as Map<*, *>
        assertContentEquals("moo".toByteArray(), dict["cow"] as ByteArray)
        assertContentEquals("eggs".toByteArray(), dict["spam"] as ByteArray)
    }

    @Test
    fun encodingWhatWasDecodedGivesTheSameBytes() {
        val text = "d1:ai1e1:bl1:xi2ee1:cd1:y3:abcee"
        val bytes = text.toByteArray(Charsets.ISO_8859_1)
        assertContentEquals(bytes, Bencode.encode(Bencode.decode(bytes)))
    }

    @Test
    fun theRawBytesOfADictionaryKeyAreAvailableForTheInfoHash() {
        val bytes = "d4:infod4:name3:abce5:otheri1ee".toByteArray(Charsets.ISO_8859_1)
        val raw = Bencode.rawValueOf(bytes, "info")
        assertContentEquals("d4:name3:abce".toByteArray(), raw)
    }

    @Test
    fun brokenInputIsRefusedClearly() {
        assertFailsWith<BencodeException> { decode("i12") }
        assertFailsWith<BencodeException> { decode("5:abc") }
        assertFailsWith<BencodeException> { decode("x") }
        assertFailsWith<BencodeException> { decode("") }
    }

    @Test
    fun trailingGarbageAfterTheValueIsRefused() {
        assertFailsWith<BencodeException> { decode("i1ei2e") }
    }
}
