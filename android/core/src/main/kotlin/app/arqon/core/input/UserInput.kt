package app.arqon.core.input

import java.net.URI
import java.net.URLDecoder
import java.net.URLEncoder

enum class InputKind { MAGNET, TORRENT_URL, HTTP_URL, OTHER }

/** What the user types or pastes: a magnet link, a link to a .torrent file, a file link, or something for the search box. */
object UserInput {
    fun classify(text: String): InputKind {
        val t = text.trim()
        if (t.startsWith("magnet:", ignoreCase = true)) return InputKind.MAGNET
        if (!isHttp(t)) return InputKind.OTHER
        val path = t.substringBefore('#').substringBefore('?')
        return if (path.endsWith(".torrent", ignoreCase = true)) InputKind.TORRENT_URL else InputKind.HTTP_URL
    }

    private fun isHttp(t: String) = t.startsWith("http://", ignoreCase = true) || t.startsWith("https://", ignoreCase = true)

    /** What the browser's address box should open for [typed]: an address as it is, a host name with https, anything else as a search. */
    fun addressFor(typed: String): String {
        val t = typed.trim()
        if (t.isEmpty()) return "about:blank"
        if (isHttp(t) || t.startsWith("magnet:", ignoreCase = true) || t.startsWith("about:") || t.startsWith("file:")) return t
        val looksLikeHost = !t.contains(' ') && (t.contains('.') || t.startsWith("localhost")) && !t.startsWith(".") && !t.endsWith(".")
        if (looksLikeHost) {
            val hostPart = t.substringBefore('/').substringBefore('?')
            val isLocalNumber = hostPart.substringBefore(':').all { it.isDigit() || it == '.' }
            return (if (isLocalNumber || t.startsWith("localhost")) "http://" else "https://") + t
        }
        return "https://duckduckgo.com/?q=" + URLEncoder.encode(t, "UTF-8")
    }

    /** The name a downloaded file gets when the server does not say: the last part of the path, or "download". */
    fun fileNameFromUrl(url: String): String {
        val path = try { URI(url.substringBefore('#')).rawPath.orEmpty() } catch (_: Exception) { url.substringBefore('?').substringAfter("://").substringAfter('/') }
        val last = path.trimEnd('/').substringAfterLast('/')
        val decoded = try { URLDecoder.decode(last.replace("+", "%2B"), "UTF-8") } catch (_: Exception) { last }
        return decoded.replace('/', '_').replace('\\', '_').ifBlank { "download" }
    }
}
