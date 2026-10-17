using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Arqon.Core.Links;
using Microsoft.Win32.SafeHandles;

namespace Arqon.Core.Download;

/// <summary>The server advertised ranges but ignores them; the job falls back to one stream.</summary>
internal sealed class RangeNotSupportedException(string message) : IOException(message);

/// <summary>The server's answer does not match what the probe saw (size changed, wrong byte range): continuing would corrupt the file.</summary>
internal sealed class RemoteChangedException(string message) : IOException(message);

/// <summary>
/// Downloads one URL over several links at once. Every link runs its own workers that pull byte ranges from a
/// shared queue, so faster links naturally take more of the file. Falls back to one stream when the server
/// cannot serve ranges. A link that keeps failing is rested for a while and then tried again.
/// </summary>
public sealed class DownloadJob
{
    private const int BufferSize = 64 * 1024;
    private static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _active;

    /// <summary>Stops the running download; <see cref="RunAsync"/> then throws OperationCanceledException and progress stays resumable.</summary>
    public void Pause() => _active?.Cancel();

    public async Task RunAsync(DownloadRequest request, IReadOnlyList<LinkInfo> links,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _active = run;

        var runners = links.Count == 0
            ? new List<Runner> { new("default", BoundHttp.CreateDefault()) }
            : links.Select(l => new Runner(l.Id, BoundHttp.Create(l))).ToList();

        try
        {
            var info = await ProbeAnyAsync(request.Url, runners, run.Token).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.DestinationPath))!);

            if (info.Length is { } length && length > 0 && info.AcceptsRanges)
            {
                try
                {
                    await new ParallelSession(request, runners, length, info.Validator, progress).RunAsync(run.Token).ConfigureAwait(false);
                    return;
                }
                catch (RangeNotSupportedException)
                {
                    // fall through to a single stream
                }
            }
            await RunSingleAsync(request, runners, info.Length, progress, run.Token).ConfigureAwait(false);
        }
        finally
        {
            foreach (var r in runners) r.Client.Dispose();
            _active = null;
        }
    }

    private static async Task<RemoteFileInfo> ProbeAnyAsync(Uri url, List<Runner> runners, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var r in runners)
        {
            try { return await RemoteFileProbe.ProbeAsync(url, r.Client, ct).ConfigureAwait(false); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { last = ex; }
        }
        throw last ?? new InvalidOperationException("No links to probe");
    }

    private static async Task RunSingleAsync(DownloadRequest request, List<Runner> runners, long? knownTotal,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var part = request.DestinationPath + ".arqpart";
        var limiter = request.SpeedLimitBps is { } bps ? new RateLimiter(bps) : null;
        Exception? last = null;
        string? finishedOn = null;
        long finishedBytes = 0;

        foreach (var runner in runners)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, request.Url);
                using var resp = await runner.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? knownTotal ?? 0;

                long done = 0;
                var clock = Stopwatch.StartNew();
                var lastReport = TimeSpan.Zero;
                long lastReportBytes = 0;
                double speed = 0;

                await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, true))
                {
                    var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
                    try
                    {
                        int n;
                        while ((n = await src.ReadAsync(buf.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                        {
                            if (limiter is not null) await limiter.AcquireAsync(n, ct).ConfigureAwait(false);
                            await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                            done += n;

                            var now = clock.Elapsed;
                            if (now - lastReport >= ReportEvery)
                            {
                                var inst = (done - lastReportBytes) / Math.Max(0.001, (now - lastReport).TotalSeconds);
                                speed = speed == 0 ? inst : speed * 0.7 + inst * 0.3;
                                lastReport = now;
                                lastReportBytes = done;
                                progress?.Report(new DownloadProgress(total, done, new Dictionary<string, long> { [runner.Id] = done }, speed));
                            }
                        }
                    }
                    finally { ArrayPool<byte>.Shared.Return(buf); }
                }

                finishedOn = runner.Id;
                finishedBytes = done;
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
            }
        }

        if (finishedOn is null)
        {
            TryDelete(part);
            throw new IOException("Download failed on every link", last);
        }

        // Outside the per-link try: a failed rename (locked file, antivirus) is not a network failure,
        // and the fully downloaded data must be kept so the user does not pay for it twice.
        File.Move(part, request.DestinationPath, overwrite: true);
        progress?.Report(new DownloadProgress(finishedBytes, finishedBytes, new Dictionary<string, long> { [finishedOn] = finishedBytes }, 0));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class Runner(string id, HttpClient client)
    {
        public readonly string Id = id;
        public readonly HttpClient Client = client;
        public int Target = ParallelSession.InitialWorkersPerLink;
        public int Active;
        public int ConsecutiveFailures;
        public long WonBytes;

        private readonly object _restLock = new();
        private long _restUntil;
        private int _backoffSeconds = 2;

        /// <summary>True while the link is resting after repeated failures; it is tried again afterwards.</summary>
        public bool Resting => Stopwatch.GetTimestamp() < Volatile.Read(ref _restUntil);

        public void Rest()
        {
            lock (_restLock)
            {
                if (Resting) return;
                Volatile.Write(ref _restUntil, Stopwatch.GetTimestamp() + _backoffSeconds * Stopwatch.Frequency);
                _backoffSeconds = Math.Min(30, _backoffSeconds * 2);
                ConsecutiveFailures = 0;
            }
        }

        public void Recovered()
        {
            lock (_restLock) _backoffSeconds = 2;
            ConsecutiveFailures = 0;
        }
    }

    private sealed class Track(ByteRange range)
    {
        public readonly ByteRange Range = range;
        public bool Completed;
        public int Racers;
        public int Failures;
        public long Received;
        public long StartedTicks;
    }

    private sealed class ParallelSession
    {
        public const int InitialWorkersPerLink = 4;
        private const int MaxWorkersPerLink = 16;
        private const int MaxChunkFailures = 8;
        private const int RestAfterFailures = 5;
        private const int StallSeconds = 20;
        private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan AdaptEvery = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RaceAfter = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan GiveUpAfterNoProgress = TimeSpan.FromSeconds(60);

        private readonly DownloadRequest _request;
        private readonly List<Runner> _runners;
        private readonly long _length;
        private readonly string? _validator;
        private readonly IProgress<DownloadProgress>? _progress;
        private readonly string _part;
        private readonly string _statePath;
        private readonly RateLimiter? _limiter;
        private readonly object _gate = new();
        private readonly ConcurrentDictionary<long, Track> _inFlight = new();
        private readonly HashSet<ByteRange> _completed = new();
        private ChunkQueue _queue = new([]);
        private SafeFileHandle _handle = null!;
        private long _completedBytes;
        private long _wire;
        private Exception? _fatal;

        public ParallelSession(DownloadRequest request, List<Runner> runners, long length, string? validator,
            IProgress<DownloadProgress>? progress)
        {
            _request = request;
            _runners = runners;
            _length = length;
            _validator = validator;
            _progress = progress;
            _part = request.DestinationPath + ".arqpart";
            _statePath = request.DestinationPath + ".arqstate";
            _limiter = request.SpeedLimitBps is { } bps ? new RateLimiter(bps) : null;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var all = ChunkPlan.Split(_length, _request.ChunkSize);

            var resume = ResumeState.TryLoad(_statePath, _request.Url, _length, _request.ChunkSize, _validator);
            if (resume is not null && File.Exists(_part) && new FileInfo(_part).Length == _length)
            {
                _completed.UnionWith(resume.ToRanges());
                _completed.IntersectWith(all);
            }
            else
            {
                TryDelete(_part);
                TryDelete(_statePath);
            }
            _completedBytes = _completed.Sum(r => r.Length);
            _queue = new ChunkQueue(all.Where(r => !_completed.Contains(r)));

            var cancelled = false;
            using (_handle = File.OpenHandle(_part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, FileOptions.Asynchronous))
            {
                if (RandomAccess.GetLength(_handle) != _length) RandomAccess.SetLength(_handle, _length);

                using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var tasks = new List<Task>();
                try
                {
                    await Supervise(work.Token, tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    cancelled = true;
                }
                finally
                {
                    work.Cancel();
                    try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { /* workers report through _fatal */ }
                    SaveState();
                    Report(0);
                }
            }

            if (cancelled) throw new OperationCanceledException(ct);

            if (_fatal is RangeNotSupportedException or RemoteChangedException)
            {
                // What is on disk can no longer be trusted (or is not needed): start clean next time.
                TryDelete(_part);
                TryDelete(_statePath);
                if (_fatal is RangeNotSupportedException notSupported) throw notSupported;
            }
            if (_fatal is not null) throw new IOException("Download failed", _fatal);

            File.Move(_part, _request.DestinationPath, overwrite: true);
            TryDelete(_statePath);
        }

        private async Task Supervise(CancellationToken token, List<Task> tasks)
        {
            var clock = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            var lastSave = TimeSpan.Zero;
            var lastAdapt = TimeSpan.Zero;
            var lastProgress = TimeSpan.Zero;
            long lastReportWire = 0, lastAdaptWire = 0, lastSeenWire = 0;
            double speed = 0, previousAdaptRate = 0;

            while (true)
            {
                if (_fatal is not null) return;
                lock (_gate) { if (_completedBytes >= _length) return; }

                var now = clock.Elapsed;
                var wireNow = Interlocked.Read(ref _wire);
                if (wireNow != lastSeenWire)
                {
                    lastSeenWire = wireNow;
                    lastProgress = now;
                }

                var alive = _runners.Where(r => !r.Resting).ToList();
                if (alive.Count == 0 && now - lastProgress > GiveUpAfterNoProgress)
                {
                    _fatal ??= new IOException("All links failed");
                    return;
                }

                foreach (var link in alive)
                {
                    while (Volatile.Read(ref link.Active) < link.Target)
                    {
                        Interlocked.Increment(ref link.Active);
                        var l = link;
                        tasks.Add(Task.Run(() => WorkerAsync(l, token), CancellationToken.None));
                    }
                }

                await Task.Delay(50, token).ConfigureAwait(false);
                now = clock.Elapsed;

                if (now - lastReport >= ReportEvery)
                {
                    var wire = Interlocked.Read(ref _wire);
                    var inst = (wire - lastReportWire) / Math.Max(0.001, (now - lastReport).TotalSeconds);
                    speed = speed == 0 ? inst : speed * 0.7 + inst * 0.3;
                    lastReport = now;
                    lastReportWire = wire;
                    Report(speed);
                }

                if (now - lastSave >= SaveEvery)
                {
                    lastSave = now;
                    SaveState();
                }

                if (now - lastAdapt >= AdaptEvery)
                {
                    // Add workers while total throughput is still growing; stop once it plateaus.
                    var wire = Interlocked.Read(ref _wire);
                    var rate = (wire - lastAdaptWire) / Math.Max(0.001, (now - lastAdapt).TotalSeconds);
                    if (rate > previousAdaptRate * 1.10)
                        foreach (var r in alive) r.Target = Math.Min(MaxWorkersPerLink, r.Target + 2);
                    previousAdaptRate = rate;
                    lastAdaptWire = wire;
                    lastAdapt = now;
                }
            }
        }

        private async Task WorkerAsync(Runner link, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !link.Resting)
                {
                    Track track;
                    var race = false;
                    if (_queue.TryTake(out var range))
                    {
                        track = _inFlight.GetOrAdd(range.Start, _ => new Track(range));
                    }
                    else
                    {
                        var raced = PickRace();
                        if (raced is null)
                        {
                            if (_inFlight.IsEmpty) return;
                            await Task.Delay(50, token).ConfigureAwait(false);
                            continue;
                        }
                        track = raced;
                        race = true;
                    }
                    await ProcessAsync(link, track, race, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { Interlocked.Decrement(ref link.Active); }
        }

        /// <summary>Endgame: when the queue is empty, an idle worker duplicates the oldest slow chunk; the first copy to finish wins.</summary>
        private Track? PickRace()
        {
            var limit = Stopwatch.GetTimestamp() - (long)(RaceAfter.TotalSeconds * Stopwatch.Frequency);
            Track? best = null;
            foreach (var t in _inFlight.Values)
            {
                lock (t)
                {
                    if (t.Completed || t.Racers != 1 || t.StartedTicks > limit) continue;
                    if (best is null || t.StartedTicks < best.StartedTicks) best = t;
                }
            }
            return best;
        }

        private async Task ProcessAsync(Runner link, Track track, bool race, CancellationToken token)
        {
            lock (track)
            {
                if (track.Completed) return;
                if (!race)
                {
                    track.StartedTicks = Stopwatch.GetTimestamp();
                    track.Received = 0;
                }
                track.Racers++;
            }

            var ok = false;
            try
            {
                await DownloadRangeAsync(link, track, token).ConfigureAwait(false);
                link.Recovered();
                Complete(link, track);
                ok = true;
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                if (ex is RangeNotSupportedException or RemoteChangedException)
                {
                    _fatal ??= ex;
                }
                else
                {
                    if (Interlocked.Increment(ref link.ConsecutiveFailures) >= RestAfterFailures) link.Rest();
                    int failures;
                    lock (track) failures = ++track.Failures;
                    if (failures > MaxChunkFailures) _fatal ??= ex;
                    try { await Task.Delay(Math.Min(1000, 100 * failures), token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
            }
            finally
            {
                lock (track)
                {
                    track.Racers--;
                    if (!ok && !track.Completed && track.Racers == 0)
                    {
                        track.Received = 0;
                        if (!token.IsCancellationRequested) _queue.Return(track.Range);
                    }
                }
            }
        }

        private async Task DownloadRangeAsync(Runner link, Track track, CancellationToken token)
        {
            var range = track.Range;
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
            stall.CancelAfter(TimeSpan.FromSeconds(StallSeconds));

            using var req = new HttpRequestMessage(HttpMethod.Get, _request.Url);
            req.Headers.Range = new RangeHeaderValue(range.Start, range.End);
            using var resp = await link.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.OK)
                throw new RangeNotSupportedException("Server ignored the Range header");
            if (resp.StatusCode != HttpStatusCode.PartialContent)
                throw new HttpRequestException($"Expected 206 Partial Content, got {(int)resp.StatusCode}");

            // The bytes must be exactly the range we asked for, of the file we probed.
            var cr = resp.Content.Headers.ContentRange;
            if (cr is null || cr.From != range.Start || cr.To != range.End || (cr.Length is { } total && total != _length))
                throw new RemoteChangedException(
                    $"Server answered with range {cr?.From}-{cr?.To}/{cr?.Length}, expected {range.Start}-{range.End}/{_length}");

            await using var stream = await resp.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                var offset = range.Start;
                while (offset <= range.End)
                {
                    stall.CancelAfter(TimeSpan.FromSeconds(StallSeconds));
                    var want = (int)Math.Min(buf.Length, range.End - offset + 1);
                    var n = await stream.ReadAsync(buf.AsMemory(0, want), stall.Token).ConfigureAwait(false);
                    if (n == 0) throw new IOException("Connection closed before the range was complete");
                    if (_limiter is not null) await _limiter.AcquireAsync(n, token).ConfigureAwait(false);
                    await RandomAccess.WriteAsync(_handle, buf.AsMemory(0, n), offset, token).ConfigureAwait(false);
                    offset += n;
                    Interlocked.Add(ref _wire, n);
                    lock (track) track.Received = Math.Max(track.Received, offset - range.Start);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buf); }
        }

        private void Complete(Runner link, Track track)
        {
            lock (track)
            {
                if (track.Completed) return;
                track.Completed = true;
            }
            lock (_gate)
            {
                // A range is only ever counted once, even if two copies of it raced to the finish.
                if (!_completed.Add(track.Range)) return;
                _completedBytes += track.Range.Length;
                link.WonBytes += track.Range.Length;
            }
            _inFlight.TryRemove(track.Range.Start, out _);
        }

        private void SaveState()
        {
            ResumeState state;
            lock (_gate)
                state = new ResumeState(_request.Url.ToString(), _length, _request.ChunkSize,
                    _completed.Select(r => new[] { r.Start, r.End }).ToList(), _validator);
            try { state.Save(_statePath); }
            catch (IOException) { /* losing one checkpoint only costs re-downloading a few chunks */ }
        }

        private void Report(double speed)
        {
            if (_progress is null) return;
            long done;
            var perLink = new Dictionary<string, long>();
            lock (_gate)
            {
                done = _completedBytes;
                foreach (var r in _runners) perLink[r.Id] = r.WonBytes;
            }
            foreach (var t in _inFlight.Values)
                lock (t) if (!t.Completed) done += t.Received;
            _progress.Report(new DownloadProgress(_length, Math.Min(done, _length), perLink, speed));
        }
    }
}
