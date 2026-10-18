using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Arqon.Core.Links;

namespace Arqon.Core.Steering;

public enum SteererAvailability { Ready, DriverMissing, NotAdmin }

/// <summary>
/// Applies <see cref="NatSteeringEngine"/> to real traffic through the WinDivert driver. Fail-open by construction:
/// the driver only diverts packets while our handle is open, so closing it (Stop, a fault, or the process dying)
/// returns every connection to normal Windows routing.
/// </summary>
public sealed class WinDivertSteerer : ISteerer, IDisposable
{
    private const int MaxPacket = 65535 + 64;
    private const int MaxConsecutiveSendFailures = 200;
    private const int MaxEngineErrors = 100;
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private sealed class Session(IntPtr handle, string filter, Dictionary<IPAddress, uint> interfaceIndex)
    {
        public readonly IntPtr Handle = handle;
        public readonly string Filter = filter;
        public readonly Dictionary<IPAddress, uint> InterfaceIndex = interfaceIndex;
        public readonly List<Thread> Workers = [];
        public volatile bool Closing;
    }

    private readonly NatSteeringEngine _engine;
    private readonly Func<IReadOnlyList<LinkInfo>> _links;
    private readonly Func<IPAddress?> _defaultAddress;
    private readonly string _directory;
    private readonly object _gate = new();
    private Session? _session;
    private CancellationTokenSource? _cts;
    private int _engineErrors;

    public WinDivertSteerer(NatSteeringEngine engine, Func<IReadOnlyList<LinkInfo>> links, Func<IPAddress?> defaultAddress,
        string? directory = null)
    {
        _engine = engine;
        _links = links;
        _defaultAddress = defaultAddress;
        _directory = directory ?? AppContext.BaseDirectory;
    }

    public event Action<string>? Faulted;

    public bool IsAvailable => CheckAvailability(_directory, IsAdministrator()) == SteererAvailability.Ready;

    public string? UnavailableReason => CheckAvailability(_directory, IsAdministrator()) switch
    {
        SteererAvailability.DriverMissing => "driver-missing",
        SteererAvailability.NotAdmin => "not-admin",
        _ => null,
    };

    public static SteererAvailability CheckAvailability(string directory, bool isAdmin)
    {
        var sys = Environment.Is64BitProcess ? "WinDivert64.sys" : "WinDivert32.sys";
        if (!File.Exists(Path.Combine(directory, "WinDivert.dll")) || !File.Exists(Path.Combine(directory, sys)))
            return SteererAvailability.DriverMissing;
        return isAdmin ? SteererAvailability.Ready : SteererAvailability.NotAdmin;
    }

    public static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            _engineErrors = 0;
            try
            {
                Reconfigure();   // throws on a driver problem, so the caller sees it immediately
            }
            catch
            {
                _cts = null;
                throw;
            }

