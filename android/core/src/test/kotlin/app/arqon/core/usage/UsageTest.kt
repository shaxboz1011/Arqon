package app.arqon.core.usage

import java.time.LocalDate
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class UsageTest {
    @Test
    fun `traffic adds up per day and medium`() {
        val ledger = UsageLedger()
        ledger.add("2026-10-19", Medium.WIFI, 100, 10)
        ledger.add("2026-10-19", Medium.WIFI, 50, 5)
        ledger.add("2026-10-19", Medium.MOBILE, 7, 1)
        assertEquals(Traffic(150, 15), ledger.day("2026-10-19", Medium.WIFI))
        assertEquals(Traffic(7, 1), ledger.day("2026-10-19", Medium.MOBILE))
        assertEquals(Traffic(), ledger.day("2026-10-18", Medium.WIFI))
    }

    @Test
    fun `empty and negative amounts change nothing`() {
        val ledger = UsageLedger()
        ledger.add("2026-10-19", Medium.WIFI, 0, 0)
        ledger.add("2026-10-19", Medium.WIFI, -5, -5)
        assertTrue(ledger.isEmpty())
    }

    @Test
    fun `a month sums only its own days`() {
        val ledger = UsageLedger()
        ledger.add("2026-09-30", Medium.MOBILE, 1000, 0)
        ledger.add("2026-10-01", Medium.MOBILE, 10, 1)
        ledger.add("2026-10-31", Medium.MOBILE, 20, 2)
        ledger.add("2026-11-01", Medium.MOBILE, 5000, 0)
        ledger.add("2026-10-15", Medium.WIFI, 999, 0)
        assertEquals(Traffic(30, 3), ledger.month("2026-10", Medium.MOBILE))
    }

    @Test
    fun `the last days are listed oldest first with zeros for quiet days`() {
        val ledger = UsageLedger()
        ledger.add("2026-10-19", Medium.WIFI, 5, 5)
        ledger.add("2026-10-17", Medium.MOBILE, 9, 0)
        val week = ledger.lastDays(LocalDate.of(2026, 10, 19), 4)
        assertEquals(listOf("2026-10-16", "2026-10-17", "2026-10-18", "2026-10-19"), week.map { it.date.toString() })
        assertEquals(9L, week[1].mobile.rx)
        assertEquals(0L, week[2].total)
        assertEquals(10L, week[3].total)
    }

    @Test
    fun `the ledger survives being written and read`() {
        val ledger = UsageLedger()
        ledger.add("2026-10-19", Medium.WIFI, 123456789012, 3)
        ledger.add("2026-10-18", Medium.MOBILE, 1, 2)
        val back = UsageLedger.parse(ledger.serialize())
        assertEquals(ledger.serialize(), back.serialize())
        assertEquals(Traffic(123456789012, 3), back.day("2026-10-19", Medium.WIFI))
    }

    @Test
    fun `damaged lines are skipped when reading`() {
        val back = UsageLedger.parse("garbage\n2026-10-19,WIFI,5,6\n2026-10-19,XYZ,1,1\n2026-10-19,WIFI,x,1\n")
        assertEquals(Traffic(5, 6), back.day("2026-10-19", Medium.WIFI))
    }

    @Test
    fun `old days are forgotten`() {
        val ledger = UsageLedger()
        ledger.add("2024-01-01", Medium.WIFI, 5, 5)
        ledger.add("2026-10-01", Medium.WIFI, 5, 5)
        ledger.prune(LocalDate.of(2026, 10, 19), keep = 400)
        assertEquals(Traffic(), ledger.day("2024-01-01", Medium.WIFI))
        assertEquals(Traffic(5, 5), ledger.day("2026-10-01", Medium.WIFI))
    }

    @Test
    fun `the first look only remembers the counters`() {
        val sampler = TrafficSampler()
        val first = sampler.sample(1000, 5000, 3000, 100, 100)
        assertEquals(0, first.downBytesPerSecond)
        assertEquals(Traffic(), first.wifi)
    }

    @Test
    fun `speed and the split between wifi and mobile`() {
        val sampler = TrafficSampler()
        sampler.sample(0, 1000, 500, 100, 50)
        val s = sampler.sample(2000, 5000, 2500, 1100, 450)   // 2 seconds: total +4000/+2000, mobile +1000/+400
        assertEquals(2000, s.downBytesPerSecond)
        assertEquals(1000, s.upBytesPerSecond)
        assertEquals(Traffic(1000, 400), s.mobile)
        assertEquals(Traffic(3000, 1600), s.wifi)
    }

    @Test
    fun `a counter that restarts does not give a negative or huge number`() {
        val sampler = TrafficSampler()
        sampler.sample(0, 9_000_000, 9_000_000, 0, 0)
        val s = sampler.sample(1000, 2000, 1000, 0, 0)   // the phone restarted: counters are small again
        assertEquals(2000, s.downBytesPerSecond)
        assertEquals(Traffic(2000, 1000), s.wifi)
    }

    @Test
    fun `mobile can never exceed the total and a missing counter counts as zero`() {
        val sampler = TrafficSampler()
        sampler.sample(0, 0, 0, 0, 0)
        val odd = sampler.sample(1000, 100, 100, 500, 500)
        assertEquals(Traffic(), odd.wifi)
        assertEquals(Traffic(100, 100), odd.mobile)
        val missing = sampler.sample(2000, 600, 600, -1, -1)   // the phone has no mobile counter
        assertEquals(Traffic(0, 0), missing.mobile)
    }

    @Test
    fun `network types become generations`() {
        assertEquals(Generation.G5, NetworkTypes.generation(20))
        assertEquals(Generation.G4, NetworkTypes.generation(13))
        assertEquals(Generation.G4, NetworkTypes.generation(19))
        assertEquals(Generation.G3, NetworkTypes.generation(15))
        assertEquals(Generation.G2, NetworkTypes.generation(2))
        assertEquals(Generation.UNKNOWN, NetworkTypes.generation(0))
        assertEquals("5G", Generation.G5.label)
    }

    @Test
    fun `signal strength as a percentage`() {
        assertEquals(100, Signal.percentFromRssi(-40))
        assertEquals(100, Signal.percentFromRssi(-50))
        assertEquals(60, Signal.percentFromRssi(-70))
        assertEquals(0, Signal.percentFromRssi(-100))
        assertEquals(0, Signal.percentFromRssi(-120))
        assertEquals(75, Signal.percentFromLevel(3))
        assertEquals(100, Signal.percentFromLevel(9))
    }

    @Test
    fun `limits warn at eighty percent and at the limit`() {
        assertEquals(LimitState.NONE, Limits.state(500, 0))
        assertEquals(LimitState.OK, Limits.state(790, 1000))
        assertEquals(LimitState.NEAR, Limits.state(800, 1000))
        assertEquals(LimitState.REACHED, Limits.state(1000, 1000))
        assertEquals(LimitState.REACHED, Limits.state(5000, 1000))
    }
}

class TrafficSamplerRestoreTest {
    @Test
    fun `counters saved earlier count what passed while Arqon was closed but give no speed`() {
        val sampler = TrafficSampler()
        sampler.restore(0, 1000, 500, 100, 50)
        val s = sampler.sample(600_000, 9000, 1500, 100, 50)   // ten minutes later
        assertEquals(0, s.downBytesPerSecond)
        assertEquals(0, s.upBytesPerSecond)
        assertEquals(Traffic(8000, 1000), s.wifi)
        val next = sampler.sample(601_000, 10_000, 1500, 100, 50)
        assertEquals(1000, next.downBytesPerSecond)
    }
}
