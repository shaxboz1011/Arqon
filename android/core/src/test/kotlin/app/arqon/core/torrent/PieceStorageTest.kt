package app.arqon.core.torrent

import java.io.File
import java.nio.file.Files
import java.util.BitSet
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class PieceStorageTest {
    private val dir: File = Files.createTempDirectory("arqon-storage").toFile()

    @AfterTest
    fun cleanup() {
        dir.deleteRecursively()
    }

    private fun multiFile() = TestTorrent.random("a.bin" to 20_000, "sub/b.bin" to 30_000, "c.bin" to 7_000, pieceLength = 16 * 1024)

    @Test
    fun piecesThatCrossFileBoundariesAreWrittenAndReadBackAcrossTheFiles() {
        val torrent = multiFile()
        PieceStorage(torrent.meta, dir).use { storage ->
            for (i in 0 until torrent.meta.pieceCount) storage.writePiece(i, torrent.piece(i))

            for (i in 0 until torrent.meta.pieceCount) assertContentEquals(torrent.piece(i), storage.read(i, 0, torrent.meta.pieceSize(i)))
            assertContentEquals(torrent.piece(1).copyOfRange(100, 5_000), storage.read(1, 100, 4_900))
        }
        assertContentEquals(torrent.files["sub/b.bin"], File(dir, "pack/sub/b.bin").readBytes())
    }

    @Test
    fun existingCorrectDataIsRecognisedAfterARestartAndCorruptDataIsNot() {
        val torrent = multiFile()
        torrent.writeTo(dir)
        File(dir, "pack/a.bin").let { f -> f.writeBytes(f.readBytes().also { it[10] = (it[10] + 1).toByte() }) }   // damages piece 0

        val have = PieceStorage(torrent.meta, dir).use { it.verifyExisting() }

        assertFalse(have[0], "the damaged piece must not count")
        assertTrue(have[2] && have[3], "untouched pieces are recognised")
    }

    @Test
    fun aMissingFileMeansItsPiecesAreNotThereAndNothingIsCreated() {
        val torrent = multiFile()
        torrent.writeTo(dir)
        File(dir, "pack/sub/b.bin").delete()

        val have = PieceStorage(torrent.meta, dir).use { it.verifyExisting() }

        assertEquals(listOf(0), have.stream().toArray().toList(), "only the piece made of a.bin alone is still complete")
        assertFalse(File(dir, "pack/sub/b.bin").exists(), "checking must never create files")
    }

    @Test
    fun onlyTheWantedPiecesAreChecked() {
        val torrent = multiFile()
        torrent.writeTo(dir)
        val wanted = BitSet().apply { set(0) }

        val have = PieceStorage(torrent.meta, dir).use { it.verifyExisting(wanted) }

        assertEquals(listOf(0), have.stream().toArray().toList())
    }

    @Test
    fun readingAPieceThatWasNeverWrittenGivesNull() {
        val torrent = multiFile()
        PieceStorage(torrent.meta, dir).use { storage ->
            storage.writePiece(0, torrent.piece(0))
            assertNull(storage.read(3, 0, torrent.meta.pieceSize(3)))
        }
    }
}
