using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class WindowsProcessResolverTests
{
    [Fact]
    public void Finds_this_process_by_the_port_of_its_tcp_listener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;

        var owner = new WindowsProcessResolver().Resolve(6, port);

        Assert.NotNull(owner);
        Assert.Equal(Environment.ProcessId, owner!.Pid);
        Assert.Equal(Process.GetCurrentProcess().ProcessName.ToLowerInvariant() + ".exe", owner.ExeName.ToLowerInvariant());
    }

    [Fact]
    public void Finds_this_process_by_the_port_of_its_udp_socket()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = (ushort)((IPEndPoint)udp.Client.LocalEndPoint!).Port;

        var owner = new WindowsProcessResolver().Resolve(17, port);

        Assert.Equal(Environment.ProcessId, owner?.Pid);
    }

    [Fact]
    public void A_port_nobody_uses_has_no_owner()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Assert.Null(new WindowsProcessResolver().Resolve(6, port));
    }

    [Fact]
    public void Other_protocols_have_no_owner()
    {
        Assert.Null(new WindowsProcessResolver().Resolve(1, 80));
    }

    [Fact]
    public void Knows_which_local_address_and_port_a_socket_holds()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        var resolver = new WindowsProcessResolver();

        Assert.True(resolver.IsLocalPortInUse(6, IPAddress.Loopback, port));
        Assert.False(resolver.IsLocalPortInUse(6, IPAddress.Parse("127.0.0.2"), port));
        Assert.False(resolver.IsLocalPortInUse(6, IPAddress.Loopback, (ushort)(port == 65535 ? 1 : port + 1)));
    }

    [Fact]
    public void A_socket_bound_to_every_address_holds_the_port_on_each_of_them()
    {
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.True(new WindowsProcessResolver().IsLocalPortInUse(6, IPAddress.Parse("127.0.0.2"), port));
    }

    [Fact]
    public void Sees_a_socket_opened_a_moment_ago()
    {
        var resolver = new WindowsProcessResolver();
        resolver.Resolve(6, 1);   // primes the cached snapshot

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.True(resolver.IsLocalPortInUse(6, IPAddress.Loopback, port));
    }
}
