using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Arqon.App.Controls;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Download;
using Arqon.Core.Torrents;
using Microsoft.Win32;

namespace Arqon.App.ViewModels;

public enum DownloadState { Downloading, Queued, Paused, Done, Failed }

public sealed class DownloadItemViewModel : ViewModelBase
{
    private readonly DownloadsViewModel _owner;
    private DownloadJob? _job;
    private CancellationTokenSource? _cts;
    private bool _discard;

    private DownloadState _state = DownloadState.Paused;
    private long _done, _total;
    private double _speed, _upSpeed;
    private string _error = "";
    private IReadOnlyList<BarPart> _parts = [];
    private string _split = "";
    private TorrentHandle? _torrent;
    private TorrentPhase _phase = TorrentPhase.Stopped;
    private bool _spaceChecked;
    private bool _selectionApplied;
    private bool _seeding;
    private bool _seedStarting;
    private bool _seedUnavailable;
    private long _uploaded;
    private CancellationTokenSource? _seedCts;
    private int _fileCount;
    private string? _spaceProblem;

    public DownloadItemViewModel(DownloadsViewModel owner, DownloadRecord record)
    {
        _owner = owner;
        Record = record;
        if (record.Kind == DownloadKind.Torrent)
        {
            if (record.Completed) _state = DownloadState.Done;
        }
        else if (record.Completed && File.Exists(record.Path))
        {
            _state = DownloadState.Done;
            _done = _total = new FileInfo(record.Path).Length;
        }
        PauseCommand = new RelayCommand(Pause, () => _state is DownloadState.Downloading or DownloadState.Queued);
        ResumeCommand = new RelayCommand(Enqueue, () => _state is DownloadState.Paused or DownloadState.Failed);
        StartNowCommand = new RelayCommand(() => _owner.StartNow(this), () => _state == DownloadState.Queued);
        CancelCommand = new RelayCommand(Cancel);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ChooseFilesCommand = new RelayCommand(() => _ = ChooseFilesAsync());
        StopSeedCommand = new RelayCommand(StopSeeding);
    }

    /// <summary>Fixed-value item for the demo data set.</summary>
    public DownloadItemViewModel(DownloadsViewModel owner, string name, DownloadState state, long done, long total, long wifi, long phone, double speed,
        bool torrent = false)
        : this(owner, torrent
            ? new DownloadRecord("magnet:?xt=urn:btih:" + new string('a', 40), @"C:\Users\me\Downloads", name, state == DownloadState.Done, DownloadKind.Torrent)
            : new DownloadRecord("https://example.com/" + name, name, name, state == DownloadState.Done))
    {
        _state = state;
        _done = done;
        _total = total;
        _speed = speed;
        ApplyParts(new Dictionary<string, long> { ["wifi"] = wifi, ["phone"] = phone });
        if (torrent)
        {
            _upSpeed = speed / 12;
            _phase = TorrentPhase.Downloading;
            _demoTorrent = true;
        }
    }

    private readonly bool _demoTorrent;

    // The demo torrent's numbers are fixed, but the words around them follow the language chosen at the moment.
    private string DemoSplit() => Loc.Format("S.Dl.Peers", 31, 9) + "  ·  " + _owner.NameFor("wifi") + " 17  ·  " + _owner.NameFor("phone") + " 14";

    public DownloadRecord Record { get; private set; }
    public bool IsTorrent => Record.Kind == DownloadKind.Torrent;
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand StartNowCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand ChooseFilesCommand { get; }
    public RelayCommand StopSeedCommand { get; }

    public string Name => Record.Name;
    public DownloadState State => _state;
    public bool IsDownloading => _state == DownloadState.Downloading;
    public bool IsQueued => _state == DownloadState.Queued;
    public bool IsDone => _state == DownloadState.Done;
    public bool IsPaused => _state == DownloadState.Paused;
    public bool IsFailed => _state == DownloadState.Failed;
    public bool CanPause => IsDownloading || IsQueued;
    public bool CanResume => IsPaused || IsFailed;
    public bool CanStartNow => IsQueued;
    /// <summary>A finished torrent that is still being shared with other people.</summary>
    public bool IsSeeding => IsDone && _seeding;
    public bool CanChooseFiles => IsTorrent && !IsDone && _fileCount > 1;
    public double Percent => _total > 0 ? Math.Min(100, _done * 100.0 / _total) : 0;
    public double Done => _done;
    public IReadOnlyList<BarPart> Parts => _parts;
    public double TotalForBar => _total > 0 ? _total : 0;
    public string Split => _demoTorrent ? DemoSplit() : _split;
    public string Error => _error;
    public bool HasError => _error.Length > 0;
    public double SpeedBps => IsDownloading ? _speed : 0;

