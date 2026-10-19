package app.arqon.core.torrent

import java.io.File
import java.security.MessageDigest

/** Builds small torrents for tests: the payload files, the .torrent bytes and the parsed description. */
class TestTorrent(val files: Map<String, ByteArray>, val name: String = "pack", val pieceLength: Int = 16 * 1024, trackers: List<String> = emptyList()) {
    val isMultiFile = files.size > 1 || files.keys.first().contains('/')
    val data: ByteArray = files.values.fold(ByteArray(0)) { a, b -> a + b }
    val torrentBytes: ByteArray
    val meta: MetaInfo

    init {
        val pieces = ByteArrayBuilder()
        val sha = MessageDigest.getInstance("SHA-1")
        var offset = 0
        while (offset < data.size) {
            val end = minOf(offset + pieceLength, data.size)
            pieces.add(sha.digest(data.copyOfRange(offset, end)))
            offset = end
        }
        val info = if (isMultiFile) {
            mapOf(
                "name" to name, "piece length" to pieceLength.toLong(), "pieces" to pieces.toByteArray(),
                "files" to files.map { (path, bytes) -> mapOf("length" to bytes.size.toLong(), "path" to path.split('/')) },
            )
        } else {
            mapOf("name" to files.keys.first(), "length" to data.size.toLong(), "piece length" to pieceLength.toLong(), "pieces" to pieces.toByteArray())
        }
        val root = mutableMapOf<String, Any>("info" to info)
        if (trackers.isNotEmpty()) root["announce"] = trackers.first()
        torrentBytes = Bencode.encode(root)
        meta = MetaInfo.parse(torrentBytes)
    }

    /** Writes the payload under [folder] exactly as a finished download would be. */
    fun writeTo(folder: File) {
        for ((path, bytes) in files) {
            val target = if (isMultiFile) File(folder, "$name/$path") else File(folder, path)
            target.parentFile.mkdirs()
            target.writeBytes(bytes)
        }
    }

    fun piece(index: Int): ByteArray = data.copyOfRange(index * pieceLength, minOf((index + 1) * pieceLength, data.size))

    companion object {
        fun random(vararg sizes: Pair<String, Int>, pieceLength: Int = 16 * 1024, seed: Long = 5, trackers: List<String> = emptyList()): TestTorrent {
            val random = java.util.Random(seed)
            return TestTorrent(sizes.associate { (name, size) -> name to ByteArray(size).also { random.nextBytes(it) } }, pieceLength = pieceLength, trackers = trackers)
        }
    }
}

private class ByteArrayBuilder {
    private val out = java.io.ByteArrayOutputStream()
    fun add(bytes: ByteArray) = out.write(bytes)
    fun toByteArray(): ByteArray = out.toByteArray()
}
