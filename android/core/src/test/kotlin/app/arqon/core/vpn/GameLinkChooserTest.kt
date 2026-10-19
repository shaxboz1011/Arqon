package app.arqon.core.vpn

import app.arqon.core.net.LinkKind
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class GameLinkChooserTest {
    private fun wifi(ping: Int?) = LinkCandidate("wifi", LinkKind.WIFI, false, ping)
    private fun sim(ping: Int?, metered: Boolean = true) = LinkCandidate("sim", LinkKind.CELLULAR, metered, ping)

    @Test
    fun `nothing to choose from gives nothing`() {
        assertNull(GameLinkChooser().choose(emptyList()))
    }

    @Test
    fun `the faster link wins at the start`() {
        assertEquals("wifi", GameLinkChooser().choose(listOf(wifi(20), sim(15))))   // the SIM costs data, so Wi-Fi is kept
        assertEquals("sim", GameLinkChooser().choose(listOf(wifi(200), sim(30))))
    }

    @Test
    fun `a link that does not answer is not chosen while another one does`() {
        assertEquals("sim", GameLinkChooser().choose(listOf(wifi(null), sim(80))))
    }

    @Test
    fun `when nothing answers a link is still chosen`() {
        assertEquals("wifi", GameLinkChooser().choose(listOf(wifi(null), sim(null))))
    }

    @Test
    fun `a small difference does not move the game`() {
        val chooser = GameLinkChooser()
        assertEquals("wifi", chooser.choose(listOf(wifi(40), sim(45, metered = false))))
        repeat(10) { assertEquals("wifi", chooser.choose(listOf(wifi(40), sim(20, metered = false)))) }
    }

    @Test
    fun `a clearly better link takes over only after several checks`() {
        val chooser = GameLinkChooser(checksBeforeSwitch = 3)
        assertEquals("wifi", chooser.choose(listOf(wifi(30), sim(60))))
        assertEquals("wifi", chooser.choose(listOf(wifi(300), sim(60))))
        assertEquals("wifi", chooser.choose(listOf(wifi(300), sim(60))))
        assertEquals("sim", chooser.choose(listOf(wifi(300), sim(60))))
    }

    @Test
    fun `one good check in between starts the count again`() {
        val chooser = GameLinkChooser(checksBeforeSwitch = 3)
        chooser.choose(listOf(wifi(30), sim(60)))
        chooser.choose(listOf(wifi(300), sim(60)))
        chooser.choose(listOf(wifi(300), sim(60)))
        assertEquals("wifi", chooser.choose(listOf(wifi(30), sim(60))))
        assertEquals("wifi", chooser.choose(listOf(wifi(300), sim(60))))
        assertEquals("wifi", chooser.choose(listOf(wifi(300), sim(60))))
    }

    @Test
    fun `a link that goes quiet is left at once`() {
        val chooser = GameLinkChooser()
        chooser.choose(listOf(wifi(20), sim(60)))
        assertEquals("sim", chooser.choose(listOf(wifi(null), sim(60))))
    }

    @Test
    fun `a link that disappeared is left at once`() {
        val chooser = GameLinkChooser()
        chooser.choose(listOf(wifi(20), sim(60)))
        assertEquals("sim", chooser.choose(listOf(sim(60))))
    }
}
