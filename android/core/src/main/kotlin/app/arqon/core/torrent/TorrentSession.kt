package app.arqon.core.torrent

import app.arqon.core.net.Link
import app.arqon.core.net.LinkRotation
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.File
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.util.BitSet
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong
import javax.net.SocketFactory

sealed interface TorrentSource {
    class File(val bytes: ByteArray) : TorrentSource
    class MagnetLink(val magnet: Magnet) : TorrentSource
}

enum class Phase { METADATA, CHECKING, DOWNLOADING, COMPLETE, STOPPED, ERROR }

data class TorrentStatus(
    val phase: Phase = Phase.METADATA,
    val name: String? = null,
    val infoHashHex: String = "",
    val totalBytes: Long = 0,
    val doneBytes: Long = 0,
    val peers: Int = 0,
    val downloadBytesPerSecond: Long = 0,
    /** Bytes received over each link; they add up to the data accepted so far. */
    val perLink: Map<String, Long> = emptyMap(),
    val uploaded: Long = 0,
    val files: List<TorrentFileEntry> = emptyList(),
    val error: String? = null,
)

/**
 * Downloads one torrent. Every outgoing peer connection is made through the next [Link] in turn, so the connections (and with
 * them the data) spread over Wi-Fi and every SIM. Seeding only happens on connections this client opened: a phone behind a
 * mobile network cannot be reached from outside anyway.
 */