    public string StateText => _state switch
    {
        DownloadState.Downloading when IsTorrent && _phase == TorrentPhase.Metadata => Loc.Get("S.Dl.State.Metadata"),
        DownloadState.Downloading when IsTorrent && _phase == TorrentPhase.Checking => Loc.Get("S.Dl.State.Checking"),
        DownloadState.Downloading => Loc.Get("S.Dl.State.Downloading"),
        DownloadState.Queued => Loc.Format("S.Dl.State.Queued", Math.Max(1, _owner.QueuePosition(this))),
        DownloadState.Paused => Loc.Get("S.Dl.State.Paused"),
        DownloadState.Done when IsSeeding => Loc.Format("S.Dl.State.Seeding", (_total > 0 ? (double)_uploaded / _total : 0).ToString("0.00")),
        DownloadState.Done => Loc.Get("S.Dl.State.Done"),
        _ => Loc.Get("S.Dl.State.Failed"),
    };

    public string SizeText => _total > 0
        ? $"{Units.Size(_done)} / {Units.Size(_total)}  ({Percent:0}%)"
        : IsTorrent && _done == 0 ? "" : Units.Size(_done);

    public string SpeedText => IsSeeding ? $"↑ {Units.Speed(_upSpeed)} {Units.SpeedLabel}"
        : !IsDownloading ? ""
        : IsTorrent ? $"{Units.Speed(_speed)} {Units.SpeedLabel}   ↑ {Units.Speed(_upSpeed)}"
        : $"{Units.Speed(_speed)} {Units.SpeedLabel}";

    public string EtaText
    {
        get
        {
            if (!IsDownloading || _speed < 1 || _total <= 0) return "";
            return Loc.Format("S.Dl.Eta", Units.Duration(TimeSpan.FromSeconds((_total - _done) / _speed)));
        }
    }

    private bool _running;

    /// <summary>Joins the queue; it starts when there is room (at once if the limit allows).</summary>
    public void Enqueue() => _owner.Enqueue(this);

    public void MarkQueued() => SetState(DownloadState.Queued);

    /// <summary>Called by the owner when the queue says it is this download's turn.</summary>
    public void Begin()
    {
        // Guards double-clicks: two jobs on one file would fight over it.
        if (_running) return;
        _running = true;
        _discard = false;
        _spaceChecked = false;
        _spaceProblem = null;
        _ = RunAsync();
    }

    /// <summary>Called once the size is known: a download that cannot fit on the drive is stopped with a clear message.</summary>
    private void CheckSpace(long remaining)
    {
        _spaceChecked = true;
        var folder = IsTorrent ? Record.Path : Path.GetDirectoryName(Record.Path);
        if (string.IsNullOrEmpty(folder) || DiskSpace.FreeBytes(folder) is not { } free || DiskSpace.HasRoom(free, remaining)) return;

        _spaceProblem = Loc.Format("S.Dl.NoSpace", Units.Size(remaining), Units.Size(free));
        _cts?.Cancel();
    }

    public void RefreshQueuePosition()
    {
        if (IsQueued) Raise(nameof(StateText));
    }

    private Task RunAsync() => IsTorrent ? RunTorrentAsync() : RunHttpAsync();

