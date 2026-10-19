package app.arqon.net

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.TelephonyNetworkSpecifier
import android.os.Build
import android.telephony.SubscriptionManager
import app.arqon.AppSettings
import app.arqon.core.net.Link
import app.arqon.core.net.LinkKind
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.net.InetSocketAddress
import java.util.concurrent.ConcurrentHashMap

enum class LinkState { WAITING, ACTIVE, UNAVAILABLE }

/** What the screens show about one way to the internet. */
data class LinkInfo(
    val id: String,
    val name: String,
    val kind: LinkKind,
    val state: LinkState,
    val metered: Boolean,
    val pingMillis: Int?,
    val enabled: Boolean,
    val link: Link?,
)

/**
 * Asks Android for every network at the same time: Wi-Fi, cable/USB tethering, and each SIM card. Android normally keeps only
 * one of them as "the" internet; a request for a specific network makes the system bring it up (and keep it up) for this app,
 * and the network object then lets sockets be bound to exactly that network.
 *
 * Whether a second SIM can carry data while the first one does depends on the phone. When it cannot, the request is simply
 * never answered and the SIM is shown as not available: this class never pretends.
 */
class AndroidLinkManager(
    private val context: Context,
    private val settings: AppSettings,
    private val scope: CoroutineScope,
) {
    private val cm = context.getSystemService(ConnectivityManager::class.java)

    private class Slot(val id: String, val label: String, val kind: LinkKind, val request: NetworkRequest, val subId: Int? = null) {
        @Volatile var network: Network? = null
        @Volatile var metered = false
        @Volatile var callback: ConnectivityManager.NetworkCallback? = null
        @Volatile var unavailableSince = 0L
    }

    private val slots = ConcurrentHashMap<String, Slot>()
    private val pings = ConcurrentHashMap<String, Int?>()
    private val links = HashMap<String, Link>()

    private val _all = MutableStateFlow<List<LinkInfo>>(emptyList())

    private val log = java.util.ArrayDeque<String>()
    private val clock = java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.US)

    /** What Android told us, in order. It is what makes a failure on one phone understandable. */
    @Synchronized
    fun note(text: String) {
        log.addLast(clock.format(java.util.Date()) + "  " + text)
        while (log.size > 120) log.removeFirst()
    }

    private fun describe(caps: NetworkCapabilities?): String {
        if (caps == null) return "?"
        val parts = ArrayList<String>()
        for ((t, name) in listOf(
            NetworkCapabilities.TRANSPORT_WIFI to "WIFI", NetworkCapabilities.TRANSPORT_CELLULAR to "CELLULAR",
            NetworkCapabilities.TRANSPORT_ETHERNET to "ETHERNET", NetworkCapabilities.TRANSPORT_VPN to "VPN",
        )) if (caps.hasTransport(t)) parts += name
        for ((c, name) in listOf(
            NetworkCapabilities.NET_CAPABILITY_INTERNET to "INTERNET", NetworkCapabilities.NET_CAPABILITY_VALIDATED to "VALIDATED",
            NetworkCapabilities.NET_CAPABILITY_NOT_METERED to "NOT_METERED", NetworkCapabilities.NET_CAPABILITY_NOT_RESTRICTED to "NOT_RESTRICTED",
        )) if (caps.hasCapability(c)) parts += name
        return parts.joinToString("+")
    }

    /** A text a user can copy and send: phone, Android version, permissions, every request and what came of it. */
    @Synchronized
    fun report(): String = buildString {
        appendLine("Arqon ${app.arqon.BuildConfig.VERSION_NAME} · ${Build.MANUFACTURER} ${Build.MODEL} · Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})")
        appendLine("phone permission: ${hasPhonePermission()}")
        appendLine("active network: " + cm.activeNetwork?.let { describe(cm.getNetworkCapabilities(it)) })
        appendLine("--- links")
        for (info in _all.value) appendLine("${info.id} \"${info.name}\" ${info.state} enabled=${info.enabled} metered=${info.metered} ping=${info.pingMillis}")
        appendLine("--- requests")
        for (slot in slots.values) appendLine("${slot.id}: network=${slot.network} unavailableSince=${slot.unavailableSince}")
        appendLine("--- events")
        log.forEach { appendLine(it) }
    }

    /** Every link Android knows about, with its state (also the ones that are switched off or not available). */
    val all: StateFlow<List<LinkInfo>> = _all

    @Volatile private var started = false

    fun hasPhonePermission(): Boolean =
        context.checkSelfPermission(Manifest.permission.READ_PHONE_STATE) == PackageManager.PERMISSION_GRANTED

    fun start() {
        if (started) return
        started = true
        note("start; phone permission=${hasPhonePermission()}")
        rebuildSlots()
        scope.launch {
            // networks come and go (a SIM was switched, Wi-Fi was lost): requests that were never answered are repeated
            while (isActive) {
                delay(15_000)
                retryUnavailable()
            }
        }
        scope.launch(Dispatchers.IO) {
            while (isActive) {
                probeAll()
                delay(4000)
            }
        }
        scope.launch {
            settings.disabledLinks.collect { publish() }
        }
    }

    /** Call again after the phone permission was granted: the SIM cards can then be told apart and asked for one by one. */
    fun rebuildSlots() {
        val wanted = LinkedHashMap<String, Slot>()
        wanted["wifi"] = Slot("wifi", "Wi-Fi", LinkKind.WIFI, request(NetworkCapabilities.TRANSPORT_WIFI))
        wanted["ethernet"] = Slot("ethernet", "USB / Ethernet", LinkKind.ETHERNET, request(NetworkCapabilities.TRANSPORT_ETHERNET))

        val sims = if (hasPhonePermission()) activeSims() else emptyList()
        if (sims.isNotEmpty() && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            for (sim in sims) {
                val spec = TelephonyNetworkSpecifier.Builder().setSubscriptionId(sim.subId).build()   // a request for one particular SIM card (Android 12 and newer)
                wanted["sim${sim.subId}"] = Slot(
                    "sim${sim.subId}", sim.label, LinkKind.CELLULAR,
                    NetworkRequest.Builder()
                        .addTransportType(NetworkCapabilities.TRANSPORT_CELLULAR)
                        .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                        .setNetworkSpecifier(spec).build(),
                    sim.subId,
                )
            }
        }
        // The plain "mobile data" request is always made: it is the only one that works without the phone permission,
        // and it is the SIM that Android itself uses for data.
        wanted["mobile"] = Slot("mobile", "Mobile data", LinkKind.CELLULAR, request(NetworkCapabilities.TRANSPORT_CELLULAR))

        for ((id, old) in slots.entries.toList()) if (!wanted.containsKey(id)) release(old)
        for ((id, slot) in wanted) if (!slots.containsKey(id)) { slots[id] = slot; ask(slot) }
        publish()
    }

    private fun request(transport: Int) = NetworkRequest.Builder()
        .addTransportType(transport)
        .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
        .build()

    private class Sim(val subId: Int, val label: String)

    private fun activeSims(): List<Sim> = try {
        val manager = context.getSystemService(SubscriptionManager::class.java)
        @Suppress("MissingPermission")
        val list = manager.activeSubscriptionInfoList.orEmpty()
        list.sortedBy { it.simSlotIndex }.map { info ->
            val name = info.carrierName?.toString()?.takeIf { it.isNotBlank() } ?: info.displayName?.toString().orEmpty()
            Sim(info.subscriptionId, "SIM ${info.simSlotIndex + 1}" + if (name.isNotBlank()) " · $name" else "")
        }
    } catch (_: SecurityException) {
        emptyList()
    }

    private fun ask(slot: Slot) {
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                note("${slot.id}: available $network on ${cm.getLinkProperties(network)?.interfaceName}")
                slot.network = network
                slot.unavailableSince = 0
                publish()
            }

            override fun onLost(network: Network) {
                note("${slot.id}: lost $network")
                if (slot.network == network) slot.network = null
                publish()
            }

            override fun onUnavailable() {
                note("${slot.id}: Android did not give this network (unavailable)")
                slot.network = null
                slot.unavailableSince = System.currentTimeMillis()
                publish()
            }

            override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) {
                slot.metered = !caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED)
                note("${slot.id}: capabilities ${describe(caps)}")
                publish()
            }
        }
        slot.callback = callback
        try {
            cm.requestNetwork(slot.request, callback, 15_000)
        } catch (e: SecurityException) {
            note("${slot.id}: request refused (${e.message})")
            slot.unavailableSince = System.currentTimeMillis()
        } catch (e: RuntimeException) {
            note("${slot.id}: request failed (${e.javaClass.simpleName}: ${e.message})")
            slot.unavailableSince = System.currentTimeMillis()
        }
    }

    private fun release(slot: Slot) {
        slot.callback?.let { try { cm.unregisterNetworkCallback(it) } catch (_: Exception) { } }
        slots.remove(slot.id)
        links.remove(slot.id)
    }

    private fun retryUnavailable() {
        for (slot in slots.values) {
            if (slot.network == null && slot.unavailableSince > 0 && System.currentTimeMillis() - slot.unavailableSince > 10_000) {
                slot.callback?.let { try { cm.unregisterNetworkCallback(it) } catch (_: Exception) { } }
                slot.unavailableSince = 0
                ask(slot)
            }
        }
    }

    /**
     * The links that carry data now: available, switched on, and without doubles (the plain "mobile data" request and one of the
     * SIM requests may be answered with the very same network, which must be counted once). With "save mobile data" on, the SIMs
     * are left out while Wi-Fi works.
     */
    fun activeLinks(): List<Link> = _all.value.filter { it.state == LinkState.ACTIVE && it.enabled && it.link != null }.map { it.link!! }

    @Synchronized
    private fun publish() {
        val disabled = settings.disabledLinks.value
        val byNetwork = LinkedHashMap<Long, Slot>()
        val result = ArrayList<LinkInfo>()
        val wifiUp = slots["wifi"]?.network != null

        // SIM requests come first so that a shared network is named after its SIM
        val ordered = slots.values.sortedBy { if (it.id == "mobile") 2 else if (it.subId != null) 0 else 1 }
        for (slot in ordered) {
            val network = slot.network
            if (network != null) {
                val handle = network.networkHandle
                if (byNetwork.containsKey(handle)) continue
                byNetwork[handle] = slot
            }
            val link = network?.let { linkFor(slot, it) }
            val enabledByUser = slot.id !in disabled &&
                !(settings.cellularOnlyWithoutWifi.value && slot.kind == LinkKind.CELLULAR && wifiUp)
            val state = when {
                network != null -> LinkState.ACTIVE
                slot.unavailableSince > 0 -> LinkState.UNAVAILABLE
                else -> LinkState.WAITING
            }
            // a slot nobody answered and that is only a duplicate request for the same thing is not worth listing
            if (state != LinkState.ACTIVE && slot.id == "mobile" && slots.values.any { it.subId != null }) continue
            if (state == LinkState.UNAVAILABLE && slot.kind == LinkKind.ETHERNET) continue   // most phones have no cable: do not show an empty row
            result += LinkInfo(slot.id, slot.label, slot.kind, state, slot.metered, pings[slot.id], enabledByUser, link)
        }
        _all.value = result
    }

    private fun linkFor(slot: Slot, network: Network): Link = links.getOrPut(slot.id + network.networkHandle) {
        Link(
            id = slot.id, name = slot.label, kind = slot.kind,
            socketFactory = network.socketFactory,
            resolver = { host -> network.getAllByName(host).toList() },
            bindDatagram = { socket -> network.bindSocket(socket) },
        )
    }

    /** Downloads a few megabytes through one link only and says what happened: the plainest way to see whether the link really carries data. */
    suspend fun test(info: LinkInfo): String = withContext(Dispatchers.IO) {
        val link = info.link ?: return@withContext "no network"
        try {
            val client = link.httpClient(okhttp3.OkHttpClient.Builder().callTimeout(20, java.util.concurrent.TimeUnit.SECONDS).build())
            val started = System.nanoTime()
            var bytes = 0L
            client.newCall(okhttp3.Request.Builder().url("https://speed.cloudflare.com/__down?bytes=4000000").build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext "HTTP ${response.code}"
                val source = response.body!!.source()
                val buffer = ByteArray(64 * 1024)
                while (true) { val n = source.read(buffer); if (n < 0) break; bytes += n }
            }
            val seconds = (System.nanoTime() - started) / 1e9
            val text = "OK " + String.format(java.util.Locale.US, "%.1f Mbit/s (%d KB in %.1f s)", bytes * 8 / seconds / 1e6, bytes / 1024, seconds)
            note("${info.id}: test $text")
            text
        } catch (e: Exception) {
            val text = "${e.javaClass.simpleName}: ${e.message}"
            note("${info.id}: test failed $text")
            text
        }
    }

    /** A tiny connection test through each link: it shows which networks really reach the internet, and how fast. */
    private suspend fun probeAll() {
        for (info in _all.value) {
            val link = info.link ?: continue
            pings[info.id] = withContext(Dispatchers.IO) {
                try {
                    val started = System.nanoTime()
                    link.socketFactory.createSocket().use { socket ->
                        socket.connect(InetSocketAddress("1.1.1.1", 443), 2500)
                    }
                    ((System.nanoTime() - started) / 1_000_000).toInt()
                } catch (e: Exception) {
                    note("${info.id}: connection test failed (${e.javaClass.simpleName}: ${e.message})")
                    null
                }
            }
        }
        publish()
    }
}