class TorrentSession(
    private val source: TorrentSource,
    private val saveDirectory: File,
    private val links: () -> List<Link>,
    scope: CoroutineScope,
    private val config: Config = Config(),
    selectedFiles: Set<Int>? = null,
) {
    data class Config(
        val maxPeers: Int = 40,
        val pipeline: Int = 10,
        val connectTimeoutMillis: Int = 6000,
        val readTimeoutMillis: Int = 30_000,
        val announcePort: Int = 6881,
        val seedAfterComplete: Boolean = false,
        val announceToPublicTrackers: Boolean = true,
        val extraTrackers: List<String> = emptyList(),
        val uploadSlots: Int = 6,
        val httpClient: OkHttpClient = OkHttpClient(),
    )

    private val sessionJob = SupervisorJob(scope.coroutineContext[Job])
    private val sessionScope = CoroutineScope(scope.coroutineContext + sessionJob)
    private val lock = Any()
    private val rotation = LinkRotation(links)
    private val tracker = TrackerClient(config.httpClient)
    private val peerId = ByteArray(20).also {
        "-AQ0001-".toByteArray().copyInto(it)
        ByteArray(12).also { tail -> java.security.SecureRandom().nextBytes(tail) }.copyInto(it, 8)
    }

    private val magnet = (source as? TorrentSource.MagnetLink)?.magnet
    private val infoHash: ByteArray = magnet?.infoHash ?: runCatching { MetaInfo.parse((source as TorrentSource.File).bytes).infoHash }.getOrDefault(ByteArray(20))

    @Volatile private var meta: MetaInfo? = null

    /** The files have been checked, so what we have is known: only now may a peer be let in to ask for pieces. */
    @Volatile private var ready = false
    private var storage: PieceStorage? = null
    private var have = BitSet()
    private var wanted = BitSet()
    private var selected: Set<Int>? = selectedFiles
    private var availability = IntArray(0)
    private val inflight = HashMap<Int, PieceBuffer>()

    /** Pieces whose last block has arrived and that are being checked and written: nobody may start them again meanwhile. */
    private val verifying = HashSet<Int>()
    private var doneBytes = 0L
    private var totalWanted = 0L

    private val metaReady = CompletableDeferred<MetaInfo>()
    private var metaBuffer: ByteArray? = null
    private var metaReceived = BitSet()

    private val candidates = ArrayDeque<Pair<String, Int>>()
    private val known = HashSet<String>()
    private val failures = HashMap<String, Int>()
    private val peers = ConcurrentHashMap.newKeySet<Peer>()
    private val perLink = ConcurrentHashMap<String, AtomicLong>()
    private val uploadedBytes = AtomicLong()
    private val receivedBytes = AtomicLong()
    private val completion = CompletableDeferred<Unit>()

    private val _status = MutableStateFlow(TorrentStatus(infoHashHex = infoHash.joinToString("") { "%02x".format(it) }, name = magnet?.name))
    val status: StateFlow<TorrentStatus> = _status

    private class PieceBuffer(val data: ByteArray) {
        val blocks = (data.size + PeerWire.BLOCK_SIZE - 1) / PeerWire.BLOCK_SIZE
        val requested = BitSet(blocks)
        val received = BitSet(blocks)
        var count = 0
        val contributors = HashSet<Any>()
        fun blockSize(i: Int) = minOf(PeerWire.BLOCK_SIZE, data.size - i * PeerWire.BLOCK_SIZE)
    }

    private data class Req(val piece: Int, val block: Int)

    private inner class Peer(val host: String, val port: Int, val link: Link?) {
        @Volatile var socket: Socket? = null
        @Volatile lateinit var out: DataOutputStream
        var choked = true
        var sentInterested = false
        var bits: BitSet? = null
        var rawBits: ByteArray? = null
        val pendingHaves = ArrayList<Int>()
        val outstanding = LinkedHashSet<Req>()
        var capabilities: Extensions.PeerCapabilities? = null
        var amChoking = true
        var interested = false

        /** Pieces this peer once sent wrongly (they failed the hash check): it is not asked for them again. */
        val avoid = HashSet<Int>()
        var strikes = 0
        val key get() = "$host:$port"
    }

    fun addPeer(host: String, port: Int) {
        synchronized(lock) {
            val key = "$host:$port"
            if (known.add(key)) candidates.addLast(host to port)
        }
    }

    fun start(): Job = sessionScope.launch {
        try {
            run()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            fail(e.message ?: e.toString())
        }
    }

    /** Selects which files to download (null: all). Pieces already fetched stay; the rest of the swarm work follows the new choice. */
    fun selectFiles(files: Set<Int>?) {
        synchronized(lock) {
            selected = files
            val m = meta ?: return
            wanted = wantedPieces(m, files)
            totalWanted = (0 until m.pieceCount).filter { wanted[it] }.sumOf { m.pieceSize(it).toLong() }
            doneBytes = (0 until m.pieceCount).filter { wanted[it] && have[it] }.sumOf { m.pieceSize(it).toLong() }
            inflight.keys.removeAll { !wanted[it] }
        }
        publish()
        peers.forEach { refreshInterest(it) }
    }

    suspend fun stop() {
        val current = _status.value.phase
        sessionJob.cancelAndJoin()
        peers.forEach { runCatching { it.socket?.close() } }
        storage?.close()
        if (current != Phase.COMPLETE && current != Phase.ERROR) _status.value = _status.value.copy(phase = Phase.STOPPED, peers = 0, downloadBytesPerSecond = 0)
        else _status.value = _status.value.copy(peers = 0, downloadBytesPerSecond = 0)
    }

    private suspend fun run() {
        launchTicker()
        magnet?.peers?.forEach { addPeer(it.first, it.second) }
        launchConnector()
        launchAnnouncers()

        val m = when (source) {
            is TorrentSource.File -> try {
                MetaInfo.parse(source.bytes)
            } catch (e: TorrentFormatException) {
                fail(e.message ?: "bad torrent")
                return
            }
            is TorrentSource.MagnetLink -> {
                publish(Phase.METADATA)
                metaReady.await()
            }
        }
        setup(m)

        publish(Phase.CHECKING)
        val checked = withContext(Dispatchers.IO) { storage!!.verifyExisting(synchronized(lock) { wanted.clone() as BitSet }) }
        synchronized(lock) {
            have = checked
            doneBytes = (0 until m.pieceCount).filter { wanted[it] && have[it] }.sumOf { m.pieceSize(it).toLong() }
        }
        ready = true
        peers.forEach { onMetadataReady(it) }

        if (isComplete()) {
            completion.complete(Unit)
        } else {
            publish(Phase.DOWNLOADING)
        }
        completion.await()
        publish(Phase.COMPLETE)
        if (!config.seedAfterComplete) {
            peers.forEach { runCatching { it.socket?.close() } }
            storage?.close()
        } else {
            awaitCancellation()
        }
    }

    private fun setup(m: MetaInfo) {
        synchronized(lock) {
            meta = m
            availability = IntArray(m.pieceCount)
            have = BitSet(m.pieceCount)
            wanted = wantedPieces(m, selected)
            totalWanted = (0 until m.pieceCount).filter { wanted[it] }.sumOf { m.pieceSize(it).toLong() }
            storage = PieceStorage(m, saveDirectory)
        }
        _status.value = _status.value.copy(name = m.name, totalBytes = totalWanted, files = m.files)
    }

    private fun wantedPieces(m: MetaInfo, files: Set<Int>?): BitSet {
        val bits = BitSet(m.pieceCount)
        for (f in m.files) {
            if (f.length == 0L || (files != null && f.index !in files)) continue
            val first = (f.offset / m.pieceLength).toInt()
            val last = ((f.offset + f.length - 1) / m.pieceLength).toInt()
            bits.set(first, last + 1)
        }
        return bits
    }

    private fun isComplete(): Boolean = synchronized(lock) {
        meta != null && (0 until meta!!.pieceCount).none { wanted[it] && !have[it] }
    }

    private fun fail(message: String) {
        _status.value = _status.value.copy(phase = Phase.ERROR, error = message)
        completion.cancel()
    }

    private fun publish(phase: Phase? = null) {
        val current = _status.value
        _status.value = current.copy(
            phase = phase ?: current.phase,
            totalBytes = synchronized(lock) { totalWanted },
            doneBytes = synchronized(lock) { doneBytes },
            peers = peers.count { it.socket?.isConnected == true },
            perLink = perLink.mapValues { it.value.get() },
            uploaded = uploadedBytes.get(),
        )
    }

    private fun launchTicker() {
        sessionScope.launch {
            var last = receivedBytes.get()
            while (isActive) {
                delay(1000)
                val now = receivedBytes.get()
                val speed = now - last
                last = now
                publish()
                _status.value = _status.value.copy(downloadBytesPerSecond = speed)
            }
        }
    }

    // ---- finding and connecting to peers -------------------------------------------------------------------------------

    private fun launchConnector() {
        sessionScope.launch {
            while (isActive) {
                val canConnect = meta == null || !isComplete() || config.seedAfterComplete
                while (canConnect && peers.size < config.maxPeers) {
                    val next = synchronized(lock) { candidates.removeFirstOrNull() } ?: break
                    launchPeer(next.first, next.second)
                }
                delay(300)
            }
        }
    }

    private fun launchPeer(host: String, port: Int) {
        val link = rotation.next()
        val peer = Peer(host, port, link)
        peers.add(peer)
        sessionScope.launch(Dispatchers.IO) {
            val watcher = launch { try { awaitCancellation() } finally { runCatching { peer.socket?.close() } } }
            var gotData = false
            try {
                val socket = (link?.socketFactory ?: SocketFactory.getDefault()).createSocket()
                peer.socket = socket
                socket.tcpNoDelay = true
                socket.connect(InetSocketAddress(host, port), config.connectTimeoutMillis)
                socket.soTimeout = config.readTimeoutMillis
                gotData = talk(peer, socket)
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                // the peer was unreachable or went away; it may be tried again later
            } finally {
                watcher.cancel()
                runCatching { peer.socket?.close() }
                disconnected(peer)
                scheduleRetry(peer.key, host, port, gotData)
            }
        }
    }

    private fun scheduleRetry(key: String, host: String, port: Int, gotData: Boolean) {
        val tries = synchronized(lock) {
            if (gotData) failures.remove(key)
            val n = (failures[key] ?: 0) + 1
            failures[key] = n
            n
        }
        if (tries > 3 || isComplete() && !config.seedAfterComplete) return
        sessionScope.launch {
            delay(if (gotData) 20_000L else 30_000L * tries)
            synchronized(lock) { candidates.addLast(host to port) }
        }
    }

    private fun disconnected(peer: Peer) {
        synchronized(lock) {
            giveBack(peer)
            peer.bits?.let { bits -> for (i in bits.stream().toArray()) if (i < availability.size && availability[i] > 0) availability[i]-- }
        }
        peers.remove(peer)
    }

    private fun launchAnnouncers() {
        sessionScope.launch {
            val urls = LinkedHashSet<String>()
            val initial = (magnet?.trackers ?: emptyList()) + config.extraTrackers
            urls += initial
            if (source is TorrentSource.File) {
                runCatching { MetaInfo.parse(source.bytes).trackers }.getOrDefault(emptyList()).forEach { urls += it }
            }
            if (config.announceToPublicTrackers) urls += PUBLIC_TRACKERS
            for (url in urls) {
                launch {
                    var wait = 0L
                    var failuresInARow = 0
                    var first = true
                    while (isActive) {
                        if (wait > 0) delay(wait)
                        try {
                            val left = synchronized(lock) { if (meta == null) 1L else maxOf(0, totalWanted - doneBytes) }
                            val result = tracker.announce(
                                url, rotation.next(), infoHash, peerId, config.announcePort,
                                uploadedBytes.get(), receivedBytes.get(), left, if (first) TrackerEvent.STARTED else TrackerEvent.NONE,
                            )
                            first = false
                            failuresInARow = 0
                            result.peers.forEach { addPeer(it.first, it.second) }
                            wait = result.intervalSeconds.coerceIn(60, 1800) * 1000L
                        } catch (e: CancellationException) {
                            throw e
                        } catch (_: Exception) {
                            failuresInARow++
                            wait = minOf(60_000L * failuresInARow, 600_000L)
                        }
                    }
                }
            }
        }
    }

    // ---- talking to one peer -------------------------------------------------------------------------------------------

    /** Returns true when the peer delivered data. */
    private fun talk(peer: Peer, socket: Socket): Boolean {
        val input = DataInputStream(socket.getInputStream().buffered(32 * 1024))
        peer.out = DataOutputStream(socket.getOutputStream().buffered(32 * 1024))
        PeerWire.writeHandshake(peer.out, infoHash, peerId)
        val theirs = PeerWire.readHandshake(input)
        if (!theirs.infoHash.contentEquals(infoHash)) throw ProtocolException("the peer talks about another torrent")
        if (theirs.supportsExtensions) {
            PeerWire.write(peer.out, PeerMessage.Extended(Extensions.HANDSHAKE_ID, Extensions.handshake(meta?.infoBytes?.size)))
        }
        sendOurBitfield(peer)

        var delivered = false
        while (true) {
            when (val message = PeerWire.read(input)) {
                PeerMessage.KeepAlive, PeerMessage.NotInterested, is PeerMessage.Cancel, is PeerMessage.Unknown -> Unit
                PeerMessage.Choke -> synchronized(lock) { peer.choked = true; giveBack(peer) }
                PeerMessage.Unchoke -> { synchronized(lock) { peer.choked = false }; fill(peer) }
                PeerMessage.Interested -> { peer.interested = true; unchokeIfRoom(peer) }
                is PeerMessage.Have -> onHave(peer, message.piece)
                is PeerMessage.Bitfield -> onBitfield(peer, message.bits)
                is PeerMessage.Request -> serve(peer, message)
                is PeerMessage.Piece -> if (onBlock(peer, message)) delivered = true
                is PeerMessage.Extended -> onExtended(peer, message)
            }
        }
        @Suppress("UNREACHABLE_CODE")
        return delivered
    }

    private fun sendOurBitfield(peer: Peer) {
        val m = meta ?: return
        val bytes = synchronized(lock) {
            if (have.isEmpty) return
            ByteArray((m.pieceCount + 7) / 8).also { b -> for (i in have.stream().toArray()) b[i / 8] = (b[i / 8].toInt() or (0x80 shr (i % 8))).toByte() }
        }
        PeerWire.write(peer.out, PeerMessage.Bitfield(bytes))
    }

    private fun onBitfield(peer: Peer, raw: ByteArray) {
        val m = meta
        if (m == null) {
            peer.rawBits = raw
            return
        }
        applyBitfield(peer, raw, m)
        refreshInterest(peer)
        fill(peer)
    }

    private fun applyBitfield(peer: Peer, raw: ByteArray, m: MetaInfo) {
        val bits = BitSet(m.pieceCount)
        for (i in 0 until m.pieceCount) if (i / 8 < raw.size && (raw[i / 8].toInt() and (0x80 shr (i % 8))) != 0) bits.set(i)
        synchronized(lock) {
            peer.bits = bits
            for (i in bits.stream().toArray()) availability[i]++
        }
    }

    private fun onHave(peer: Peer, piece: Int) {
        val m = meta
        if (m == null) {
            peer.pendingHaves += piece
            return
        }
        if (piece !in 0 until m.pieceCount) throw ProtocolException("a piece that does not exist")
        synchronized(lock) {
            val bits = peer.bits ?: BitSet(m.pieceCount).also { peer.bits = it }
            if (!bits[piece]) {
                bits.set(piece)
                availability[piece]++
            }
        }
        refreshInterest(peer)
        fill(peer)
    }

    /** Called when the metadata (of a magnet link) and the check of the files are ready: what peers announced earlier can be used now. */
    private fun onMetadataReady(peer: Peer) {
        val m = meta ?: return
        if (peer.socket?.isConnected != true) return
        try {
            peer.rawBits?.let { applyBitfield(peer, it, m); peer.rawBits = null }
            if (peer.pendingHaves.isNotEmpty()) {
                synchronized(lock) {
                    val bits = peer.bits ?: BitSet(m.pieceCount).also { peer.bits = it }
                    for (p in peer.pendingHaves) if (p in 0 until m.pieceCount && !bits[p]) { bits.set(p); availability[p]++ }
                    peer.pendingHaves.clear()
                }
            }
            sendOurBitfield(peer)
            if (peer.interested) unchokeIfRoom(peer)
            refreshInterest(peer)
            fill(peer)
        } catch (_: IOException) {
            runCatching { peer.socket?.close() }
        }
    }

    private fun refreshInterest(peer: Peer) {
        val m = meta ?: return
        val wants = synchronized(lock) {
            val bits = peer.bits ?: return
            var any = false
            var i = wanted.nextSetBit(0)
            while (i >= 0) {
                if (!have[i] && bits[i]) { any = true; break }
                i = wanted.nextSetBit(i + 1)
            }
            any
        }
        try {
            if (wants && !peer.sentInterested) { peer.sentInterested = true; PeerWire.write(peer.out, PeerMessage.Interested) }
            else if (!wants && peer.sentInterested) { peer.sentInterested = false; PeerWire.write(peer.out, PeerMessage.NotInterested) }
        } catch (_: IOException) {
            runCatching { peer.socket?.close() }
        }
        if (m.pieceCount == 0) return
    }

    // ---- choosing what to ask for --------------------------------------------------------------------------------------

    private fun fill(peer: Peer) {
        while (true) {
            val request = synchronized(lock) {
                if (peer.choked || peer.outstanding.size >= config.pipeline || meta == null) return
                nextRequest(peer)?.also { peer.outstanding += it }
            } ?: return
            val m = meta ?: return
            val buffer = synchronized(lock) { inflight[request.piece] } ?: continue
            try {
                PeerWire.write(peer.out, PeerMessage.Request(request.piece, request.block * PeerWire.BLOCK_SIZE, buffer.blockSize(request.block)))
            } catch (_: IOException) {
                runCatching { peer.socket?.close() }
                return
            }
            if (m.pieceCount == 0) return
        }
    }

    /** Called with [lock] held. Rarest piece first; finish started pieces first; at the very end ask several peers for the same block. */
    private fun nextRequest(peer: Peer): Req? {
        val m = meta ?: return null
        val bits = peer.bits ?: return null
        for ((piece, buffer) in inflight) {
            if (!bits[piece] || piece in peer.avoid) continue
            val block = firstClear(buffer.requested, buffer.blocks)
            if (block >= 0) { buffer.requested.set(block); return Req(piece, block) }
        }
        var best = -1
        var i = wanted.nextSetBit(0)
        while (i >= 0) {
            if (!have[i] && i !in inflight && i !in verifying && i !in peer.avoid && bits[i] && (best < 0 || availability[i] < availability[best])) best = i
            i = wanted.nextSetBit(i + 1)
        }
        if (best >= 0) {
            val buffer = PieceBuffer(ByteArray(m.pieceSize(best)))
            inflight[best] = buffer
            buffer.requested.set(0)
            return Req(best, 0)
        }
        // End game: every missing piece is already being fetched; a slow peer must not hold back the last blocks.
        var anyUnstarted = false
        var j = wanted.nextSetBit(0)
        while (j >= 0) { if (!have[j] && j !in inflight && j !in verifying) { anyUnstarted = true; break }; j = wanted.nextSetBit(j + 1) }
        if (!anyUnstarted) {
            for ((piece, buffer) in inflight) {
                if (!bits[piece] || piece in peer.avoid) continue
                for (block in 0 until buffer.blocks) {
                    val r = Req(piece, block)
                    if (!buffer.received[block] && r !in peer.outstanding) return r
                }
            }
        }
        return null
    }

    private fun firstClear(bits: BitSet, size: Int): Int = bits.nextClearBit(0).takeIf { it < size } ?: -1

    /** Called with [lock] held: what a peer was asked for and has not delivered goes back to the pool. */
    private fun giveBack(peer: Peer) {
        for (r in peer.outstanding) inflight[r.piece]?.let { if (!it.received[r.block]) it.requested.clear(r.block) }
        peer.outstanding.clear()
    }

    /** Returns true when the block was new and accepted. */
    private fun onBlock(peer: Peer, message: PeerMessage.Piece): Boolean {
        val m = meta ?: return false
        var accepted = false
        var completed: ByteArray? = null
        var contributors: Set<Any> = emptySet()
        synchronized(lock) {
            val block = message.begin / PeerWire.BLOCK_SIZE
            peer.outstanding.remove(Req(message.piece, block))
            val buffer = inflight[message.piece] ?: return@synchronized
            if (message.begin % PeerWire.BLOCK_SIZE != 0 || block >= buffer.blocks || message.data.size != buffer.blockSize(block)) {
                throw ProtocolException("a block of the wrong size")
            }
            if (buffer.received[block]) return@synchronized
            message.data.copyInto(buffer.data, message.begin)
            buffer.contributors.add(peer)
            buffer.received.set(block)
            buffer.count++
            accepted = true
            if (buffer.count == buffer.blocks) {
                inflight.remove(message.piece)
                verifying.add(message.piece)
                completed = buffer.data
                contributors = buffer.contributors.toSet()
            }
        }
        if (accepted) {
            receivedBytes.addAndGet(message.data.size.toLong())
            peer.link?.let { perLink.getOrPut(it.id) { AtomicLong() }.addAndGet(message.data.size.toLong()) }
        }
        completed?.let { data ->
            if (PieceStorage.sha1(data).contentEquals(m.pieceHash(message.piece))) {
                storage!!.writePiece(message.piece, data)
                synchronized(lock) {
                    have.set(message.piece)
                    verifying.remove(message.piece)
                    if (wanted[message.piece]) doneBytes += data.size
                }
                broadcastHave(message.piece)
                if (isComplete()) completion.complete(Unit)
            } else {
                // a piece that fails its hash is dropped: it is no longer in flight, so it will be requested again
                synchronized(lock) {
                    verifying.remove(message.piece)
                    for (c in contributors) (c as? Peer)?.let { it.avoid += message.piece; it.strikes++ }
                }
                for (c in contributors) if ((c as? Peer)?.strikes ?: 0 >= 3) runCatching { (c as Peer).socket?.close() }   // a peer that keeps sending bad data is dropped
            }
        }
        fill(peer)
        return accepted
    }

    private fun broadcastHave(piece: Int) {
        for (p in peers) {
            try {
                if (p.socket?.isConnected == true && p.bits != null) PeerWire.write(p.out, PeerMessage.Have(piece))
            } catch (_: IOException) {
                runCatching { p.socket?.close() }
            }
        }
    }

    // ---- giving back --------------------------------------------------------------------------------------------------

    private fun unchokeIfRoom(peer: Peer) {
        if (!ready) return   // asked again from onMetadataReady once the check is done
        val allow = synchronized(lock) {
            if (!peer.amChoking) return
            val open = peers.count { !it.amChoking }
            if (open < config.uploadSlots) { peer.amChoking = false; true } else false
        }
        if (allow) PeerWire.write(peer.out, PeerMessage.Unchoke)
    }

    private fun serve(peer: Peer, request: PeerMessage.Request) {
        val m = meta ?: return
        if (peer.amChoking || request.piece !in 0 until m.pieceCount || request.length !in 1..(2 * PeerWire.BLOCK_SIZE)) return
        if (!synchronized(lock) { have[request.piece] }) return
        if (request.begin < 0 || request.begin + request.length > m.pieceSize(request.piece)) return
        val data = storage?.read(request.piece, request.begin, request.length) ?: return
        PeerWire.write(peer.out, PeerMessage.Piece(request.piece, request.begin, data))
        uploadedBytes.addAndGet(data.size.toLong())
        publish()
    }

    // ---- extensions: magnet metadata and peer exchange ---------------------------------------------------------------

    private fun onExtended(peer: Peer, message: PeerMessage.Extended) {
        when (message.id) {
            Extensions.HANDSHAKE_ID -> {
                val caps = Extensions.readHandshake(message.payload)
                peer.capabilities = caps
                if (meta == null && caps.metadataId != null && caps.metadataSize != null) requestMetadata(peer, caps)
            }
            Extensions.OUR_METADATA_ID -> {
                val data = Extensions.readMetadata(message.payload) ?: return
                if (data.type == 1) receiveMetadata(data) else if (data.type == 0) rejectMetadata(peer, data.piece)
            }
            Extensions.OUR_PEX_ID -> Extensions.readPex(message.payload).forEach { addPeer(it.first, it.second) }
        }
    }

    private fun requestMetadata(peer: Peer, caps: Extensions.PeerCapabilities) {
        val size = caps.metadataSize ?: return
        if (size > 8 * 1024 * 1024) return
        synchronized(lock) {
            if (metaBuffer == null) {
                metaBuffer = ByteArray(size)
                metaReceived = BitSet()
            }
        }
        val pieces = (size + Extensions.METADATA_PIECE - 1) / Extensions.METADATA_PIECE
        for (i in 0 until pieces) {
            if (synchronized(lock) { metaReceived[i] }) continue
            PeerWire.write(peer.out, PeerMessage.Extended(caps.metadataId!!, Extensions.metadataRequest(i)))
        }
    }

    private fun rejectMetadata(peer: Peer, piece: Int) {
        val id = peer.capabilities?.metadataId ?: return
        PeerWire.write(peer.out, PeerMessage.Extended(id, Extensions.metadataReject(piece)))
    }

    private fun receiveMetadata(data: Extensions.MetadataMessage) {
        val ready: ByteArray? = synchronized(lock) {
            val buffer = metaBuffer ?: return
            val offset = data.piece * Extensions.METADATA_PIECE
            if (data.piece < 0 || offset >= buffer.size || data.data.size > buffer.size - offset) return
            data.data.copyInto(buffer, offset)
            metaReceived.set(data.piece)
            val pieces = (buffer.size + Extensions.METADATA_PIECE - 1) / Extensions.METADATA_PIECE
            if (metaReceived.cardinality() == pieces) buffer.copyOf() else null
        }
        if (ready == null) return
        if (PieceStorage.sha1(ready).contentEquals(infoHash)) {
            try {
                metaReady.complete(MetaInfo.fromInfo(ready, magnet?.trackers ?: emptyList()))
            } catch (_: TorrentFormatException) {
                resetMetadata()
            }
        } else {
            resetMetadata()   // what the peers sent is not the torrent the magnet link names: never trust it
        }
    }

    private fun resetMetadata() {
        synchronized(lock) { metaBuffer = null; metaReceived = BitSet() }
    }

    companion object {
        val PUBLIC_TRACKERS = listOf(
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://open.stealth.si:80/announce",
            "udp://exodus.desync.com:6969/announce",
            "udp://tracker.torrent.eu.org:451/announce",
            "udp://explodie.org:6969/announce",
            "udp://open.demonii.com:1337/announce",
            "http://tracker.openbittorrent.com:80/announce",
        )
    }
}
