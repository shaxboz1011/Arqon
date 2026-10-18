using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Arqon.App.Controls;
using Arqon.App.Infrastructure;
using Arqon.App.Services;
using Arqon.Core.Download;
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
    private double _speed;
    private string _error = "";
    private IReadOnlyList<BarPart> _parts = [];
    private string _split = "";

    public DownloadItemViewModel(DownloadsViewModel owner, DownloadRecord record)
    {
        _owner = owner;
        Record = record;
        if (record.Completed && File.Exists(record.Path))
        {
            _state = DownloadState.Done;
            _done = _total = new FileInfo(record.Path).Length;
        }
        PauseCommand = new RelayCommand(Pause, () => _state is DownloadState.Downloading or DownloadState.Queued);
        ResumeCommand = new RelayCommand(Enqueue, () => _state is DownloadState.Paused or DownloadState.Failed);
        StartNowCommand = new RelayCommand(() => _owner.StartNow(this), () => _state == DownloadState.Queued);
        CancelCommand = new RelayCommand(Cancel);
        OpenFolderCommand = new RelayCommand(OpenFolder);
    }

    /// <summary>Fixed-value item for the demo data set.</summary>
    public DownloadItemViewModel(DownloadsViewModel owner, string name, DownloadState state, long done, long total, long wifi, long phone, double speed)
        : this(owner, new DownloadRecord("https://example.com/" + name, name, name, state == DownloadState.Done))
    {
        _state = state;
        _done = done;
        _total = total;
        _speed = speed;
        ApplyParts(new Dictionary<string, long> { ["wifi"] = wifi, ["phone"] = phone });
    }

    public DownloadRecord Record { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand StartNowCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

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
    public double Percent => _total > 0 ? Math.Min(100, _done * 100.0 / _total) : 0;
    public double Done => _done;
    public IReadOnlyList<BarPart> Parts => _parts;
    public double TotalForBar => _total > 0 ? _total : 0;
    public string Split => _split;
    public string Error => _error;
    public bool HasError => _error.Length > 0;
    public double SpeedBps => IsDownloading ? _speed : 0;

    public string StateText => _state switch
    {
        DownloadState.Downloading => Loc.Get("S.Dl.State.Downloading"),
        DownloadState.Queued => Loc.Format("S.Dl.State.Queued", Math.Max(1, _owner.QueuePosition(this))),
        DownloadState.Paused => Loc.Get("S.Dl.State.Paused"),
        DownloadState.Done => Loc.Get("S.Dl.State.Done"),
        _ => Loc.Get("S.Dl.State.Failed"),
    };

    public string SizeText => _total > 0
        ? $"{Units.Size(_done)} / {Units.Size(_total)}  ({Percent:0}%)"
        : Units.Size(_done);

    public string SpeedText => IsDownloading ? $"{Units.Speed(_speed)} {Units.SpeedLabel}" : "";

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
        _ = RunAsync();
    }

    public void RefreshQueuePosition()
    {
        if (IsQueued) Raise(nameof(StateText));
    }

    private async Task RunAsync()
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
        }
        catch (OperationCanceledException)
        {
            if (_discard) _owner.Remove(this);
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
        if (_state == DownloadState.Downloading)
        {
            _cts?.Cancel();
            return;
        }
        _owner.Remove(this);
    }

    public void DeletePartialFiles()
    {
        foreach (var f in new[] { Record.Path + ".arqpart", Record.Path + ".arqstate" })
        {
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void OpenFolder()
    {
        var target = File.Exists(Record.Path) ? $"/select,\"{Record.Path}\"" : $"\"{Path.GetDirectoryName(Record.Path)}\"";
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
    private string _folder;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    public DownloadsViewModel(AppServices services)
    {
        _services = services;
        _store = new DownloadStore(DownloadStore.DefaultPath);
        _queue = new DownloadQueue(services.Settings.MaxConcurrentDownloads);
        services.SettingsChanged += OnSettingsChanged;
        _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Arqon");

        AddCommand = new RelayCommand(Add);
        PasteCommand = new RelayCommand(Paste);
        ChooseFolderCommand = new RelayCommand(ChooseFolder);
        AcceptClipboardCommand = new RelayCommand(AcceptClipboard);
        DismissClipboardCommand = new RelayCommand(DismissClipboard);

        if (services.Demo)
        {
            const long gb = 1024L * 1024 * 1024;
            Items.Add(new DownloadItemViewModel(this, "Windows11_24H2.iso", DownloadState.Downloading, (long)(3.1 * gb), (long)(5.2 * gb), (long)(1.8 * gb), (long)(1.3 * gb), 8_500_000));
            Items.Add(new DownloadItemViewModel(this, "movie-2160p.mkv", DownloadState.Queued, 0, (long)(8.4 * gb), 0, 0, 0));
            Items.Add(new DownloadItemViewModel(this, "ubuntu-24.04-desktop.iso", DownloadState.Paused, (long)(1.0 * gb), (long)(2.7 * gb), (long)(0.55 * gb), (long)(0.45 * gb), 0));
            Items.Add(new DownloadItemViewModel(this, "setup_game.exe", DownloadState.Done, 890L * 1024 * 1024, 890L * 1024 * 1024, 490L * 1024 * 1024, 400L * 1024 * 1024, 0));
        }
        else
        {
            foreach (var r in _store.Load()) Items.Add(new DownloadItemViewModel(this, r));
        }
        Items.CollectionChanged += (_, _) => Raise(string.Empty);

        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) =>
        {
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
        Items.FirstOrDefault(i => string.Equals(i.Record.Path, id, StringComparison.OrdinalIgnoreCase));

    private void Launch(IEnumerable<string> ids)
    {
        foreach (var id in ids) ItemFor(id)?.Begin();
        foreach (var queued in Items.Where(i => i.IsQueued)) queued.RefreshQueuePosition();
        Raise(nameof(QueuedCount));
    }

    /// <summary>The item joins the line and starts when the limit allows.</summary>
    public void Enqueue(DownloadItemViewModel item)
    {
        var start = _queue.Add(item.Record.Path);
        if (!start.Contains(item.Record.Path, StringComparer.OrdinalIgnoreCase)) item.MarkQueued();
        Launch(start);
    }

    /// <summary>A waiting item starts now, ahead of the line and even beyond the limit.</summary>
    public void StartNow(DownloadItemViewModel item) => Launch(_queue.StartNow(item.Record.Path));

    /// <summary>The item leaves the line without starting.</summary>
    public void Dequeue(DownloadItemViewModel item) => Launch(_queue.Remove(item.Record.Path));

    /// <summary>The item stopped running (done, failed or paused): the next one in line may start.</summary>
    public void OnEnded(DownloadItemViewModel item) => Launch(_queue.Finished(item.Record.Path));

    /// <summary>1 for the next download to start, 0 when the item is not waiting.</summary>
    public int QueuePosition(DownloadItemViewModel item) => _queue.PositionOf(item.Record.Path);

    private void OnSettingsChanged() => Launch(_queue.SetLimit(_services.Settings.MaxConcurrentDownloads));

    public int QueuedCount => Items.Count(i => i.IsQueued);


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

        var link = DownloadLinkDetector.Detect(text)?.ToString();
        var known = link is not null && Items.Any(i => string.Equals(i.Record.Url, link, StringComparison.OrdinalIgnoreCase));
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

    public string UrlText { get => _urlText; set => Set(ref _urlText, value); }
    public string Message { get => _message; private set { Set(ref _message, value); Raise(nameof(HasMessage)); } }
    public bool HasMessage => _message.Length > 0;
    public string Folder { get => _folder; private set => Set(ref _folder, value); }
    public bool IsEmpty => Items.Count == 0;
    public int ActiveCount => Items.Count(i => i.IsDownloading);
    public string TotalSpeed => $"{Units.Speed(Items.Sum(i => i.SpeedBps))} {Units.SpeedLabel}";

    public LinkThrottle Throttle => _services.Throttle;

    public IReadOnlyList<Core.Links.LinkInfo> CurrentLinks() => _services.Links.GetLinks();

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
        Items.Remove(item);
        Persist();
        Launch(_queue.Remove(item.Record.Path));
    }

    private void Add()
    {
        var text = UrlText.Trim();
        if (text.Length == 0) return;

        if (text.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            Message = Loc.Get("S.Dl.TorrentSoon");
            return;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            Message = Loc.Get("S.Dl.BadUrl");
            return;
        }

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
        if (dialog.ShowDialog() == true) Folder = dialog.FolderName;
    }
}
