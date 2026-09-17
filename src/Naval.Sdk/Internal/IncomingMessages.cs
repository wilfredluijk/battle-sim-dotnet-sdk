using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Naval.Sdk.Internal;

internal sealed class ReceivedTick(WorldView view, long receivedAt)
{
    private int superseded;
    internal WorldView View { get; } = view;
    internal void Supersede() => Volatile.Write(ref superseded, 1);
    internal bool CanSend => Volatile.Read(ref superseded) == 0 &&
        Stopwatch.GetElapsedTime(receivedAt).TotalMilliseconds < View.DeadlineMs;
}

internal sealed record IncomingMessage(JsonObject? Message = null, ReceivedTick? Tick = null, bool Closed = false);
internal sealed class TransportFailureException(string message) : IOException(message);

/// <summary>One socket reader, independent of user callbacks. Never silently drops lifecycle frames.</summary>
internal sealed class IncomingMessages
{
    internal const int Capacity = 128;
    private readonly Channel<IncomingMessage> channel = Channel.CreateBounded<IncomingMessage>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = false
    });
    internal ChannelReader<IncomingMessage> Reader => channel.Reader;
    internal Task Completion { get; }

    internal IncomingMessages(ClientWebSocket socket, int maxBytes, CancellationToken ct) =>
        Completion = Task.Run(() => Read(socket, maxBytes, ct));

    private async Task Read(ClientWebSocket socket, int maxBytes, CancellationToken ct)
    {
        ReceivedTick? current = null;
        Exception? failure = null;
        try
        {
            while (true)
            {
                var frame = await Receive(socket, maxBytes, ct).ConfigureAwait(false);
                var receivedAt = Stopwatch.GetTimestamp();
                if (frame.Closed) { Enqueue(new(Closed: true)); break; }
                JsonObject message;
                try { message = Wire.Object(JsonNode.Parse(frame.Text ?? "null")); }
                catch (Exception e) when (Wire.IsMalformed(e)) { Enqueue(new()); continue; }
                var type = message["type"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
                ReceivedTick? tick = null;
                if (type == "tick")
                {
                    try
                    {
                        var view = WorldView.FromJson(message);
                        if (view.MatchId.Length > 0)
                        {
                            tick = new(view, receivedAt);
                            current?.Supersede();
                            current = tick;
                        }
                    }
                    catch (Exception e) when (Wire.IsMalformed(e)) { /* The consumer counts malformed messages. */ }
                }
                else if (type is "welcome" or "configuration" or "game_start" or "game_over" or "lobby")
                {
                    current?.Supersede();
                    current = null;
                }
                Enqueue(new(message, tick));
            }
        }
        catch (Exception e) { failure = e; }
        finally
        {
            current?.Supersede();
            channel.Writer.TryComplete(failure);
        }
    }

    private void Enqueue(IncomingMessage message)
    {
        // Waiting here would stop servicing WebSocket control frames during a blocked callback.
        if (!channel.Writer.TryWrite(message))
            throw new TransportFailureException($"Incoming backlog exceeded {Capacity} frames; callbacks or recording cannot keep up.");
    }

    private static async Task<(bool Closed, string? Text)> Receive(ClientWebSocket socket, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        ValueWebSocketReceiveResult result;
        var binary = false;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return (true, null);
            binary |= result.MessageType != WebSocketMessageType.Text;
            if (message.Length + result.Count > maxBytes)
                throw new TransportFailureException("Server message exceeded the configured size limit.");
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        if (binary) return (false, null);
        try { return (false, new UTF8Encoding(false, true).GetString(message.GetBuffer(), 0, (int)message.Length)); }
        catch (DecoderFallbackException) { return (false, null); }
    }
}
