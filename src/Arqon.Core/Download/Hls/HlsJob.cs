using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Arqon.Core.Links;

namespace Arqon.Core.Download.Hls;

/// <summary>What was saved: the file and its container (".ts" or ".mp4").</summary>
public sealed record HlsResult(string Path, string Extension, int Segments);

/// <summary>
/// Downloads a whole HLS video (a .m3u8 playlist and its segments) over every link at once. Each link runs a few workers that take
/// the next segment from a shared line, so a faster link fetches more of them. Segments are written to the file in order;
/// a failed segment is tried again on another link. Pausing keeps what was written, and the next run goes on from there.
/// </summary>
public sealed class HlsJob(Func<LinkInfo, HttpClient>? clientFor = null)
{
    public const int WorkersPerLink = 3;
    private const int MaxAhead = 24;          // segments held in memory while an earlier one is still on its way
    private const int AttemptsPerSegment = 5;
    private static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(250);

    private readonly Func<LinkInfo, HttpClient> _clientFor = clientFor ?? BoundHttp.Create;

    private sealed record State(string Playlist, int Written, long Bytes);

    public async Task<HlsResult> RunAsync(Uri playlist, string destination, IReadOnlyList<LinkInfo> links,
        IProgress<DownloadProgress>? progress, CancellationToken ct, LinkThrottle? throttle = null)
    {
        var clients = links.Count == 0
            ? [("default", BoundHttp.CreateDefault())]
            : links.Select(l => (l.Id, _clientFor(l))).ToList();
        try
        {
            var media = await LoadMediaAsync(playlist, clients, ct).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            var part = destination + ".arqpart";
            var statePath = destination + ".arqhls";
            var state = ReadState(statePath, playlist);
            if (state is null || !File.Exists(part) || new FileInfo(part).Length < state.Bytes) state = new State(playlist.ToString(), 0, 0);

            await using (var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true))
            {
                file.SetLength(state.Bytes);
                file.Position = state.Bytes;
                await new Session(media, clients, file, state, statePath, progress, throttle).RunAsync(ct).ConfigureAwait(false);
            }

            File.Move(part, destination, overwrite: true);
            File.Delete(statePath);
            return new HlsResult(destination, media.Extension, media.Segments.Count);
        }
        finally
        {
            foreach (var (_, client) in clients) client.Dispose();
        }
    }

    /// <summary>The media playlist to download: the playlist itself, or the best stream of a master playlist.</summary>
    private static async Task<HlsMedia> LoadMediaAsync(Uri playlist, List<(string Id, HttpClient Client)> clients, CancellationToken ct)
    {
        var parsed = HlsPlaylist.Parse(await FetchTextAsync(playlist, clients, ct).ConfigureAwait(false), playlist);
        if (parsed is HlsMaster master)
        {
            var best = master.Best.Uri;
            parsed = HlsPlaylist.Parse(await FetchTextAsync(best, clients, ct).ConfigureAwait(false), best);
            if (parsed is not HlsMedia) throw new FormatException("The stream playlist is a master playlist again");
        }
        return (HlsMedia)parsed;
    }

    /// <summary>A playlist or key from whichever link answers: a link without a route must not stop the download.</summary>
    private static async Task<byte[]> FetchAsync(Uri uri, List<(string Id, HttpClient Client)> clients, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var (_, client) in clients)
        {
            try { return await client.GetByteArrayAsync(uri, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) { last = ex; }
        }
        throw last ?? new HttpRequestException("No link could fetch " + uri);
    }

    private static async Task<string> FetchTextAsync(Uri uri, List<(string Id, HttpClient Client)> clients, CancellationToken ct) =>
        System.Text.Encoding.UTF8.GetString(await FetchAsync(uri, clients, ct).ConfigureAwait(false));

    private static State? ReadState(string path, Uri playlist)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
            return state is not null && state.Playlist == playlist.ToString() && state.Written >= 0 && state.Bytes >= 0 ? state : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The AES-128 IV of a segment: the one in the playlist, or its sequence number as a 128-bit big-endian number.</summary>
    public static byte[] IvFor(HlsSegment segment)
    {
        if (segment.Key?.Iv is { } iv) return iv;
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(8), segment.Sequence);
        return bytes;
    }

    private sealed class Session(HlsMedia media, List<(string Id, HttpClient Client)> clients, FileStream file, State start, string statePath,
        IProgress<DownloadProgress>? progress, LinkThrottle? throttle)
    {
        // index -1 is the fMP4 init segment, which goes first; the lowest index is always taken first
        private readonly SortedSet<int> _line = [];
        private readonly ConcurrentDictionary<int, byte[]> _ready = new();
        private readonly ConcurrentDictionary<int, int> _attempts = new();
        private readonly ConcurrentDictionary<string, long> _perLink = new();
        private readonly ConcurrentDictionary<Uri, Task<byte[]>> _keys = new();
        private readonly SemaphoreSlim _writeGate = new(1);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _nextToWrite;
        private long _written;
        private int _segmentsWritten;
        private Exception? _fatal;
        private TimeSpan _lastReport = -ReportEvery;

        public async Task RunAsync(CancellationToken ct)
        {
            var hasInit = media.InitSegment is not null;
            var first = start.Written == 0 && hasInit ? -1 : start.Written;
            _nextToWrite = first;
            _written = start.Bytes;
            _segmentsWritten = start.Written;
            for (var i = first; i < media.Segments.Count; i++) _line.Add(i);

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var workers = clients.SelectMany(c => Enumerable.Range(0, WorkersPerLink).Select(_ => WorkAsync(c.Id, c.Client, stop))).ToList();
            await Task.WhenAll(workers).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (_fatal is not null) throw _fatal;
            if (_nextToWrite < media.Segments.Count) throw new IOException("Some segments could not be downloaded over any link");
            await file.FlushAsync(ct).ConfigureAwait(false);
            Report(force: true);
        }

        private async Task WorkAsync(string linkId, HttpClient client, CancellationTokenSource stop)
        {
            var ct = stop.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!TryTake(out var index))
                    {
                        if (Volatile.Read(ref _nextToWrite) >= media.Segments.Count) return;
                        await Task.Delay(50, ct).ConfigureAwait(false);
                        continue;
                    }

                    byte[] data;
                    try
                    {
                        data = await FetchSegmentAsync(index, linkId, client, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException && !ct.IsCancellationRequested)
                    {
                        if (_attempts.AddOrUpdate(index, 1, (_, n) => n + 1) >= AttemptsPerSegment)
                        {
                            _fatal = new IOException($"Segment {index + 1} failed {AttemptsPerSegment} times: {ex.Message}", ex);
                            stop.Cancel();
                            return;
                        }
                        lock (_line) _line.Add(index);   // another worker, often on another link, tries it again
                        await Task.Delay(200, ct).ConfigureAwait(false);
                        continue;
                    }

                    _ready[index] = data;
                    await WriteReadyAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // paused, or another worker hit a fatal error
            }
            catch (CryptographicException ex)
            {
                _fatal = new IOException("A segment could not be decrypted: " + ex.Message, ex);
                stop.Cancel();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _fatal ??= ex;   // a disk error and the like: stop the other workers too instead of leaving them waiting
                stop.Cancel();
            }
        }

        /// <summary>
        /// The lowest segment still to fetch, unless it is too far ahead of the one being written (memory stays bounded).
        /// The segment the file waits for is always allowed, so a retried segment can never be locked out.
        /// </summary>
        private bool TryTake(out int index)
        {
            lock (_line)
            {
                index = 0;
                if (_line.Count == 0) return false;
                var lowest = _line.Min;
                if (lowest >= Volatile.Read(ref _nextToWrite) + MaxAhead) return false;
                _line.Remove(lowest);
                index = lowest;
                return true;
            }
        }

        private async Task<byte[]> FetchSegmentAsync(int index, string linkId, HttpClient client, CancellationToken ct)
        {
            var uri = index < 0 ? media.InitSegment! : media.Segments[index].Uri;
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int n;
            while ((n = await body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (throttle is not null) await throttle.For(linkId).AcquireAsync(n, ct).ConfigureAwait(false);
                buffer.Write(chunk, 0, n);
                _perLink.AddOrUpdate(linkId, n, (_, v) => v + n);
            }
            var data = buffer.ToArray();
            if (index >= 0 && media.Segments[index].Key is { } key)
            {
                var keyBytes = await _keys.GetOrAdd(key.Uri, u => FetchAsync(u, clients, CancellationToken.None)).ConfigureAwait(false);
                using var aes = Aes.Create();
                aes.Key = keyBytes;
                data = aes.DecryptCbc(data, IvFor(media.Segments[index]));
            }
            return data;
        }

        /// <summary>Writes every segment that is next in order; the others wait in memory.</summary>
        private async Task WriteReadyAsync(CancellationToken ct)
        {
            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                while (_ready.TryRemove(_nextToWrite, out var data))
                {
                    await file.WriteAsync(data, ct).ConfigureAwait(false);
                    _written += data.Length;
                    _attempts.TryRemove(_nextToWrite, out _);
                    if (_nextToWrite >= 0) _segmentsWritten++;
                    Volatile.Write(ref _nextToWrite, _nextToWrite + 1);
                    if (_segmentsWritten % 10 == 0) await SaveStateAsync(ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _writeGate.Release();
            }
            Report(force: false);
        }

        /// <summary>Remembers how far the file is complete, so a pause or a crash goes on from there.</summary>
        private async Task SaveStateAsync(CancellationToken ct)
        {
            await file.FlushAsync(ct).ConfigureAwait(false);
            if (_nextToWrite <= 0) return;   // the init segment alone is not worth resuming from
            try { await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new State(start.Playlist, _nextToWrite, _written)), ct).ConfigureAwait(false); }
            catch (IOException) { /* resuming starts over, which is only slower */ }
        }

        private void Report(bool force)
        {
            if (progress is null) return;
            var now = _clock.Elapsed;
            if (!force && now - _lastReport < ReportEvery) return;
            _lastReport = now;
            var done = Interlocked.Read(ref _written);
            var count = media.Segments.Count;
            // the size is not known before the end: estimate it from the average segment so far
            var total = _segmentsWritten > 0 ? Math.Max(done, done * count / _segmentsWritten) : 0;
            if (force) total = done;
            var fetched = _perLink.Values.Sum();
            progress.Report(new DownloadProgress(total, done, new Dictionary<string, long>(_perLink),
                now.TotalSeconds > 0 ? fetched / now.TotalSeconds : 0));
        }
    }
}
