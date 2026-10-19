package app.arqon.core.torrent

import java.net.URLDecoder

/** A parsed magnet link: the info hash identifies the torrent, the rest helps to find peers. */
class Magnet(val infoHash: ByteArray, val name: String?, val trackers: List<String>, val peers: List<Pair<String, Int>>) {
    val infoHashHex: String get() = infoHash.joinToString("") { "%02x".format(it) }

    companion object {
        fun parse(text: String): Magnet? {
            val trimmed = text.trim()
            if (!trimmed.startsWith("magnet:", ignoreCase = true)) return null
            val query = trimmed.substringAfter('?', "")
            if (query.isEmpty()) return null

            var hash: ByteArray? = null
            var name: String? = null
            val trackers = ArrayList<String>()
            val peers = ArrayList<Pair<String, Int>>()
            for (pair in query.split('&')) {
                val key = pair.substringBefore('=')
                val value = decode(pair.substringAfter('=', ""))
                when (key.lowercase()) {
                    "xt" -> if (hash == null && value.startsWith("urn:btih:", ignoreCase = true)) hash = hashOf(value.substring(9))
                    "dn" -> name = value.ifBlank { null }
                    "tr" -> if (value.isNotBlank()) trackers += value
                    "x.pe" -> parsePeer(value)?.let { peers += it }
                }
            }
            return Magnet(hash ?: return null, name, trackers, peers)
        }

        private fun decode(value: String) = try {
            URLDecoder.decode(value, "UTF-8")
        } catch (_: IllegalArgumentException) {
            value
        }

        private fun hashOf(text: String): ByteArray? = when (text.length) {
            40 -> if (text.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }) {
                ByteArray(20) { text.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
            } else {
                null
            }
            32 -> Base32.decode(text)
            else -> null
        }

        private fun parsePeer(value: String): Pair<String, Int>? {
            val host: String
            val portText: String
            if (value.startsWith("[")) {
                host = value.substringAfter('[').substringBefore(']')
                portText = value.substringAfterLast("]:", "")
            } else {
                host = value.substringBeforeLast(':', "")
                portText = value.substringAfterLast(':', "")
            }
            val port = portText.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
            return if (host.isBlank()) null else host to port
        }
    }
}

/** RFC 4648 base32 without padding: older magnet links write the info hash this way. */
object Base32 {
    private const val ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"

    fun encode(bytes: ByteArray): String {
        val out = StringBuilder()
        var buffer = 0
        var bits = 0
        for (b in bytes) {
            buffer = (buffer shl 8) or (b.toInt() and 0xFF)
            bits += 8
            while (bits >= 5) {
                out.append(ALPHABET[(buffer shr (bits - 5)) and 31])
                bits -= 5
            }
        }
        if (bits > 0) out.append(ALPHABET[(buffer shl (5 - bits)) and 31])
        return out.toString()
    }

    fun decode(text: String): ByteArray? {
        val out = java.io.ByteArrayOutputStream()
        var buffer = 0
        var bits = 0
        for (c in text.uppercase()) {
            val v = ALPHABET.indexOf(c)
            if (v < 0) return null
            buffer = (buffer shl 5) or v
            bits += 5
            if (bits >= 8) {
                out.write((buffer shr (bits - 8)) and 0xFF)
                bits -= 8
            }
        }
        return out.toByteArray().takeIf { it.size == 20 }
    }
}
