package app.arqon.core.speed

import app.arqon.core.net.Link
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong

/** What one test run measured: bytes received through each link in [seconds]. */
class SpeedResult(val bytes: Map<String, Long>, val seconds: Double, val errors: Map<String, String>) {
    val totalBytes: Long get() = bytes.values.sum()

    fun megabitsPerSecond(linkId: String): Double = (bytes[linkId] ?: 0L) * 8 / seconds / 1_000_000

    val totalMegabitsPerSecond: Double get() = totalBytes * 8 / seconds / 1_000_000
}

/**
 * Measures how fast links really download: a few connections per link read from the same test file for a fixed time, and the
 * bytes are counted per link. Run alone, a link's number is its own speed; run together, the total shows what combining gives.
 */
object SpeedTest {
    suspend fun measure(
        links: List<Link>,
        url: String,
        seconds: Int,
        connectionsPerLink: Int = 3,
        client: OkHttpClient = OkHttpClient(),
        onProgress: (Map<String, Long>) -> Unit = {},
    ): SpeedResult = coroutineScope {
        val counters = ConcurrentHashMap<String, AtomicLong>().also { map -> links.forEach { map[it.id] = AtomicLong() } }
        val errors = ConcurrentHashMap<String, String>()
        val started = System.nanoTime()
        val deadline = started + seconds * 1_000_000_000L

        val workers = ArrayList<kotlinx.coroutines.Job>()
        for (link in links) {
            val http = link.httpClient(client)
            repeat(connectionsPerLink) {
                workers += launch(Dispatchers.IO) {
                    while (System.nanoTime() < deadline) {
                        val call = http.newCall(Request.Builder().url(url).build())
                        val stopper = launch {
                            delay(((deadline - System.nanoTime()) / 1_000_000).coerceAtLeast(1))
                            call.cancel()
                        }
                        try {
                            call.execute().use { response ->
                                if (!response.isSuccessful) { errors[link.id] = "HTTP ${response.code}"; return@launch }
                                val source = (response.body ?: return@use).source()
                                val buffer = ByteArray(64 * 1024)
                                while (System.nanoTime() < deadline) {
                                    val n = source.read(buffer)
                                    if (n < 0) break
                                    counters.getValue(link.id).addAndGet(n.toLong())
                                }
                            }
                        } catch (e: CancellationException) {
                            throw e
                        } catch (e: Exception) {
                            if (System.nanoTime() < deadline) {
                                errors[link.id] = "${e.javaClass.simpleName}: ${e.message}"
                                return@launch
                            }
                        } finally {
                            stopper.cancel()
                        }
                    }
                }
            }
        }
        launch { while (System.nanoTime() < deadline) { delay(500); onProgress(counters.mapValues { it.value.get() }) } }
        workers.forEach { it.join() }
        val elapsed = (System.nanoTime() - started) / 1e9
        SpeedResult(counters.mapValues { it.value.get() }, elapsed, errors.toMap())
    }

    /** How many times faster the combined run was than the best link alone (1.0: no gain); 0 when there is nothing to compare with. */
    fun gain(combined: Double, separate: Map<String, Double>): Double {
        val best = separate.values.maxOrNull() ?: return 0.0
        return if (best <= 0.0) 0.0 else combined / best
    }
}
