package app.arqon.core.torrent

import java.io.File
import java.io.IOException
import java.io.RandomAccessFile
import java.security.MessageDigest
import java.util.BitSet

/**
 * Where a torrent's bytes live on disk. A piece may span several files; this class hides that. Files are created when the first
 * byte is written, so a torrent never leaves empty files behind for parts that were not chosen.
 */
class PieceStorage(private val meta: MetaInfo, private val root: File) : AutoCloseable {
    private val open = HashMap<Int, RandomAccessFile>()

    /** A piece cut along file boundaries: [length] bytes of file [file] from [fileOffset], placed at [pieceOffset] in the piece. */
    private class Segment(val file: TorrentFileEntry, val fileOffset: Long, val pieceOffset: Int, val length: Int)

    private fun segments(piece: Int, begin: Int, length: Int): List<Segment> {
        val result = ArrayList<Segment>()
        var position = piece.toLong() * meta.pieceLength + begin
        var remaining = length
        var pieceOffset = begin
        for (file in meta.files) {
            if (remaining == 0) break
            val end = file.offset + file.length
            if (position >= end || file.length == 0L) continue
            val inFile = position - file.offset
            val take = minOf(remaining.toLong(), file.length - inFile).toInt()
            result += Segment(file, inFile, pieceOffset, take)
            position += take
            pieceOffset += take
            remaining -= take
        }
        return result
    }

    fun fileFor(entry: TorrentFileEntry): File = File(root, entry.path)

    @Synchronized
    private fun handle(entry: TorrentFileEntry, forWriting: Boolean): RandomAccessFile? {
        open[entry.index]?.let { return it }
        val file = fileFor(entry)
        if (!forWriting && !file.exists()) return null
        file.parentFile?.mkdirs()
        return RandomAccessFile(file, "rw").also { open[entry.index] = it }
    }

    /** Stores a whole piece; the caller has already checked its hash. */
    fun writePiece(piece: Int, data: ByteArray) {
        require(data.size == meta.pieceSize(piece)) { "piece $piece has the wrong size" }
        for (s in segments(piece, 0, data.size)) {
            val raf = handle(s.file, forWriting = true)!!
            synchronized(raf) {
                raf.seek(s.fileOffset)
                raf.write(data, s.pieceOffset, s.length)
            }
        }
    }

    /** Reads [length] bytes of a piece, or null when the file holding them is missing or too short (it was never downloaded). */
    fun read(piece: Int, begin: Int, length: Int): ByteArray? {
        val out = ByteArray(length)
        for (s in segments(piece, begin, length)) {
            val raf = handle(s.file, forWriting = false) ?: return null
            synchronized(raf) {
                if (raf.length() < s.fileOffset + s.length) return null
                raf.seek(s.fileOffset)
                raf.readFully(out, s.pieceOffset - begin, s.length)
            }
        }
        return out
    }

    /** Hashes what is already on disk and tells which pieces are complete and correct (resume, and seeding after a restart). */
    fun verifyExisting(wanted: BitSet? = null, onProgress: (checked: Int, total: Int) -> Unit = { _, _ -> }): BitSet {
        val have = BitSet(meta.pieceCount)
        val sha = MessageDigest.getInstance("SHA-1")
        for (piece in 0 until meta.pieceCount) {
            if (wanted == null || wanted[piece]) {
                val data = try { read(piece, 0, meta.pieceSize(piece)) } catch (_: IOException) { null }
                if (data != null && sha.digest(data).contentEquals(meta.pieceHash(piece))) have.set(piece)
            }
            onProgress(piece + 1, meta.pieceCount)
        }
        return have
    }

    @Synchronized
    override fun close() {
        open.values.forEach { try { it.close() } catch (_: IOException) { } }
        open.clear()
    }

    companion object {
        fun sha1(data: ByteArray): ByteArray = MessageDigest.getInstance("SHA-1").digest(data)
    }
}
