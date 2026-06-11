using System.Net.WebSockets;
using System.Text;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>
/// Thin seam over a ComfyUI progress WebSocket so the generation service is testable —
/// ClientWebSocket is sealed and cannot be mocked.
/// </summary>
public interface IComfyUiSocket : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken ct);

    /// <summary>
    /// The next complete TEXT message. Binary frames (ComfyUI streams live preview JPEGs on
    /// the same socket) are skipped. Null when the server closed the connection.
    /// </summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);
}

/// <summary>Production adapter over <see cref="ClientWebSocket"/>.</summary>
public sealed class ClientWebSocketComfyUiSocket : IComfyUiSocket
{
    private readonly ClientWebSocket _socket = new();

    public Task ConnectAsync(Uri uri, CancellationToken ct) => _socket.ConnectAsync(uri, ct);

    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
                continue;
            }

            // Binary = live preview frame; drain the remaining fragments and keep waiting.
            while (!result.EndOfMessage)
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
