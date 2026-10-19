package app.arqon.core.torrent

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull

class MagnetTest {
    private val hex = "0123456789abcdef0123456789abcdef01234567"

    @Test
    fun aHexMagnetLinkIsParsed() {
        val magnet = Magnet.parse("magnet:?xt=urn:btih:$hex&dn=My%20Movie&tr=udp%3A%2F%2Ftracker.example%3A80%2Fannounce&tr=http%3A%2F%2Fb%2Fannounce")

        assertNotNull(magnet)
        assertEquals(hex, magnet.infoHashHex)
        assertEquals("My Movie", magnet.name)
        assertEquals(listOf("udp://tracker.example:80/announce", "http://b/announce"), magnet.trackers)
    }

    @Test
    fun aBase32InfoHashGivesTheSameBytesAsHex() {
        val hexMagnet = Magnet.parse("magnet:?xt=urn:btih:$hex")!!
        val base32 = Base32.encode(hexMagnet.infoHash)

        val parsed = Magnet.parse("magnet:?xt=urn:btih:$base32")

        assertEquals(hex, parsed!!.infoHashHex)
    }

    @Test
    fun directPeerAddressesAreKept() {
        val magnet = Magnet.parse("magnet:?xt=urn:btih:$hex&x.pe=10.0.0.5:6881&x.pe=[2001:db8::1]:51413")!!

        assertEquals(listOf("10.0.0.5" to 6881, "2001:db8::1" to 51413), magnet.peers)
    }

    @Test
    fun linksThatAreNotMagnetLinksOrHaveNoValidHashAreRefused() {
        assertNull(Magnet.parse("https://example.com/file.torrent"))
        assertNull(Magnet.parse("magnet:?dn=NoHash"))
        assertNull(Magnet.parse("magnet:?xt=urn:btih:nothex"))
        assertNull(Magnet.parse("magnet:?xt=urn:btih:${hex.take(39)}"))
    }

    @Test
    fun theSchemeIsNotCaseSensitive() {
        assertNotNull(Magnet.parse("MAGNET:?xt=urn:btih:$hex"))
    }
}
