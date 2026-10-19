package app.arqon.core.input

import kotlin.test.Test
import kotlin.test.assertEquals

class UserInputTest {
    @Test
    fun textIsRecognisedForWhatItIs() {
        assertEquals(InputKind.MAGNET, UserInput.classify("  magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567 "))
        assertEquals(InputKind.TORRENT_URL, UserInput.classify("https://example.com/files/linux.torrent"))
        assertEquals(InputKind.TORRENT_URL, UserInput.classify("https://example.com/get?id=5&name=a.torrent#x".substringBefore("?") + ".torrent"))
        assertEquals(InputKind.HTTP_URL, UserInput.classify("https://example.com/file.zip"))
        assertEquals(InputKind.HTTP_URL, UserInput.classify("http://example.com/dl.php?id=3"))
        assertEquals(InputKind.OTHER, UserInput.classify("hello there"))
        assertEquals(InputKind.OTHER, UserInput.classify(""))
    }

    @Test
    fun aTorrentLinkWithAQueryStringIsStillATorrent() {
        assertEquals(InputKind.TORRENT_URL, UserInput.classify("https://example.com/a.TORRENT?token=abc"))
    }

    @Test
    fun theAddressBarTurnsWhatWasTypedIntoAnAddress() {
        assertEquals("https://example.com", UserInput.addressFor("example.com"))
        assertEquals("https://sub.example.co.uk/path?q=1", UserInput.addressFor("sub.example.co.uk/path?q=1"))
        assertEquals("http://example.com/x", UserInput.addressFor("http://example.com/x"))
        assertEquals("http://192.168.0.1:8080/", UserInput.addressFor("192.168.0.1:8080/"))
        assertEquals("magnet:?xt=urn:btih:abc", UserInput.addressFor("magnet:?xt=urn:btih:abc"))
    }

    @Test
    fun textThatIsNotAnAddressBecomesASearch() {
        assertEquals("https://duckduckgo.com/?q=free+linux+iso", UserInput.addressFor("free linux iso"))
        assertEquals("https://duckduckgo.com/?q=hello", UserInput.addressFor("hello"))
        assertEquals("https://duckduckgo.com/?q=caf%C3%A9+menu", UserInput.addressFor("café menu"))
    }

    @Test
    fun aFileNameIsTakenFromTheLinkOrMadeUp() {
        assertEquals("linux.iso", UserInput.fileNameFromUrl("https://example.com/pub/linux.iso?mirror=2"))
        assertEquals("my file.zip", UserInput.fileNameFromUrl("https://example.com/my%20file.zip"))
        assertEquals("download", UserInput.fileNameFromUrl("https://example.com/"))
        assertEquals("a_b.bin", UserInput.fileNameFromUrl("https://example.com/a%2Fb.bin"))
    }
}
