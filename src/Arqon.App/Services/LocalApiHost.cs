using System.IO;
using System.Net.Sockets;
using System.Windows.Threading;
using Arqon.App.Infrastructure;
using Arqon.Core.Api;
using Arqon.Core.Diagnostics;

namespace Arqon.App.Services;

/// <summary>Runs the local API while it is switched on in Settings, and answers its questions on the UI thread.</summary>
internal sealed class LocalApiHost : ILocalApiHandler, IDisposable
{
    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;
    private readonly Func<IReadOnlyList<ApiDownload>> _downloads;
    private readonly Action<string> _add;
    private LocalApiServer? _server;

    public LocalApiHost(AppServices services, Dispatcher dispatcher, Func<IReadOnlyList<ApiDownload>> downloads, Action<string> add)
    {
        _services = services;
        _dispatcher = dispatcher;
        _downloads = downloads;
        _add = add;
        _services.SettingsChanged += Apply;
        Apply();
    }

    public static string TokenDirectory => CrashLog.DefaultDirectory;

    /// <summary>Why the API is not running although it is switched on (the port is taken), or null.</summary>
    public static string? Problem { get; private set; }

    private void Apply()
    {
        var wanted = _services.Settings.LocalApi && !_services.Demo;
        if (wanted == (_server is not null)) return;
        if (!wanted)
        {
            _server?.Dispose();
            _server = null;
            Problem = null;
            return;
        }
        try
        {
            var server = new LocalApiServer(this, ApiToken.LoadOrCreate(TokenDirectory), LocalApiServer.DefaultPort);
            server.Start();
            _server = server;
            Problem = null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            Problem = ex.Message;
        }
    }

    public ApiStatus Status()
    {
        var latest = _services.Monitor.Latest;
        var links = _services.Links.GetLinks().Select(l => latest.TryGetValue(l.Id, out var s)
            ? new ApiLink(l.Id, l.Name, l.Kind.ToString(), s.LatencyMs, s.DownBps, s.UpBps)
            : new ApiLink(l.Id, l.Name, l.Kind.ToString(), null, 0, 0)).ToList();
        var (down, up) = _services.CurrentSpeed();
        return new ApiStatus(AppInfo.Version, down, up, links);
    }

    public IReadOnlyList<ApiDownload> Downloads() => _dispatcher.Invoke(_downloads);

    public void Add(string url) => _dispatcher.BeginInvoke(() => _add(url));

    public void Dispose()
    {
        _services.SettingsChanged -= Apply;
        _server?.Dispose();
    }
}
