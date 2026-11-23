using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External;

namespace ImageGenerator.MAUI.Tests.Infrastructure;

public sealed class HappyEyeballsConnectorTests : IDisposable
{
    // Not routed on a home LAN: a SYN to it goes unanswered until the OS gives up (~21 s).
    private static readonly IPAddress BlackHole = IPAddress.Parse("10.255.255.1");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public HappyEyeballsConnectorTests() => _listener.Start();

    private int ListenerPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Dispose() => _listener.Stop();

    [Fact]
    public async Task UnansweredFirstAddress_DoesNotDelayTheNextOne()
    {
        var watch = Stopwatch.StartNew();

        using var socket = await HappyEyeballsConnector.ConnectSocketAsync(
            [BlackHole, IPAddress.Loopback], ListenerPort, CancellationToken.None);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        socket.Connected.Should().BeTrue();
        ((IPEndPoint)socket.RemoteEndPoint!).Address.Should().Be(IPAddress.Loopback);
    }

    [Fact]
    public async Task EveryAddressRefused_ThrowsTheSocketError()
    {
        var port = ListenerPort;
        _listener.Stop();

        var act = () => HappyEyeballsConnector.ConnectSocketAsync([IPAddress.Loopback], port, CancellationToken.None);

        (await act.Should().ThrowAsync<SocketException>())
            .Which.SocketErrorCode.Should().Be(SocketError.ConnectionRefused);
    }

    [Fact]
    public async Task CallerCancel_Throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = () => HappyEyeballsConnector.ConnectSocketAsync([BlackHole], ListenerPort, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Interleave_AlternatesFamilies_StartingWithTheResolversFirstChoice()
    {
        var v6a = IPAddress.Parse("fe80::1");
        var v6b = IPAddress.Parse("fe80::2");
        var v4 = IPAddress.Parse("192.168.178.26");

        HappyEyeballsConnector.Interleave([v6a, v6b, v4]).Should().Equal(v6a, v4, v6b);
        HappyEyeballsConnector.Interleave([v4, v6a]).Should().Equal(v4, v6a);
        HappyEyeballsConnector.Interleave([]).Should().BeEmpty();
    }
}
