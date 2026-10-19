package app.arqon.core.torrent

import app.arqon.core.net.Link
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.net.URI
import java.nio.ByteBuffer
import java.util.concurrent.TimeUnit

class TrackerException(message: String, cause: Throwable? = null) : IOException(message, cause)

class AnnounceResult(val peers: List<Pair<String, Int>>, val intervalSeconds: Int)

enum class TrackerEvent(val udpCode: Int, val httpName: String?) {
    NONE(0, null), COMPLETED(1, "completed"), STARTED(2, "started"), STOPPED(3, "stopped"),
}

/** Asks a tracker for peers, over HTTP(S) or UDP (BEP 15), through a chosen [Link] so that every network can find its own peers. */
class TrackerClient(private val http: OkHttpClient = OkHttpClient(), private val timeoutMillis: Int = 6000) {
    suspend fun announce(
        url: String,
        link: Link?,
        infoHash: ByteArray,
        peerId: ByteArray,
        port: Int,
        uploaded: Long,
        downloaded: Long,
        left: Long,
        event: TrackerEvent,
    ): AnnounceResult = withContext(Dispatchers.IO) {
        when {
            url.startsWith("udp://", ignoreCase = true) -> udp(url, link, infoHash, peerId, port, uploaded, downloaded, left, event)
            url.startsWith("http://", ignoreCase = true) || url.startsWith("https://", ignoreCase = true) ->
                httpAnnounce(url, link, infoHash, peerId, port, uploaded, downloaded, left, event)
            else -> throw TrackerException("unsupported tracker address: $url")
        }
    }

    private fun httpAnnounce(url: String, link: Link?, infoHash: ByteArray, peerId: ByteArray, port: Int, uploaded: Long, downloaded: Long, left: Long, event: TrackerEvent): AnnounceResult {
        val query = buildString {
            append(if (url.contains('?')) '&' else '?')
            append("info_hash=").append(percent(infoHash))
            append("&peer_id=").append(percent(peerId))
            append("&port=").append(port).append("&uploaded=").append(uploaded).append("&downloaded=").append(downloaded)
            append("&left=").append(left).append("&compact=1&numwant=80")
            event.httpName?.let { append("&event=").append(it) }
        }
        val base = (link?.httpClient(http) ?: http).newBuilder()
            .callTimeout(timeoutMillis.toLong(), TimeUnit.MILLISECONDS).build()
        val bytes = try {
            base.newCall(Request.Builder().url(url + query).build()).execute().use { response ->
                if (!response.isSuccessful) throw TrackerException("the tracker answered ${response.code}")
                (response.body ?: throw TrackerException("the tracker sent nothing")).bytes()
            }
        } catch (e: TrackerException) {
            throw e
        } catch (e: IOException) {
            throw TrackerException("could not reach the tracker: ${e.message}", e)
        }
        val root = try { Bencode.decode(bytes) as? Map<*, *> } catch (e: BencodeException) { null } ?: throw TrackerException("the tracker sent something unreadable")
        (root["failure reason"] as? ByteArray)?.let { throw TrackerException("the tracker refused: ${String(it)}") }
        val interval = (root["interval"] as? Long)?.toInt() ?: 1800
        val peers = when (val p = root["peers"]) {
            is ByteArray -> Extensions.compactPeers(p)
            is List<*> -> p.mapNotNull {
                val d = it as? Map<*, *> ?: return@mapNotNull null
                val host = (d["ip"] as? ByteArray)?.let { ip -> String(ip) } ?: return@mapNotNull null
                val portNumber = (d["port"] as? Long)?.toInt() ?: return@mapNotNull null
                host to portNumber
            }
            else -> emptyList()
        }
        return AnnounceResult(peers, interval)
    }

    private fun udp(url: String, link: Link?, infoHash: ByteArray, peerId: ByteArray, port: Int, uploaded: Long, downloaded: Long, left: Long, event: TrackerEvent): AnnounceResult {
        val uri = try { URI(url) } catch (e: Exception) { throw TrackerException("bad tracker address: $url") }
        val host = uri.host ?: throw TrackerException("bad tracker address: $url")
        val address = try {
            (link?.resolver?.invoke(host) ?: InetAddress.getAllByName(host).toList()).firstOrNull { it.address.size == 4 }
        } catch (e: IOException) {
            throw TrackerException("could not find the tracker $host", e)
        } ?: throw TrackerException("the tracker $host has no IPv4 address")
        val target = InetSocketAddress(address, if (uri.port > 0) uri.port else 80)

        DatagramSocket().use { socket ->
            link?.bindDatagram?.invoke(socket)
            socket.soTimeout = timeoutMillis
            // connect: protects the tracker from forged addresses
            val transaction = (Math.random() * Int.MAX_VALUE).toInt()
            val connect = ByteBuffer.allocate(16).putLong(0x41727101980L).putInt(0).putInt(transaction).array()
            val reply = exchange(socket, target, connect, 16)
            val buffer = ByteBuffer.wrap(reply)
            if (buffer.int != 0 || buffer.int != transaction) throw TrackerException("the tracker answered the wrong thing")
            val connectionId = buffer.long

            val transaction2 = (Math.random() * Int.MAX_VALUE).toInt()
            val announce = ByteBuffer.allocate(98)
                .putLong(connectionId).putInt(1).putInt(transaction2)
                .put(infoHash).put(peerId)
                .putLong(downloaded).putLong(left).putLong(uploaded)
                .putInt(event.udpCode).putInt(0).putInt((Math.random() * Int.MAX_VALUE).toInt()).putInt(-1).putShort(port.toShort())
                .array()
            val answer = exchange(socket, target, announce, 20)
            val a = ByteBuffer.wrap(answer)
            val action = a.int
            if (a.int != transaction2) throw TrackerException("the tracker answered the wrong thing")
            if (action == 3) throw TrackerException("the tracker refused: " + String(answer, 8, answer.size - 8))
            if (action != 1) throw TrackerException("unexpected tracker answer")
            val interval = a.int
            a.int
            a.int
            val peerBytes = ByteArray(a.remaining()).also { a.get(it) }
            return AnnounceResult(Extensions.compactPeers(peerBytes), interval)
        }
    }

    private fun exchange(socket: DatagramSocket, target: InetSocketAddress, request: ByteArray, minReply: Int): ByteArray {
        var lastError: Exception? = null
        repeat(2) {
            try {
                socket.send(DatagramPacket(request, request.size, target))
                val buffer = ByteArray(2048)
                val packet = DatagramPacket(buffer, buffer.size)
                socket.receive(packet)
                if (packet.length >= minReply || packet.length >= 8) return buffer.copyOf(packet.length)
            } catch (e: SocketTimeoutException) {
                lastError = e
            } catch (e: IOException) {
                throw TrackerException("could not talk to the tracker: ${e.message}", e)
            }
        }
        throw TrackerException("the tracker did not answer", lastError)
    }

    private fun percent(bytes: ByteArray) = bytes.joinToString("") { "%%%02X".format(it) }
}