    private async Task RunTorrentAsync()
    {
        SetState(DownloadState.Downloading);
        _error = "";
        _phase = TorrentPhase.Metadata;
        _selectionApplied = false;
        _seeding = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            var engine = _owner.TorrentEngine();
            _torrent = Record.Url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
                ? await engine.AddMagnetAsync(Record.Url, Record.Path)
                : await engine.AddFileAsync(Record.Url, Record.Path);
            await _torrent.StartAsync();

            while (true)
            {
                await ApplySavedSelectionAsync();
                var snapshot = _torrent.Snapshot();
                ApplyTorrent(snapshot);
                if (snapshot.Phase == TorrentPhase.Complete) break;
                if (snapshot.Phase == TorrentPhase.Error) throw new IOException(snapshot.Error ?? "torrent error");
                await Task.Delay(500, token);
            }

            _speed = 0;
            if (_owner.SeedTorrents) _seeding = true;   // stays running: SeedAsync shares it and stops it later
            else
            {
                await _torrent.StopAsync();
                _upSpeed = 0;
            }
            SetState(DownloadState.Done);
            _owner.OnCompleted(this);
        }
        catch (OperationCanceledException)
        {
            await StopTorrentQuietly();
            if (_spaceProblem is not null)
            {
                _error = _spaceProblem;
                SetState(DownloadState.Failed);
            }
            else if (_discard)
            {
                await DropTorrentAsync(deleteData: true);
                _owner.Remove(this);
            }
            else SetState(DownloadState.Paused);
        }
        catch (Exception ex)
        {
            await StopTorrentQuietly();
            _error = ex.InnerException?.Message ?? ex.Message;
            SetState(DownloadState.Failed);
        }
        finally
        {
            _running = false;
        }
        _owner.Persist();
        _owner.OnEnded(this);   // seeding does not hold a place in the queue
        if (_seeding) _ = SeedAsync();
    }

    /// <summary>
    /// Shares a finished torrent until it has uploaded the chosen ratio, the setting is switched off, or the user stops it.
    /// Only for torrents finished in this session: after a restart a finished torrent is not started again on its own.
    /// </summary>
    private async Task SeedAsync()
    {
        _seedCts = new CancellationTokenSource();
        var token = _seedCts.Token;
        try
        {
            while (true)
            {
                await Task.Delay(1000, token);
                var torrent = _torrent;
                if (torrent is null || !_owner.SeedTorrents) break;   // removed from the list, or seeding was switched off
                var snapshot = torrent.Snapshot();
                _uploaded = snapshot.Uploaded;
                _upSpeed = snapshot.UpBps;
                Raise(string.Empty);
                if (SeedPolicy.ReachedTarget(snapshot.Uploaded, _total, _owner.SeedRatio)) break;
            }
        }
        catch (OperationCanceledException)
        {
            // stopped by hand
        }
        await StopTorrentQuietly();
        _seeding = false;
        _upSpeed = 0;
        _seedCts = null;
        Raise(string.Empty);
    }

    private void StopSeeding() => _seedCts?.Cancel();

    /// <summary>Seeding was switched on again: a torrent whose files were missing before gets another look.</summary>
    public void RetrySeeding() => _seedUnavailable = false;

    /// <summary>
    /// Starts sharing a torrent that finished in an earlier session (or earlier in this one and was stopped). It only works when
    /// all wanted files are still on disk: a deleted file never turns this into a download, the item simply stays "Finished".
    /// </summary>
    public async Task ResumeSeedingAsync()
    {
        if (!IsTorrent || !IsDone || _seeding || _seedStarting || _seedUnavailable || _running || _owner.IsDemo || !_owner.SeedTorrents) return;
        _seedStarting = true;
        try
        {
            var handle = await _owner.TorrentEngine().OpenForSeedingAsync(Record.Url, Record.Path, Record.Files?.ToHashSet());
            if (handle is null)
            {
                _seedUnavailable = true;   // its files are gone: do not look again until seeding is switched on anew
                return;
            }
            if (!_owner.Items.Contains(this))
            {
                await handle.RemoveAsync(deleteData: false);   // removed from the list while its files were being checked
                return;
            }
            _torrent = handle;
            _total = _done = handle.Snapshot().Size;
            _uploaded = 0;
            _seeding = true;
            Raise(string.Empty);
            _ = SeedAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // sharing is a bonus; a failure leaves a normal finished download
        }
        finally
        {
            _seedStarting = false;
        }
    }

    /// <summary>
    /// The files are known once the metadata is there (at once for a .torrent file, later for a magnet link). The choice made
    /// earlier is applied then, before any data of an unwanted file is requested for long.
    /// </summary>
    private async Task ApplySavedSelectionAsync()
    {
        if (_torrent is null || _selectionApplied) return;
        var files = _torrent.Files;
        if (files.Count == 0) return;
        _selectionApplied = true;
        if (Record.Files is { } chosen) await _torrent.SelectFilesAsync(chosen.ToHashSet());
        if (_fileCount != files.Count)
        {
            _fileCount = files.Count;
            Raise(nameof(CanChooseFiles));
        }
    }

    private async Task ChooseFilesAsync()
    {
        if (_torrent is null) return;
        var files = _torrent.Files;
        if (files.Count < 2) return;
        var window = new Views.TorrentFilesWindow(Record.Name, files, Record.Files?.ToHashSet())
        {
            Owner = Application.Current.MainWindow,
        };
        if (window.ShowDialog() != true) return;

        var chosen = window.Selected;
        // Everything chosen is stored as "all" so a later change of the file list cannot leave anything out.
        Record = Record with { Files = chosen.Count == files.Count ? null : chosen.Order().ToList() };
        _owner.Persist();
        try { await _torrent.SelectFilesAsync(chosen); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _error = ex.Message; Raise(nameof(Error)); Raise(nameof(HasError)); }
    }

    private async Task StopTorrentQuietly()
    {
        try { if (_torrent is not null) await _torrent.StopAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* it is being stopped anyway */ }
    }

    /// <summary>
    /// Takes the torrent out of the engine. A torrent that was not started in this session is not known to the engine yet;
    /// to delete its files it is added once more and removed again, so only its own files go.
    /// </summary>
    public async Task DropTorrentAsync(bool deleteData)
    {
        try
        {
            if (_torrent is not null) await _torrent.RemoveAsync(deleteData);
            else if (deleteData && !_owner.IsDemo) await _owner.TorrentEngine().DiscardAsync(Record.Url, Record.Path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* nothing more to clean up */ }
        _torrent = null;
    }

    private void ApplyTorrent(TorrentSnapshot s)
    {
        _phase = s.Phase;
        _done = s.Done;
        _total = s.Size;
        _speed = s.DownBps;
        _upSpeed = s.UpBps;
        if (!_spaceChecked && s.Size > 0) CheckSpace(s.Size - s.Done);
        if (s.Name.Length > 0 && s.Name != Record.Name)
        {
            Record = Record with { Name = s.Name };   // a magnet link only learns its name once the metadata arrives
            _owner.Persist();
        }

        var perLink = _owner.TorrentConnectionsPerLink();
        _parts = perLink.Select(kv => new BarPart(_owner.ColorFor(kv.Key), kv.Value)).ToList();
        var connections = Loc.Format("S.Dl.Peers", s.Peers, s.Seeds);
        _split = perLink.Count == 0 ? connections
            : connections + "  ·  " + string.Join("  ·  ", perLink.Select(kv => $"{_owner.NameFor(kv.Key)} {kv.Value}"));
        Raise(string.Empty);
    }

    private async Task RunHttpAsync()
    {
        SetState(DownloadState.Downloading);
        _error = "";
        _cts = new CancellationTokenSource();
        _job = new DownloadJob();
        var progress = new Progress<DownloadProgress>(OnProgress);
        try
        {
            await _job.RunAsync(new DownloadRequest(new Uri(Record.Url), Record.Path, Throttle: _owner.Throttle), _owner.CurrentLinks(), progress, _cts.Token)
                .ConfigureAwait(true);
            _done = _total = new FileInfo(Record.Path).Length;
            _speed = 0;
            SetState(DownloadState.Done);
            _owner.OnCompleted(this);
        }
        catch (OperationCanceledException)
        {
            if (_spaceProblem is not null)
            {
                _error = _spaceProblem;
                SetState(DownloadState.Failed);
            }
            else if (_discard) _owner.Remove(this);
            else SetState(DownloadState.Paused);
        }
        catch (Exception ex)
        {
            _error = ex.InnerException?.Message ?? ex.Message;
            SetState(DownloadState.Failed);
        }
        finally
        {
            _running = false;
        }
        _owner.Persist();
        _owner.OnEnded(this);   // its place goes to the next download in the queue
    }

    private void OnProgress(DownloadProgress p)
    {
        _done = p.Done;
        _total = p.Total;
        _speed = p.BytesPerSecond;
        if (!_spaceChecked && p.Total > 0) CheckSpace(p.Total - p.Done);
        ApplyParts(p.PerLinkBytes);
        Raise(string.Empty);
    }

    private void ApplyParts(IReadOnlyDictionary<string, long> perLink)
    {
        _parts = perLink.Select(kv => new BarPart(_owner.ColorFor(kv.Key), kv.Value)).ToList();
        var sum = perLink.Values.Sum();
        _split = sum <= 0 ? "" : string.Join("  ·  ", perLink.Where(kv => kv.Value > 0)
            .Select(kv => $"{_owner.NameFor(kv.Key)} {kv.Value * 100.0 / sum:0}%"));
    }

    private void SetState(DownloadState state)
    {
        _state = state;
        Raise(string.Empty);
    }

    private void Pause()
    {
        if (_state == DownloadState.Queued)
        {
            _owner.Dequeue(this);
            SetState(DownloadState.Paused);
            return;
        }
        _cts?.Cancel();
    }

    private void Cancel()
    {
        _discard = true;
        _seedCts?.Cancel();
        if (_state == DownloadState.Downloading)
        {
            _cts?.Cancel();
            return;
        }
        _owner.Remove(this);
    }

    public void DeletePartialFiles()
    {
        if (IsTorrent) return;   // a torrent's files are removed through the engine, which knows exactly which ones are its own
        foreach (var f in new[] { Record.Path + ".arqpart", Record.Path + ".arqstate" })
        {
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void OpenFolder()
    {
        // A torrent is saved into the chosen folder under its own name; show that, or the folder itself while nothing is there yet.
        var item = IsTorrent ? Path.Combine(Record.Path, Record.Name) : Record.Path;
        var target = File.Exists(item) || Directory.Exists(item)
            ? $"/select,\"{item}\""
            : $"\"{(IsTorrent ? Record.Path : Path.GetDirectoryName(Record.Path))}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
    }
}

public sealed class DownloadsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly DownloadStore _store;
    private readonly DownloadQueue _queue;
    private string _urlText = "";
    private string _message = "";

    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    public DownloadsViewModel(AppServices services)
    {
        _services = services;
        _store = new DownloadStore(DownloadStore.DefaultPath);
        _queue = new DownloadQueue(services.Settings.MaxConcurrentDownloads);
        services.SettingsChanged += OnSettingsChanged;

        AddCommand = new RelayCommand(Add);
        PasteCommand = new RelayCommand(Paste);
        ChooseFolderCommand = new RelayCommand(ChooseFolder);
        ResetFolderCommand = new RelayCommand(ResetFolder);
        AddTorrentFileCommand = new RelayCommand(ChooseTorrentFile);
        AcceptClipboardCommand = new RelayCommand(AcceptClipboard);
        DismissClipboardCommand = new RelayCommand(DismissClipboard);

        if (services.Demo)
        {
            const long gb = 1024L * 1024 * 1024;
            Items.Add(new DownloadItemViewModel(this, "Windows11_24H2.iso", DownloadState.Downloading, (long)(3.1 * gb), (long)(5.2 * gb), (long)(1.8 * gb), (long)(1.3 * gb), 8_500_000));
            Items.Add(new DownloadItemViewModel(this, "Big.Buck.Bunny.1080p", DownloadState.Downloading, (long)(0.62 * gb), (long)(1.4 * gb), (long)(0.36 * gb), (long)(0.26 * gb), 6_200_000, torrent: true));
            Items.Add(new DownloadItemViewModel(this, "movie-2160p.mkv", DownloadState.Queued, 0, (long)(8.4 * gb), 0, 0, 0));
            Items.Add(new DownloadItemViewModel(this, "ubuntu-24.04-desktop.iso", DownloadState.Paused, (long)(1.0 * gb), (long)(2.7 * gb), (long)(0.55 * gb), (long)(0.45 * gb), 0));
            Items.Add(new DownloadItemViewModel(this, "setup_game.exe", DownloadState.Done, 890L * 1024 * 1024, 890L * 1024 * 1024, 490L * 1024 * 1024, 400L * 1024 * 1024, 0));
        }
        else
        {
            foreach (var r in _store.Load()) Items.Add(new DownloadItemViewModel(this, r));
        }
        Items.CollectionChanged += (_, _) => Raise(string.Empty);
        _ = ResumeSeedingAsync(TimeSpan.FromSeconds(5));

        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ApplySchedule();
        _tick.Tick += (_, _) =>
        {
            ApplySchedule();
            Raise(nameof(TotalSpeed));
            Raise(nameof(ActiveCount));
            Raise(nameof(QueuedCount));
        };
        _tick.Start();
    }

    private readonly System.Windows.Threading.DispatcherTimer _tick;

    public override void Dispose()
    {
        _tick.Stop();
        _services.SettingsChanged -= OnSettingsChanged;
        base.Dispose();
    }

    // ---- queue ----------------------------------------------------------------------------------------------

    private DownloadItemViewModel? ItemFor(string id) =>
        Items.FirstOrDefault(i => string.Equals(i.Record.Key, id, StringComparison.OrdinalIgnoreCase));

    private void Launch(IEnumerable<string> ids)
    {
        foreach (var id in ids) ItemFor(id)?.Begin();
        foreach (var queued in Items.Where(i => i.IsQueued)) queued.RefreshQueuePosition();
        Raise(nameof(QueuedCount));
    }

    /// <summary>The item joins the line and starts when the limit allows.</summary>
    public void Enqueue(DownloadItemViewModel item)
    {
        var start = _queue.Add(item.Record.Key);
        if (!start.Contains(item.Record.Key, StringComparer.OrdinalIgnoreCase)) item.MarkQueued();
        Launch(start);
    }

    /// <summary>A waiting item starts now, ahead of the line and even beyond the limit.</summary>
    public void StartNow(DownloadItemViewModel item) => Launch(_queue.StartNow(item.Record.Key));

    /// <summary>The item leaves the line without starting.</summary>
    public void Dequeue(DownloadItemViewModel item) => Launch(_queue.Remove(item.Record.Key));

    /// <summary>The item stopped running (done, failed or paused): the next one in line may start.</summary>
    public void OnEnded(DownloadItemViewModel item) => Launch(_queue.Finished(item.Record.Key));

    /// <summary>A download finished: say so (tray balloon), because it may have taken a while.</summary>
    public void OnCompleted(DownloadItemViewModel item) => _services.NotifyDownloadDone(item.Name);

    /// <summary>1 for the next download to start, 0 when the item is not waiting.</summary>
    public int QueuePosition(DownloadItemViewModel item) => _queue.PositionOf(item.Record.Key);

    private bool _seedingPass;

    /// <summary>
    /// Finished torrents are shared again when seeding is on: after a start of Arqon (a few seconds late, so start-up is not
    /// slowed down and the firewall question does not appear at once) and whenever the setting is switched on.
    /// One at a time, because checking the files of a big torrent is heavy.
    /// </summary>
    private async Task ResumeSeedingAsync(TimeSpan delay)
    {
        if (_seedingPass || _services.Demo || !_services.Settings.SeedTorrents) return;
        _seedingPass = true;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            foreach (var item in Items.ToList()) await item.ResumeSeedingAsync();
        }
        finally
        {
            _seedingPass = false;
        }
    }

    private bool _seedWasOn;

    private void OnSettingsChanged()
    {
        var seedOn = _services.Settings.SeedTorrents;
        if (seedOn && !_seedWasOn) foreach (var item in Items) item.RetrySeeding();
        _seedWasOn = seedOn;
        _ = ResumeSeedingAsync(TimeSpan.Zero);
        Launch(_queue.SetLimit(_services.Settings.MaxConcurrentDownloads));
        ApplySchedule();
        Raise(nameof(Folder));
        Raise(nameof(FolderText));
        Raise(nameof(FolderIsCustom));
        Raise(nameof(FolderIsAuto));
    }

    public int QueuedCount => Items.Count(i => i.IsQueued);

    // ---- schedule: queued downloads start only inside the chosen hours ----------------------------------------

    private DownloadSchedule Schedule => new(_services.Settings.ScheduleEnabled, _services.Settings.ScheduleStartHour, _services.Settings.ScheduleEndHour);

    private void ApplySchedule()
    {
        var held = !Schedule.IsOpen(TimeOnly.FromDateTime(DateTime.Now));
        if (held == _queue.Held) return;
        Launch(_queue.SetHeld(held));
        Raise(nameof(ScheduleNote));
        Raise(nameof(HasScheduleNote));
    }

    public bool HasScheduleNote => _queue.Held;

    public string ScheduleNote => _queue.Held ? Loc.Format("S.Dl.Scheduled", Schedule.NextOpen(DateTime.Now).ToString("HH:mm")) : "";


    private string? _clipboardLink;
    private string? _dismissedClipboard;

    public RelayCommand AcceptClipboardCommand { get; private set; } = null!;
    public RelayCommand DismissClipboardCommand { get; private set; } = null!;

    /// <summary>A file link found on the clipboard that the user has not added or dismissed yet.</summary>
    public string? ClipboardLink
    {
        get => _clipboardLink;
        private set { if (Set(ref _clipboardLink, value)) Raise(nameof(HasClipboardLink)); }
    }

    public bool HasClipboardLink => _clipboardLink is not null;

    /// <summary>Call when the window is activated or this page is shown; offers a copied download link.</summary>
    public void CheckClipboard()
    {
        if (!_services.Settings.ClipboardWatch) { ClipboardLink = null; return; }

        string? text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy: try again next time */ }

        var link = TorrentInput.Classify(text) == InputKind.Magnet ? text!.Trim() : DownloadLinkDetector.Detect(text)?.ToString();
        var magnetHash = link is not null && link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? TorrentIdentity.OfMagnet(link) : null;
        var known = link is not null && Items.Any(i => string.Equals(i.Record.Url, link, StringComparison.OrdinalIgnoreCase)
                                                       || (magnetHash is not null && i.IsTorrent && TorrentSource.HashOf(i.Record.Url) == magnetHash));
        ClipboardLink = link is null || known || link == _dismissedClipboard ? null : link;
    }

    private void AcceptClipboard()
    {
        if (_clipboardLink is null) return;
        UrlText = _clipboardLink;
        ClipboardLink = null;
        Add();
    }

    private void DismissClipboard()
    {
        _dismissedClipboard = _clipboardLink;
        ClipboardLink = null;
    }

    public RelayCommand AddCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand ResetFolderCommand { get; }
    public RelayCommand AddTorrentFileCommand { get; }

    public string UrlText { get => _urlText; set => Set(ref _urlText, value); }
    public string Message { get => _message; private set { Set(ref _message, value); Raise(nameof(HasMessage)); } }
    public bool HasMessage => _message.Length > 0;

    /// <summary>Where new downloads go: the folder chosen in the app, or by default the Downloads folder of Windows.</summary>
    public string Folder => DownloadFolder.Resolve(_services.Settings.DownloadDirectory, DownloadFolder.Windows());

    /// <summary>What the page shows for <see cref="Folder"/>; the demo shows a neutral path instead of the real user name.</summary>
    public string FolderText => _services.Demo && !FolderIsCustom ? @"C:\Users\you\Downloads" : Folder;

    public bool FolderIsCustom => DownloadFolder.IsCustom(_services.Settings.DownloadDirectory);
    public bool FolderIsAuto => !FolderIsCustom;
    public bool IsEmpty => Items.Count == 0;
    public int ActiveCount => Items.Count(i => i.IsDownloading);
    public string TotalSpeed => $"{Units.Speed(Items.Sum(i => i.SpeedBps))} {Units.SpeedLabel}";

    public LinkThrottle Throttle => _services.Throttle;

    public IReadOnlyList<Core.Links.LinkInfo> CurrentLinks() => _services.DownloadLinks();

    public Color ColorFor(string linkId)
    {
        var link = _services.Links.GetLinks().FirstOrDefault(l => l.Id == linkId);
        return link is null ? Color.FromRgb(0x6C, 0x8C, 0xFF) : LinkPalette.ColorFor(link.Kind);
    }

    public string NameFor(string linkId) =>
        _services.Links.GetLinks().FirstOrDefault(l => l.Id == linkId)?.Name ?? linkId;

    public void Persist()
    {
        if (_services.Demo) return;
        try { _store.Save(Items.Select(i => i.Record with { Completed = i.IsDone })); }
        catch (IOException) { /* the list is a convenience; downloads themselves are unaffected */ }
        Raise(string.Empty);
    }

    public void Remove(DownloadItemViewModel item)
    {
        if (!item.IsDone) item.DeletePartialFiles();
        if (item.IsTorrent) _ = item.DropTorrentAsync(deleteData: !item.IsDone);
        Items.Remove(item);
        Persist();
        Launch(_queue.Remove(item.Record.Key));
    }

    // ---- torrents ---------------------------------------------------------------------------------------------

    public bool IsDemo => _services.Demo;

    public bool SeedTorrents => _services.Settings.SeedTorrents;

    public double SeedRatio => _services.Settings.SeedRatio;

    public TorrentEngine TorrentEngine() => _services.Torrents;

    /// <summary>How many peer connections each link received, for the Wi-Fi / phone split shown on a torrent.</summary>
    public IReadOnlyDictionary<string, int> TorrentConnectionsPerLink() => _services.TorrentConnectionsPerLink();

    private void ChooseTorrentFile()
    {
        var dialog = new OpenFileDialog { Title = Loc.Get("S.Dl.AddTorrent"), Filter = "Torrent (*.torrent)|*.torrent", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var file in dialog.FileNames) AddTorrentFile(file);
    }

    private void AddMagnet(string magnet)
    {
        var hash = TorrentIdentity.OfMagnet(magnet);
        if (hash is null) { Message = Loc.Get("S.Dl.BadMagnet"); return; }
        if (Items.Any(i => i.IsTorrent && TorrentSource.HashOf(i.Record.Url) == hash)) { Message = Loc.Get("S.Dl.AlreadyAdded"); return; }

        var name = MonoTorrent.MagnetLink.TryParse(magnet.Trim(), out var parsed) && !string.IsNullOrWhiteSpace(parsed?.Name) ? parsed!.Name! : hash;
        AddTorrentItem(new DownloadRecord(magnet.Trim(), Folder, name, false, DownloadKind.Torrent));
    }

    private void AddTorrentFile(string path)
    {
        var info = TorrentIdentity.OfFile(path);
        if (info is null) { Message = Loc.Get("S.Dl.BadTorrent"); return; }
        if (Items.Any(i => i.IsTorrent && TorrentSource.HashOf(i.Record.Url) == info.Hash)) { Message = Loc.Get("S.Dl.AlreadyAdded"); return; }

        // A copy is kept, so the torrent still resumes after the original file is moved or deleted.
        var copy = TorrentSource.SaveCopy(File.ReadAllBytes(path), info.Hash);
        AddTorrentItem(new DownloadRecord(copy, Folder, info.Name, false, DownloadKind.Torrent));
    }

    private async Task AddTorrentUrlAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var bytes = await http.GetByteArrayAsync(url);
            if (bytes.Length > 10 * 1024 * 1024) throw new InvalidDataException("too large for a .torrent file");
            var info = TorrentIdentity.OfBytes(bytes);
            if (info is null) { Message = Loc.Get("S.Dl.BadTorrent"); return; }
            if (Items.Any(i => i.IsTorrent && TorrentSource.HashOf(i.Record.Url) == info.Hash)) { Message = Loc.Get("S.Dl.AlreadyAdded"); return; }

            AddTorrentItem(new DownloadRecord(TorrentSource.SaveCopy(bytes, info.Hash), Folder, info.Name, false, DownloadKind.Torrent));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
        {
            Message = Loc.Format("S.Dl.TorrentDownloadFailed", ex.Message);
        }
    }

    private void AddTorrentItem(DownloadRecord record)
    {
        var item = new DownloadItemViewModel(this, record);
        Items.Insert(0, item);
        UrlText = "";
        Message = "";
        Persist();
        item.Enqueue();
    }

    /// <summary>Adds what came from outside the box (a request from another copy, a drop on the window).</summary>
    public void AddFromText(string text)
    {
        UrlText = text;
        Add();
    }

    private void Add()
    {
        var text = UrlText.Trim();
        if (text.Length == 0) return;

        switch (TorrentInput.Classify(text))
        {
            case InputKind.Magnet: AddMagnet(text); return;
            case InputKind.TorrentFile: AddTorrentFile(TorrentInput.LocalPath(text)); return;
            case InputKind.TorrentUrl: _ = AddTorrentUrlAsync(text); return;
            case InputKind.Invalid: Message = Loc.Get("S.Dl.BadUrl"); return;
        }
        var uri = new Uri(text);

        Directory.CreateDirectory(Folder);
        var name = Path.GetFileName(Uri.UnescapeDataString(uri.LocalPath));
        if (string.IsNullOrWhiteSpace(name)) name = "download";
        var path = UniquePath(Folder, name, Items.Select(i => i.Record.Path));

        var item = new DownloadItemViewModel(this, new DownloadRecord(uri.ToString(), path, Path.GetFileName(path), false));
        Items.Insert(0, item);
        UrlText = "";
        Message = "";
        Persist();
        item.Enqueue();
    }

    /// <summary>A path no file on disk and no download already in the list uses (the part file only appears after the probe).</summary>
    private static string UniquePath(string folder, string name, IEnumerable<string> reserved)
    {
        var taken = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 1; File.Exists(path) || File.Exists(path + ".arqpart") || taken.Contains(path); i++)
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
        return path;
    }

    private void Paste()
    {
        try
        {
            if (Clipboard.ContainsText()) UrlText = Clipboard.GetText().Trim();
        }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy */ }
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = Folder, Title = Loc.Get("S.Dl.Folder") };
        if (dialog.ShowDialog() == true)
            _services.SaveSettings(_services.Settings with { DownloadDirectory = dialog.FolderName });
    }

    /// <summary>Back to automatic: the Downloads folder of Windows.</summary>
    private void ResetFolder() => _services.SaveSettings(_services.Settings with { DownloadDirectory = null });
}
