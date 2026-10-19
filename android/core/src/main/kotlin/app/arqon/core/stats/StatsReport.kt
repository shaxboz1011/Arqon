package app.arqon.core.stats

/**
 * The anonymous usage report, only ever sent when the user agreed. It is built here, in one place, so that what leaves the phone is
 * exactly this and can be read in the open: a random installation number, the app and Android versions, the phone model, the language,
 * which connections the phone could give to apps, and how many times each feature was used on one day. No links, file names,
 * addresses, locations, SIM or phone numbers, and nothing about what was downloaded.
 */
object StatsReport {
    fun json(
        installId: String,
        appVersion: String,
        platform: String,
        osVersion: String,
        device: String,
        language: String,
        day: String,
        counts: Map<String, Int>,
        connections: List<String>,
    ): String = buildString {
        append('{')
        field("id", installId); append(',')
        field("app", appVersion); append(',')
        field("platform", platform); append(',')
        field("os", osVersion); append(',')
        field("device", device); append(',')
        field("lang", language); append(',')
        field("day", day); append(',')
        append("\"connections\":[").append(connections.sorted().joinToString(",") { quote(it) }).append("],")
        append("\"features\":{").append(counts.toSortedMap().entries.joinToString(",") { quote(it.key) + ":" + it.value }).append('}')
        append('}')
    }

    private fun StringBuilder.field(name: String, value: String) {
        append(quote(name)).append(':').append(quote(value))
    }

    fun quote(text: String): String = buildString {
        append('"')
        for (c in text) when {
            c == '"' -> append("\\\"")
            c == '\\' -> append("\\\\")
            c == '\n' -> append("\\n")
            c == '\r' -> append("\\r")
            c == '\t' -> append("\\t")
            c < ' ' -> append("\\u%04x".format(c.code))
            else -> append(c)
        }
        append('"')
    }
}
