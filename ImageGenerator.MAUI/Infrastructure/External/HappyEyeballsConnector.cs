using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;

namespace ImageGenerator.MAUI.Infrastructure.External;

/// <summary>
/// TCP connect that races a host's addresses instead of trying them one after another
/// (RFC 8305 "Happy Eyeballs", simplified). .NET's default connect tries each address until
/// the OS gives up on it, so a LAN name that resolves to an unreachable link-local IPv6
/// address first (via LLMNR) costs ~21 s before IPv4 is tried. Here each further address
/// starts <see cref="AttemptStagger"/> after the previous one, alternating address families,
/// and the first connection wins.
/// </summary>
public static class HappyEyeballsConnector
{
    internal static readonly TimeSpan AttemptStagger = TimeSpan.FromMilliseconds(250);

    /// <summary>The primary handler for every named HttpClient.</summary>
    public static SocketsHttpHandler CreateHandler() => new() { ConnectCallback = ConnectAsync };

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var socket = await ConnectSocketAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, ct);
        return new NetworkStream(socket, ownsSocket: true);
    }

    internal static async Task<Socket> ConnectSocketAsync(string host, int port, CancellationToken ct)
    {
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);
        return await ConnectSocketAsync(Interleave(addresses), port, ct);
    }

    /// <summary>Races <paramref name="addresses"/> in the given order, staggered.</summary>
    internal static async Task<Socket> ConnectSocketAsync(IReadOnlyList<IPAddress> addresses, int port, CancellationToken ct)
    {
        if (addresses.Count == 0) throw new SocketException((int)SocketError.HostNotFound);

        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var attempts = addresses
            .Select((address, i) => AttemptAsync(address, port, AttemptStagger * i, raceCts.Token))
            .ToList();

        Exception? lastError = null;
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts);
            attempts.Remove(done);
            if (done.IsCompletedSuccessfully)
            {
                raceCts.Cancel();
                foreach (var loser in attempts) _ = DisposeIfConnectedAsync(loser);
                return done.Result;
            }
            lastError = done.Exception?.InnerException ?? lastError;
        }

        ct.ThrowIfCancellationRequested();
        ExceptionDispatchInfo.Throw(lastError ?? new SocketException((int)SocketError.HostUnreachable));
        throw new UnreachableException();
    }

    /// <summary>Alternates address families, starting with the resolver's first choice.</summary>
    internal static IPAddress[] Interleave(IReadOnlyList<IPAddress> addresses)
    {
        if (addresses.Count == 0) return [];
        var firstFamily = addresses[0].AddressFamily;
        var preferred = addresses.Where(a => a.AddressFamily == firstFamily).ToList();
        var other = addresses.Where(a => a.AddressFamily != firstFamily).ToList();

        var result = new List<IPAddress>(addresses.Count);
        for (var i = 0; i < Math.Max(preferred.Count, other.Count); i++)
        {
            if (i < preferred.Count) result.Add(preferred[i]);
            if (i < other.Count) result.Add(other[i]);
        }
        return [.. result];
    }

    private static async Task<Socket> AttemptAsync(IPAddress address, int port, TimeSpan delay, CancellationToken ct)
    {
        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(address, port, ct);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // A losing attempt can still connect between the winner and the cancel.
    private static async Task DisposeIfConnectedAsync(Task<Socket> attempt)
    {
        try
        {
            (await attempt).Dispose();
        }
        catch
        {
            // A loser that failed or was cancelled holds no socket.
        }
    }
}
