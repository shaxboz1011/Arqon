package app.arqon.core.speed

import app.arqon.core.http.TestRangeServer
import app.arqon.core.net.Link
import app.arqon.core.net.LinkKind
import kotlinx.coroutines.runBlocking
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class SpeedTestTest {
    private val wifi = Link("wifi", "Wi-Fi", LinkKind.WIFI)
    private val sim = Link("sim1", "SIM 1", LinkKind.CELLULAR)

    @Test
    fun aLinkIsMeasuredByWhatItDownloadsInTheTimeGiven() = runBlocking {
        TestRangeServer(TestRangeServer.randomPayload(2_000_000)).use { server ->
            val result = SpeedTest.measure(listOf(wifi), server.url, seconds = 1, connectionsPerLink = 2)

            val measured = result.bytes.getValue("wifi")
            assertTrue(measured > 1_000_000, "something must have been downloaded: $measured")
            assertTrue(result.seconds in 0.8..3.0, "the test runs for about the time asked: ${result.seconds}")
            assertTrue(result.megabitsPerSecond("wifi") > 1.0)
        }
    }

    @Test
    fun linksTestedTogetherAreAddedUpAndEachKeepsItsOwnShare() = runBlocking {
        TestRangeServer(TestRangeServer.randomPayload(2_000_000)).use { server ->
            val result = SpeedTest.measure(listOf(wifi, sim), server.url, seconds = 1, connectionsPerLink = 2)

            assertEquals(setOf("wifi", "sim1"), result.bytes.keys)
            assertTrue(result.bytes.values.all { it > 0 })
            assertEquals(result.bytes.values.sum(), result.totalBytes)
            assertEquals(result.megabitsPerSecond("wifi") + result.megabitsPerSecond("sim1"), result.totalMegabitsPerSecond, 0.01)
        }
    }

    @Test
    fun aLinkThatFailsIsReportedWithZeroBytesInsteadOfBreakingTheTest() = runBlocking {
        val result = SpeedTest.measure(listOf(wifi), "http://127.0.0.1:1/nothing", seconds = 1, connectionsPerLink = 1)

        assertEquals(0L, result.bytes.getValue("wifi"))
        assertTrue(result.errors.containsKey("wifi"), "the reason is kept so it can be shown")
    }

    @Test
    fun theGainIsComparedWithTheBestSingleLink() {
        val separate = mapOf("wifi" to 15.0, "sim1" to 6.0)

        assertEquals(1.32, SpeedTest.gain(combined = 19.8, separate = separate), 0.001)
        assertEquals(1.0, SpeedTest.gain(combined = 15.0, separate = separate), 0.001)
        assertEquals(0.0, SpeedTest.gain(combined = 10.0, separate = emptyMap()), 0.001)
    }
}
