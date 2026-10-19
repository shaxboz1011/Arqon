package app.arqon.core.torrent

import java.io.ByteArrayOutputStream

class BencodeException(message: String) : Exception(message)

/**
 * Bencode, the encoding of .torrent files and tracker answers. A value is a Long, a ByteArray, a List or a Map; dictionary
 * keys are kept as ISO-8859-1 strings so that every byte survives and the order of keys is the byte order the format asks for.
 */
object Bencode {
    fun decode(bytes: ByteArray): Any {
        val parser = Parser(bytes)
        val value = parser.value()
        if (parser.pos != bytes.size) throw BencodeException("unexpected data after the value")
        return value
    }

    /** Reads one value from the start of [bytes] and says how many bytes it used (the rest may be binary data that follows it). */
    fun decodeFirst(bytes: ByteArray): Pair<Any, Int> {
        val parser = Parser(bytes)
        val value = parser.value()
        return value to parser.pos
    }

    /**
     * The exact bytes of the value stored under [key] in the top-level dictionary. The info hash of a torrent is the SHA-1 of
     * these bytes, so they must not be re-encoded.
     */
    fun rawValueOf(bytes: ByteArray, key: String): ByteArray? {
        val parser = Parser(bytes)
        if (parser.peek() != 'd'.code) throw BencodeException("a dictionary was expected")
        parser.pos++
        while (parser.peek() != 'e'.code) {
            val name = String(parser.string(), Charsets.ISO_8859_1)
            val start = parser.pos
            parser.value()
            if (name == key) return bytes.copyOfRange(start, parser.pos)
        }
        return null
    }

    fun encode(value: Any): ByteArray = ByteArrayOutputStream().also { write(value, it) }.toByteArray()

    private fun write(value: Any, out: ByteArrayOutputStream) {
        when (value) {
            is Long -> out.write("i${value}e".toByteArray())
            is Int -> out.write("i${value}e".toByteArray())
            is ByteArray -> { out.write("${value.size}:".toByteArray()); out.write(value) }
            is String -> write(value.toByteArray(Charsets.UTF_8), out)
            is List<*> -> { out.write('l'.code); value.forEach { write(it!!, out) }; out.write('e'.code) }
            is Map<*, *> -> {
                out.write('d'.code)
                for (key in value.keys.map { it as String }.sorted()) {
                    write(key.toByteArray(Charsets.ISO_8859_1), out)
                    write(value[key]!!, out)
                }
                out.write('e'.code)
            }
            else -> throw BencodeException("cannot encode ${value::class.simpleName}")
        }
    }

    private class Parser(val data: ByteArray) {
        var pos = 0

        private fun indexOf(byte: Byte, from: Int): Int {
            for (i in from until data.size) if (data[i] == byte) return i
            return -1
        }

        fun peek(): Int = if (pos < data.size) data[pos].toInt() and 0xFF else throw BencodeException("unexpected end of data")

        fun value(): Any = when (val c = peek().toChar()) {
            'i' -> integer()
            'l' -> {
                pos++
                val list = ArrayList<Any>()
                while (peek() != 'e'.code) list.add(value())
                pos++
                list
            }
            'd' -> {
                pos++
                val map = LinkedHashMap<String, Any>()
                while (peek() != 'e'.code) {
                    val key = String(string(), Charsets.ISO_8859_1)
                    map[key] = value()
                }
                pos++
                map
            }
            in '0'..'9' -> string()
            else -> throw BencodeException("unexpected character '$c' at $pos")
        }

        private fun integer(): Long {
            pos++
            val end = indexOf('e'.code.toByte(), pos)
            if (end < 0) throw BencodeException("integer is not closed")
            val text = String(data, pos, end - pos, Charsets.ISO_8859_1)
            pos = end + 1
            return text.toLongOrNull() ?: throw BencodeException("not a number: $text")
        }

        fun string(): ByteArray {
            val colon = indexOf(':'.code.toByte(), pos)
            if (colon < 0) throw BencodeException("string length is not followed by ':'")
            val length = String(data, pos, colon - pos, Charsets.ISO_8859_1).toIntOrNull()
                ?: throw BencodeException("bad string length at $pos")
            if (length < 0 || colon + 1 + length > data.size) throw BencodeException("string is longer than the data")
            val result = data.copyOfRange(colon + 1, colon + 1 + length)
            pos = colon + 1 + length
            return result
        }
    }
}
