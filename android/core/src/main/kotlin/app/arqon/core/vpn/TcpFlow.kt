package app.arqon.core.vpn

import java.io.IOException
import java.net.Socket
import java.util.ArrayDeque
import java.util.concurrent.Executor
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock
import kotlin.random.Random

/** Where finished packets go: back into the tunnel, towards the app that sent the request. */
internal fun interface PacketSink {
    fun send(packet: ByteArray)
}

private const val MASK = 0xffffffffL

/**
 * One TCP connection of an app, carried over a real socket. The tunnel gives us raw packets, so this class plays the server
 * side of the connection for the app (handshake, sequence numbers, acknowledgements, window, closing) and copies the bytes to
 * and from a socket that was opened through the chosen network.
 *
 * The tunnel is a local pipe: it does not lose or reorder packets, so there is no congestion control and only a simple
 * retransmission timer. The advertised window is 64 KB and the app's own window limits what is sent to it.
 */
internal class TcpFlow(
    private val clientIp: Int,
    private val clientPort: Int,
    private val serverIp: Int,
    private val serverPort: Int,
    private val sink: PacketSink,
    private val executor: Executor,
    private val connect: () -> Socket,
    private val onClosed: (TcpFlow) -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private enum class State { CONNECTING, OPEN, CLOSED }

    private class Sent(val seq: Long, val data: ByteArray, val fin: Boolean) {
        var sentAt = 0L
        var tries = 0
    }

    private val lock = ReentrantLock()
    private val changed = lock.newCondition()

    private var state = State.CONNECTING
    private var isn = 0L
    private var rcvNext = 0L
    private var sndNext = 0L
    private var sndUna = 0L
    private var clientWindow = 0
    private var mss = OUR_MSS
    private var finReceived = false
    private var finSent = false
    private var finAcked = false
    private var lastAdvertised = RECV_WINDOW
    private var synAckAt = 0L
    private var synAckTries = 0
    private val unacked = ArrayDeque<Sent>()
    private var socket: Socket? = null

    private val outgoing = LinkedBlockingQueue<ByteArray>()
    private val pending = AtomicInteger()

    @Volatile var lastActivity = clock()
        private set

    /** Bytes the app sent and bytes it received, for the numbers on the screen. */
    @Volatile var bytesUp = 0L
        private set
    @Volatile var bytesDown = 0L
        private set

    val isClosed get() = state == State.CLOSED

    /** The app's SYN: remember its numbers, open the real connection, and answer with SYN+ACK once that worked. */
    fun start(syn: TcpSegment) {
        lock.withLock {
            rcvNext = (syn.seq + 1) and MASK
            clientWindow = syn.window
            mss = minOf(OUR_MSS, syn.mssOption() ?: 536)
        }
        executor.execute(::connectAndServe)
    }

    private fun connectAndServe() {
        val upstream = try {
            connect()
        } catch (e: Exception) {
            lock.withLock {
                if (state != State.CLOSED) {
                    sendRaw(0, rcvNext, RST or ACK, 0)
                    closeLocked()
                }
            }
            return
        }
        lock.withLock {
            if (state == State.CLOSED) {
                closeQuietly(upstream)
                return
            }
            socket = upstream
            isn = Random.nextLong(0, 1L shl 32)
            sndUna = isn
            sndNext = isn
            sendSynAck()
            sndNext = (isn + 1) and MASK
            state = State.OPEN
        }
        executor.execute { writerLoop(upstream) }
        readerLoop(upstream)
    }

    private fun sendSynAck() {
        sink.send(PacketBuilder.tcp(serverIp, serverPort, clientIp, clientPort, isn, rcvNext, SYN or ACK, window(), mss = mss))
        synAckAt = clock()
    }

    /** A segment of the app for this connection. */
    fun onSegment(t: TcpSegment) {
        lock.withLock {
            lastActivity = clock()
            if (state == State.CLOSED) return
            if (t.flags and RST != 0) {
                closeLocked()
                return
            }
            if (state != State.OPEN) return   // still connecting: a repeated SYN, nothing to do
            if (t.flags and SYN != 0) {   // the app did not get our SYN+ACK: say it again
                if (sndUna == isn) sendSynAck()
                return
            }
            if (t.flags and ACK != 0) handleAck(t.ack, t.window)

            val buf = t.ip.buf
            var seq = t.seq
            var offset = t.payloadOffset
            var length = t.payloadLength
            var needAck = false
            if (length > 0) {
                needAck = true
                val behind = Bytes.seqDiff(rcvNext, seq)
                if (behind > 0) {   // part of it (or all of it) is a repeat
                    val cut = minOf(behind, length)
                    seq = (seq + cut) and MASK
                    offset += cut
                    length -= cut
                }
                if (length > 0 && seq == rcvNext) {
                    val chunk = buf.copyOfRange(offset, offset + length)
                    pending.addAndGet(length)
                    outgoing.add(chunk)
                    rcvNext = (rcvNext + length) and MASK
                    bytesUp += length
                }
            }
            if (t.flags and FIN != 0 && !finReceived && ((t.seq + t.payloadLength) and MASK) == rcvNext) {
                finReceived = true
                rcvNext = (rcvNext + 1) and MASK
                outgoing.add(FIN_MARK)
                needAck = true
            }
            if (needAck) sendAck()
            finishIfDone()
        }
    }

    private fun handleAck(ack: Long, window: Int) {
        clientWindow = window
        if (Bytes.seqDiff(ack, sndUna) > 0 && Bytes.seqDiff(sndNext, ack) >= 0) {
            sndUna = ack
            while (unacked.isNotEmpty()) {
                val first = unacked.first()
                val end = (first.seq + first.data.size + (if (first.fin) 1 else 0)) and MASK
                if (Bytes.seqDiff(ack, end) >= 0) unacked.removeFirst() else break
            }
            if (finSent && ack == sndNext) finAcked = true
        }
        changed.signalAll()
    }

    private fun finishIfDone() {
        if (finSent && finAcked && finReceived) closeLocked()
    }

    private fun inFlight() = Bytes.seqDiff(sndNext, sndUna)

    private fun window() = maxOf(0, RECV_WINDOW - pending.get())

    private fun sendAck() = sendRaw(sndNext, rcvNext, ACK, window())

    private fun sendRaw(seq: Long, ack: Long, flags: Int, window: Int) {
        lastAdvertised = window
        sink.send(PacketBuilder.tcp(serverIp, serverPort, clientIp, clientPort, seq, ack, flags, window))
    }

    /** Bytes from the real connection to the app, as long as the app's window has room. */
    private fun readerLoop(upstream: Socket) {
        try {
            val input = upstream.getInputStream()
            val buffer = ByteArray(mss)
            while (true) {
                var room = 0
                lock.withLock {
                    while (state == State.OPEN) {
                        room = minOf(mss, clientWindow - inFlight())
                        if (room > 0) break
                        changed.await(250, TimeUnit.MILLISECONDS)
                    }
                    if (state != State.OPEN) return
                }
                val n = input.read(buffer, 0, room)
                if (n < 0) break
                lock.withLock {
                    if (state != State.OPEN) return
                    sendData(buffer, n)
                }
            }
            lock.withLock {
                if (state == State.OPEN && !finSent) {
                    val fin = Sent(sndNext, EMPTY, true)
                    transmit(fin)
                    unacked.addLast(fin)
                    sndNext = (sndNext + 1) and MASK
                    finSent = true
                    finishIfDone()
                }
            }
        } catch (_: IOException) {
            lock.withLock {
                if (state == State.OPEN) {
                    sendRaw(sndNext, rcvNext, RST or ACK, 0)
                    closeLocked()
                }
            }
        }
    }

    private fun sendData(buffer: ByteArray, n: Int) {
        lastActivity = clock()
        val segment = Sent(sndNext, buffer.copyOf(n), false)
        transmit(segment)
        unacked.addLast(segment)
        sndNext = (sndNext + n) and MASK
        bytesDown += n
    }

    private fun transmit(segment: Sent) {
        segment.sentAt = clock()
        lastAdvertised = window()
        val flags = if (segment.fin) FIN or ACK else ACK or PSH
        sink.send(PacketBuilder.tcp(serverIp, serverPort, clientIp, clientPort, segment.seq, rcvNext, flags, lastAdvertised, segment.data, 0, segment.data.size))
    }

    /** Bytes from the app to the real connection, in order. */
    private fun writerLoop(upstream: Socket) {
        try {
            val output = upstream.getOutputStream()
            while (true) {
                val item = outgoing.take()
                if (item === CLOSE_MARK) return
                if (item === FIN_MARK) {
                    try { upstream.shutdownOutput() } catch (_: IOException) { }
                    continue
                }
                output.write(item)
                output.flush()
                pending.addAndGet(-item.size)
                lock.withLock {
                    // the app was told to wait; tell it that there is room again
                    if (state == State.OPEN && lastAdvertised < mss * 2 && window() >= RECV_WINDOW / 2) sendAck()
                }
            }
        } catch (_: IOException) {
            lock.withLock {
                if (state == State.OPEN) {
                    sendRaw(sndNext, rcvNext, RST or ACK, 0)
                    closeLocked()
                }
            }
        } catch (_: InterruptedException) {
        }
    }

    /** Called a few times a second: repeats what the app did not acknowledge, and gives up on dead connections. */
    fun tick(now: Long) {
        lock.withLock {
            if (state == State.CLOSED) return
            if (now - lastActivity > IDLE_MILLIS) {
                if (state == State.OPEN) sendRaw(sndNext, rcvNext, RST or ACK, 0)
                closeLocked()
                return
            }
            if (state != State.OPEN) return
            if (sndUna == isn) {   // the handshake is not finished
                if (now - synAckAt > 1000) {
                    if (++synAckTries > 5) closeLocked() else sendSynAck()
                }
                return
            }
            val first = unacked.peekFirst() ?: return
            if (now - first.sentAt > 400L shl minOf(first.tries, 4)) {
                if (++first.tries > 10) {
                    sendRaw(sndNext, rcvNext, RST or ACK, 0)
                    closeLocked()
                } else {
                    transmit(first)
                }
            }
        }
    }

    /** Ends the connection from our side (the tunnel is closing, or the link was lost). */
    fun abort() {
        lock.withLock {
            if (state == State.OPEN) sendRaw(sndNext, rcvNext, RST or ACK, 0)
            closeLocked()
        }
    }

    private fun closeLocked() {
        if (state == State.CLOSED) return
        state = State.CLOSED
        socket?.let(::closeQuietly)
        outgoing.clear()
        outgoing.add(CLOSE_MARK)
        changed.signalAll()
        onClosed(this)
    }

    private fun closeQuietly(s: Socket) {
        try { s.close() } catch (_: IOException) { }
    }

    internal companion object {
        const val OUR_MSS = 1360
        const val RECV_WINDOW = 65535
        const val IDLE_MILLIS = 5 * 60 * 1000L
        private val EMPTY = ByteArray(0)
        private val FIN_MARK = ByteArray(0)
        private val CLOSE_MARK = ByteArray(0)
    }
}
