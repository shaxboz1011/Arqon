package app.arqon.core.vpn

import app.arqon.core.net.LinkKind

/** What the chooser knows about one link: the answer time of a small connection test, and whether it costs mobile data. */
class LinkCandidate(val id: String, val kind: LinkKind, val metered: Boolean, val pingMillis: Int?)

/**
 * Picks the link the game's traffic travels over: the one with the shortest answer time, with a small preference for links that do not
 * cost mobile data. A game connection breaks when its address changes, so the choice is sticky: it is left only when the current
 * link stops answering, or when another one is clearly better for several checks in a row.
 */
class GameLinkChooser(private val checksBeforeSwitch: Int = 3) {
    private var current: String? = null
    private var challenger: String? = null
    private var votes = 0

    /** The id of the link to use now, or null when there is none. Call it each time the pings are refreshed. */
    @Synchronized
    fun choose(candidates: List<LinkCandidate>): String? {
        if (candidates.isEmpty()) {
            current = null
            return null
        }
        val answering = candidates.filter { it.pingMillis != null }
        val pool = answering.ifEmpty { candidates }
        val best = pool.minBy(::cost)
        val held = pool.firstOrNull { it.id == current }
        when {
            held == null -> switchTo(best.id)   // nothing chosen yet, or the chosen link went quiet
            best.id == held.id -> forget()
            cost(best) + maxOf(15.0, cost(held) * 0.3) < cost(held) -> {
                if (challenger == best.id) votes++ else { challenger = best.id; votes = 1 }
                if (votes >= checksBeforeSwitch) switchTo(best.id)
            }
            else -> forget()
        }
        return current
    }

    /** The id chosen last (without looking again). */
    @Synchronized
    fun current(): String? = current

    private fun switchTo(id: String) {
        current = id
        forget()
    }

    private fun forget() {
        challenger = null
        votes = 0
    }

    private fun cost(c: LinkCandidate): Double =
        (c.pingMillis ?: 1000) + (if (c.metered) 60.0 else 0.0) + (if (c.kind == LinkKind.CELLULAR) 10.0 else 0.0)
}
