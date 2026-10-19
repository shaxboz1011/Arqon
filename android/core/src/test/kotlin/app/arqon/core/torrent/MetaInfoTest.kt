package app.arqon.core.torrent

import java.security.MessageDigest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

class MetaInfoTest {
    private fun sha1(bytes: ByteArray) = MessageDigest.getInstance("SHA-1").digest(bytes)

    private fun torrent(info: Map<String, Any>, extra: Map<String, Any> = emptyMap()): ByteArray =
        Bencode.encode(extra + mapOf("info" to info))

    private fun singleFileInfo(length: Long = 40_000, pieceLength: Int = 16_384): Map<String, Any> {
        val pieceCount = ((length + pieceLength - 1) / pieceLength).toInt()
        return mapOf(
            "name" to "movie.bin", "length" to length, "piece length" to pieceLength.toLong(),
            "pieces" to ByteArray(20 * pieceCount) { it.toByte() },
        )
    }

    @Test
    fun aSingleFileTorrentIsRead() {
        val meta = MetaInfo.parse(torrent(singleFileInfo(), mapOf("announce" to "http://tracker.example/announce")))

        assertEquals("movie.bin", meta.name)
        assertEquals(40_000L, meta.totalSize)
        assertEquals(16_384, meta.pieceLength)
        assertEquals(3, meta.pieceCount)
        assertEquals(1, meta.files.size)
        assertEquals("movie.bin", meta.files[0].path)
        assertEquals(listOf("http://tracker.example/announce"), meta.trackers)
    }

    @Test
    fun theInfoHashIsTheSha1OfTheOriginalInfoBytes() {
        val bytes = torrent(singleFileInfo())
        val expected = sha1(Bencode.rawValueOf(bytes, "info")!!)

        assertContentEquals(expected, MetaInfo.parse(bytes).infoHash)
    }

    @Test
    fun aMultiFileTorrentGetsFileOffsetsInOrder() {
        val info = mapOf(
            "name" to "pack", "piece length" to 16_384L, "pieces" to ByteArray(20 * 3),
            "files" to listOf(
                mapOf("length" to 20_000L, "path" to listOf("a", "one.bin")),
                mapOf("length" to 25_000L, "path" to listOf("two.bin")),
            ),
        )

        val meta = MetaInfo.parse(torrent(info))

        assertEquals(45_000L, meta.totalSize)
        assertEquals(listOf("pack/a/one.bin", "pack/two.bin"), meta.files.map { it.path })
        assertEquals(listOf(0L, 20_000L), meta.files.map { it.offset })
    }

    @Test
    fun trackersFromTheAnnounceListAreCollectedWithoutDuplicates() {
        val bytes = torrent(
            singleFileInfo(),
            mapOf("announce" to "udp://a:1/announce", "announce-list" to listOf(listOf("udp://a:1/announce"), listOf("http://b/announce", "udp://c:2/announce"))),
        )

        assertEquals(listOf("udp://a:1/announce", "http://b/announce", "udp://c:2/announce"), MetaInfo.parse(bytes).trackers)
    }

    @Test
    fun pathsThatTryToLeaveTheDownloadFolderAreRefused() {
        val info = mapOf(
            "name" to "pack", "piece length" to 16_384L, "pieces" to ByteArray(20),
            "files" to listOf(mapOf("length" to 10L, "path" to listOf("..", "evil.bin"))),
        )

        assertFailsWith<TorrentFormatException> { MetaInfo.parse(torrent(info)) }
        assertFailsWith<TorrentFormatException> {
            MetaInfo.parse(torrent(singleFileInfo().plus("name" to "../x")))
        }
    }

    @Test
    fun aPieceCountThatDoesNotMatchTheSizeIsRefused() {
        val info = singleFileInfo(length = 40_000).plus("pieces" to ByteArray(20))   // needs 3 hashes
        assertFailsWith<TorrentFormatException> { MetaInfo.parse(torrent(info)) }
    }

    @Test
    fun theLastPieceIsShorterWhenTheSizeIsNotAMultiple() {
        val meta = MetaInfo.parse(torrent(singleFileInfo(40_000, 16_384)))

        assertEquals(16_384, meta.pieceSize(0))
        assertEquals(40_000 - 2 * 16_384, meta.pieceSize(2))
    }

    @Test
    fun metadataFetchedForAMagnetLinkIsReadFromTheInfoBytesAlone() {
        val info = Bencode.encode(singleFileInfo())

        val meta = MetaInfo.fromInfo(info, listOf("udp://t:1/announce"))

        assertContentEquals(sha1(info), meta.infoHash)
        assertEquals(listOf("udp://t:1/announce"), meta.trackers)
    }

    @Test
    fun aFileThatIsNotATorrentIsRefused() {
        assertFailsWith<TorrentFormatException> { MetaInfo.parse("hello".toByteArray()) }
        assertFailsWith<TorrentFormatException> { MetaInfo.parse(Bencode.encode(mapOf("x" to 1L))) }
    }

    @Test
    fun noMagnetMeansNoTrackersAreInvented() {
        assertNull(Magnet.parse("not a magnet"))
    }
}
