package app.arqon.core.http

import com.sun.net.httpserver.HttpExchange
import com.sun.net.httpserver.HttpServer
import java.net.InetSocketAddress
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong

/** A small HTTP server for tests: serves a payload, optionally with range support and a delay per request. */
class TestRangeServer(
    val payload: ByteArray,
    private val ranges: Boolean = true,
    private val delayMillis: Long = 0,
    private val refuse: IntRange = IntRange.EMPTY,
) : AutoCloseable {
    private val server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
    val bytesServed = AtomicLong()
    val requests = AtomicInteger()
    val url: String get() = "http://127.0.0.1:${server.address.port}/file.bin"

    init {
        server.executor = Executors.newFixedThreadPool(16)
        server.createContext("/file.bin") { handle(it) }
        server.start()
    }

    private fun handle(exchange: HttpExchange) {
        exchange.use {
            val number = requests.incrementAndGet()
            if (delayMillis > 0) Thread.sleep(delayMillis)
            if (number in refuse) {
                it.responseHeaders.add("Retry-After", "1")
                it.sendResponseHeaders(429, -1)
                return
            }
            val range = it.requestHeaders.getFirst("Range")
            if (ranges && range != null && range.startsWith("bytes=")) {
                val (from, to) = range.removePrefix("bytes=").split("-").let { p -> p[0].toLong() to p[1].toLong().coerceAtMost(payload.size - 1L) }
                val length = (to - from + 1).toInt()
                it.responseHeaders.add("Content-Range", "bytes $from-$to/${payload.size}")
                it.sendResponseHeaders(206, length.toLong())
                it.responseBody.write(payload, from.toInt(), length)
                bytesServed.addAndGet(length.toLong())
            } else {
                it.sendResponseHeaders(200, payload.size.toLong())
                it.responseBody.write(payload)
                bytesServed.addAndGet(payload.size.toLong())
            }
        }
    }

    override fun close() = server.stop(0)

    companion object {
        fun randomPayload(size: Int, seed: Long = 1): ByteArray = ByteArray(size).also { java.util.Random(seed).nextBytes(it) }
    }
}
