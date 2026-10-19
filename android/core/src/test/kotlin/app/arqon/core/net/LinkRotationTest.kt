package app.arqon.core.net

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class LinkRotationTest {
    private val wifi = Link("wifi", "Wi-Fi", LinkKind.WIFI)
    private val sim1 = Link("sim1", "SIM 1", LinkKind.CELLULAR)
    private val sim2 = Link("sim2", "SIM 2", LinkKind.CELLULAR)

    @Test
    fun newConnectionsTakeTheLinksInTurn() {
        val rotation = LinkRotation({ listOf(wifi, sim1, sim2) })

        val picked = (1..6).map { rotation.next()!!.id }

        assertEquals(listOf("wifi", "sim1", "sim2", "wifi", "sim1", "sim2"), picked)
        assertEquals(2, rotation.counts()["sim2"])
    }

    @Test
    fun aLinkThatIsDownIsSkippedWhileAnotherIsAlive() {
        val rotation = LinkRotation({ listOf(wifi, sim1) }, { it.id != "sim1" })

        assertEquals(listOf("wifi", "wifi", "wifi"), (1..3).map { rotation.next()!!.id })
    }

    @Test
    fun whenEveryLinkLooksDownTheyAreStillUsed() {
        val rotation = LinkRotation({ listOf(wifi, sim1) }, { false })

        assertEquals(listOf("wifi", "sim1"), listOf(rotation.next()!!.id, rotation.next()!!.id))
    }

    @Test
    fun noLinksGivesNull() {
        assertNull(LinkRotation({ emptyList() }).next())
    }

    @Test
    fun linksAppearingLaterAreUsedFromTheNextConnection() {
        val current = mutableListOf(wifi)
        val rotation = LinkRotation({ current.toList() })
        rotation.next()

        current += sim1

        val ids = (1..4).map { rotation.next()!!.id }.toSet()
        assertEquals(setOf("wifi", "sim1"), ids)
    }
}
