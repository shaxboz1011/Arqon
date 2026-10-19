package app.arqon.core.net

import okhttp3.Dns
import okhttp3.OkHttpClient
import java.net.DatagramSocket
import java.net.InetAddress
import javax.net.SocketFactory

enum class LinkKind { WIFI, CELLULAR, ETHERNET, OTHER }

/**
 * One way to the internet (Wi-Fi, the first SIM, the second SIM ...). The core never touches Android: the app supplies a
 * socket factory that creates sockets already bound to that network, a name resolver that asks that network's DNS, and a way
 * to bind UDP sockets to it. On a computer the defaults simply use the normal route, which is what the tests do.
 */
class Link(
    val id: String,
    val name: String,
    val kind: LinkKind,
    val socketFactory: SocketFactory = SocketFactory.getDefault(),
    val resolver: (String) -> List<InetAddress> = { InetAddress.getAllByName(it).toList() },
    val bindDatagram: (DatagramSocket) -> Unit = {},
) {
    override fun toString() = "Link($id, $name)"

    /** An HTTP client whose every connection (and DNS lookup) goes through this link. */
    fun httpClient(base: OkHttpClient): OkHttpClient =
        base.newBuilder()
            .socketFactory(socketFactory)
            .dns(object : Dns {
                override fun lookup(hostname: String): List<InetAddress> = resolver(hostname)
            })
            .build()
}

/**
 * Hands out the links in turn, one per new connection, so many connections spread over every network. A link that looks down
 * is skipped while another one works.
 */
class LinkRotation(private val links: () -> List<Link>, private val isAlive: (Link) -> Boolean = { true }) {
    private var turn = 0
    private val counts = HashMap<String, Int>()

    /** The link for the next connection, or null when there are no links at all. */
    @Synchronized
    fun next(): Link? {
        val all = links()
        if (all.isEmpty()) return null
        val usable = all.filter(isAlive).ifEmpty { all }   // a doubtful link is better than no connection
        val link = usable[turn++ % usable.size]
        counts[link.id] = (counts[link.id] ?: 0) + 1
        return link
    }

    @Synchronized
    fun counts(): Map<String, Int> = HashMap(counts)
}
