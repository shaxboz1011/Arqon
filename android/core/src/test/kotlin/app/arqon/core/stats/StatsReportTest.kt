package app.arqon.core.stats

import kotlin.test.Test
import kotlin.test.assertEquals

class StatsReportTest {
    @Test
    fun `the report has exactly the documented fields`() {
        val json = StatsReport.json(
            installId = "abc-123", appVersion = "0.2.0", platform = "android", osVersion = "15 (API 35)", device = "samsung SM-S928B",
            language = "uz", day = "2026-10-19",
            counts = mapOf("speed_test" to 2, "download_torrent" to 1),
            connections = listOf("wifi:active", "sim:active"),
        )
        assertEquals(
            """{"id":"abc-123","app":"0.2.0","platform":"android","os":"15 (API 35)","device":"samsung SM-S928B","lang":"uz","day":"2026-10-19",""" +
                """"connections":["sim:active","wifi:active"],"features":{"download_torrent":1,"speed_test":2}}""",
            json,
        )
    }

    @Test
    fun `an empty report is still valid`() {
        val json = StatsReport.json("i", "v", "android", "o", "d", "en", "2026-01-01", emptyMap(), emptyList())
        assertEquals("""{"id":"i","app":"v","platform":"android","os":"o","device":"d","lang":"en","day":"2026-01-01","connections":[],"features":{}}""", json)
    }

    @Test
    fun `quotes and control characters are escaped`() {
        assertEquals("\"a\\\"b\\\\c\\nd\\u0001\"", StatsReport.quote("a\"b\\c\nd\u0001"))
    }
}
