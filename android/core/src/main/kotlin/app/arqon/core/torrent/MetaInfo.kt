package app.arqon.core.torrent

import java.security.MessageDigest

class TorrentFormatException(message: String) : Exception(message)

/** One file inside a torrent: [path] is relative to the download folder, [offset] is its place in the torrent's byte stream. */
data class TorrentFileEntry(val index: Int, val path: String, val length: Long, val offset: Long)

/** The description of a torrent: what it contains and the SHA-1 of every piece. */
class MetaInfo(
    val name: String,
    val infoHash: ByteArray,
    val pieceLength: Int,
    private val pieceHashes: ByteArray,
    val files: List<TorrentFileEntry>,
    val trackers: List<String>,
    val infoBytes: ByteArray,
) {
    val totalSize: Long = files.sumOf { it.length }
    val pieceCount: Int = pieceHashes.size / 20

    fun pieceHash(index: Int): ByteArray = pieceHashes.copyOfRange(index * 20, index * 20 + 20)

    /** Every piece has [pieceLength] bytes except the last one. */
    fun pieceSize(index: Int): Int =
        if (index == pieceCount - 1) (totalSize - pieceLength.toLong() * (pieceCount - 1)).toInt() else pieceLength

    companion object {
        fun parse(torrentBytes: ByteArray): MetaInfo {
            val root = try {
                Bencode.decode(torrentBytes)
            } catch (e: BencodeException) {
                throw TorrentFormatException("not a torrent file: ${e.message}")
            }
            if (root !is Map<*, *> || root["info"] !is Map<*, *>) throw TorrentFormatException("not a torrent file: no info section")
            val infoBytes = Bencode.rawValueOf(torrentBytes, "info") ?: throw TorrentFormatException("not a torrent file: no info section")

            val trackers = LinkedHashSet<String>()
            (root["announce"] as? ByteArray)?.let { trackers += String(it, Charsets.UTF_8) }
            (root["announce-list"] as? List<*>)?.forEach { tier ->
                (tier as? List<*>)?.forEach { (it as? ByteArray)?.let { url -> trackers += String(url, Charsets.UTF_8) } }
            }
            return fromInfo(infoBytes, trackers.toList())
        }

        /** Used for magnet links: the peers hand over just the info dictionary. */
        fun fromInfo(infoBytes: ByteArray, trackers: List<String>): MetaInfo {
            val info = try {
                Bencode.decode(infoBytes)
            } catch (e: BencodeException) {
                throw TorrentFormatException("bad torrent info: ${e.message}")
            }
            if (info !is Map<*, *>) throw TorrentFormatException("bad torrent info")

            val name = safeComponent(text(info["name"]) ?: throw TorrentFormatException("the torrent has no name"))
            val pieceLength = (info["piece length"] as? Long)?.takeIf { it in 1..Int.MAX_VALUE } ?: throw TorrentFormatException("bad piece length")
            val pieces = info["pieces"] as? ByteArray
            if (pieces == null || pieces.isEmpty() || pieces.size % 20 != 0) throw TorrentFormatException("bad piece hashes")

            val files = ArrayList<TorrentFileEntry>()
            var offset = 0L
            val list = info["files"]
            if (list is List<*>) {
                for ((i, f) in list.withIndex()) {
                    val entry = f as? Map<*, *> ?: throw TorrentFormatException("bad file entry")
                    val length = (entry["length"] as? Long)?.takeIf { it >= 0 } ?: throw TorrentFormatException("bad file length")
                    val parts = (entry["path"] as? List<*>)?.map { safeComponent(text(it) ?: throw TorrentFormatException("bad file path")) }
                    if (parts.isNullOrEmpty()) throw TorrentFormatException("bad file path")
                    files += TorrentFileEntry(i, name + "/" + parts.joinToString("/"), length, offset)
                    offset += length
                }
                if (files.isEmpty()) throw TorrentFormatException("the torrent has no files")
            } else {
                val length = (info["length"] as? Long)?.takeIf { it >= 0 } ?: throw TorrentFormatException("bad file length")
                files += TorrentFileEntry(0, name, length, 0)
                offset = length
            }

            val expectedPieces = (offset + pieceLength - 1) / pieceLength
            if (expectedPieces != (pieces.size / 20).toLong()) throw TorrentFormatException("the number of piece hashes does not match the size")

            val hash = MessageDigest.getInstance("SHA-1").digest(infoBytes)
            return MetaInfo(name, hash, pieceLength.toInt(), pieces, files, trackers, infoBytes)
        }

        private fun text(value: Any?): String? = (value as? ByteArray)?.let { String(it, Charsets.UTF_8) }

        /** A name taken from a torrent becomes part of a path, so it must not be able to climb out of the download folder. */
        private fun safeComponent(part: String): String {
            if (part.isEmpty() || part == "." || part == ".." || part.contains('/') || part.contains('\\') || part.contains('\u0000')) {
                throw TorrentFormatException("unsafe file name: $part")
            }
            return part
        }
    }
}
