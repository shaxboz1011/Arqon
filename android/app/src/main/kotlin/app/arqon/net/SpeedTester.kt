package app.arqon.net

import app.arqon.core.net.Link
import app.arqon.core.speed.SpeedTest
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

/** What the speed-test card shows. Speeds are in Mbit/s. */
data class TestState(
    /** Which test is running now: a link name, or null for the combined run; [running] tells whether anything runs at all. */
    val running: Boolean = false,
    val step: String? = null,
    val names: Map<String, String> = emptyMap(),
    val separate: Map<String, Double> = emptyMap(),
    val combined: Double? = null,
    val combinedPerLink: Map<String, Double> = emptyMap(),
    val errors: Map<String, String> = emptyMap(),
) {
    /** How many times faster all links together were than the best one alone, or null while either number is missing. */
    val gain: Double? get() = combined?.let { SpeedTest.gain(it, separate).takeIf { g -> g > 0 } }
}

/**
 * Runs the speed tests of the Panel: every link alone (one after the other, so they do not disturb each other), then all links
 * at the same moment. The two numbers side by side show what combining gives on this phone and this network.
 */
class SpeedTester(private val links: AndroidLinkManager, private val scope: CoroutineScope) {
    private val _state = MutableStateFlow(TestState())
    val state: StateFlow<TestState> = _state
    private var job: Job? = null

    private fun candidates(): List<Pair<Link, String>> =
        links.all.value.filter { it.state == LinkState.ACTIVE && it.link != null }.map { it.link!! to it.name }

    fun runSeparately() = start(separate = true, combined = false)

    fun runCombined() = start(separate = false, combined = true)

    fun runBoth() = start(separate = true, combined = true)

    fun cancel() {
        job?.cancel()
        _state.value = _state.value.copy(running = false, step = null)
    }

    private fun start(separate: Boolean, combined: Boolean) {
        if (_state.value.running) return
        app.arqon.stats.Features.use("speed_test")
        val list = candidates()
        if (list.isEmpty()) return
        val names = list.associate { it.first.id to it.second }
        // a new run replaces what was shown before, except what a "combined only" run can still be compared with
        _state.value = TestState(running = true, names = names, separate = if (separate) emptyMap() else _state.value.separate,
            combined = if (combined) null else _state.value.combined)
        job = scope.launch {
            try {
                if (separate) {
                    val speeds = LinkedHashMap<String, Double>()
                    val errors = HashMap<String, String>()
                    for ((link, name) in list) {
                        _state.value = _state.value.copy(step = name)
                        val result = SpeedTest.measure(listOf(link), URL, SECONDS)
                        speeds[link.id] = result.megabitsPerSecond(link.id)
                        result.errors[link.id]?.let { errors[link.id] = it }
                        _state.value = _state.value.copy(separate = speeds.toMap(), errors = errors.toMap())
                    }
                }
                if (combined) {
                    _state.value = _state.value.copy(step = null)
                    val result = SpeedTest.measure(list.map { it.first }, URL, SECONDS)
                    _state.value = _state.value.copy(
                        combined = result.totalMegabitsPerSecond,
                        combinedPerLink = list.associate { (l, _) -> l.id to result.megabitsPerSecond(l.id) },
                        errors = _state.value.errors + result.errors,
                    )
                }
            } finally {
                _state.value = _state.value.copy(running = false, step = null)
            }
        }
    }

    companion object {
        private const val SECONDS = 6
        private const val URL = "https://speed.cloudflare.com/__down?bytes=200000000"
    }
}