            var token = _cts.Token;
            new Thread(() => Maintain(token)) { IsBackground = true, Name = "arqon-steering-maintenance" }.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts = null;
            CloseSession();
        }
    }

    public void Dispose() => Stop();

    /// <summary>Opens, replaces or closes the driver handle so that it matches the current set of links.</summary>
    private void Reconfigure()
    {
        var native = _defaultAddress();
        var others = _links().Select(l => l.LocalAddress).ToList();
        var filter = native is null ? null : WinDivertFilter.Build(native, others);

        if (_session?.Filter == filter) return;
        CloseSession();
        if (filter is null) return;   // nothing to steer yet (single link); the maintenance loop will look again

        var handle = WinDivertNative.Open(filter, WinDivertNative.LayerNetwork, 0, 0);
        if (handle == WinDivertNative.InvalidHandle)
            throw new InvalidOperationException(DescribeOpenError(Marshal.GetLastWin32Error()));

        var session = new Session(handle, filter, InterfaceIndexes());
        var workers = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        for (var i = 0; i < workers; i++)
        {
            var name = $"arqon-steering-{i}";
            session.Workers.Add(new Thread(() => Work(session)) { IsBackground = true, Name = name });
        }
        _session = session;
        foreach (var t in session.Workers) t.Start();
    }

    private void CloseSession()
    {
        var session = _session;
        _session = null;
        if (session is null) return;

        session.Closing = true;
        WinDivertNative.Shutdown(session.Handle, WinDivertNative.ShutdownBoth);   // wakes blocked receivers
        foreach (var t in session.Workers)
            if (t != Thread.CurrentThread) t.Join(TimeSpan.FromSeconds(2));
        WinDivertNative.Close(session.Handle);
    }

    private void Maintain(CancellationToken token)
    {
        var ticks = 0;
        try
        {
            while (!token.WaitHandle.WaitOne(Tick))
            {
                lock (_gate)
                {
                    if (token.IsCancellationRequested) return;
                    Reconfigure();
                }
                _engine.CheckHealth();
                if (++ticks % 10 == 0) _engine.Cleanup();
            }
        }
        catch (ObjectDisposedException)
        {
            // stopped while waiting
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(ex.Message);
        }
    }

    private void Work(Session s)
    {
        var buffer = new byte[MaxPacket];
        var backup = new byte[MaxPacket];
        var address = new WinDivertNative.Address();
        var sendFailures = 0;

        try
        {
            while (!s.Closing)
            {
                if (!WinDivertNative.Recv(s.Handle, buffer, (uint)buffer.Length, out var length, ref address))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (s.Closing) return;
                    if (error == 122) continue;   // ERROR_INSUFFICIENT_BUFFER: an oversized packet, already dropped by the driver
                    Fail($"WinDivertRecv failed ({error})");
                    return;
                }

                Divert(s, buffer, backup, (int)length, ref address);

                if (WinDivertNative.Send(s.Handle, buffer, length, out _, ref address)) { sendFailures = 0; continue; }
                if (++sendFailures >= MaxConsecutiveSendFailures && !s.Closing)
                {
                    Fail($"WinDivertSend failed {sendFailures} times in a row ({Marshal.GetLastWin32Error()})");
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!s.Closing) Fail(ex.Message);
        }
    }

    /// <summary>Per-packet fail-open: if the engine misbehaves the packet goes out exactly as it came in.</summary>
    private void Divert(Session s, byte[] buffer, byte[] backup, int length, ref WinDivertNative.Address address)
    {
        Buffer.BlockCopy(buffer, 0, backup, 0, length);
        try
        {
            var view = PacketView.TryParse(buffer, length);
            if (view is null) return;

            var outbound = address.Outbound;
            var verdict = outbound ? _engine.ProcessOutbound(view) : _engine.ProcessInbound(view);
            if (verdict == SteerVerdict.Rewritten && outbound && s.InterfaceIndex.TryGetValue(view.Source, out var index))
            {
                address.IfIdx = index;
                address.SubIfIdx = 0;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Buffer.BlockCopy(backup, 0, buffer, 0, length);
            if (Interlocked.Increment(ref _engineErrors) >= MaxEngineErrors && !s.Closing)
                Fail($"the steering engine failed {MaxEngineErrors} times: {ex.Message}");
        }
    }

    private void Fail(string why)
    {
        // Release the driver first so traffic flows normally again, then tell the owner.
        lock (_gate)
        {
            _cts?.Cancel();
            _cts = null;
            CloseSession();
        }
        Faulted?.Invoke(why);
    }

    private static Dictionary<IPAddress, uint> InterfaceIndexes()
    {
        var map = new Dictionary<IPAddress, uint>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var properties = nic.GetIPProperties();
                var index = (uint)properties.GetIPv4Properties().Index;
                foreach (var u in properties.UnicastAddresses) map[u.Address] = index;
            }
            catch (NetworkInformationException) { /* IPv4 not enabled on this adapter */ }
        }
        return map;
    }

    private static string DescribeOpenError(int error) => error switch
    {
        5 => "access denied: run Arqon as administrator",
        2 or 126 => "WinDivert.dll or WinDivert64.sys was not found next to Arqon.exe",
        577 => "Windows rejected the WinDivert driver signature",
        1275 => "Windows blocked the WinDivert driver (security software or Secure Boot policy)",
        1060 => "the WinDivert driver service could not be installed",
        654 => "another driver conflicts with WinDivert",
        _ => new Win32Exception(error).Message,
    };
}
