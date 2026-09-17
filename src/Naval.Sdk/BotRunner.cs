using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Naval.Sdk.Internal;

namespace Naval.Sdk;

public sealed record RunOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 7878;
    public string Path { get; init; } = "/bot";
    public string? Url { get; init; }
    public string? Token { get; init; }
    public string Name { get; init; } = "bot";
    public string Version { get; init; } = $"naval-sdk-dotnet/{typeof(Bot).Assembly.GetName().Version!.ToString(3)}";
    public BotRecorder? Recorder { get; init; }
    public int ReconnectAttempts { get; init; }
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxMessageBytes { get; init; } = 1024 * 1024;

    // Omit free-form connection fields as well: an invalid URL can contain credentials.
    public override string ToString() => $"RunOptions {{ Token = [redacted], ReconnectAttempts = {ReconnectAttempts}, " +
        $"ReconnectDelay = {ReconnectDelay}, HandshakeTimeout = {HandshakeTimeout}, MaxMessageBytes = {MaxMessageBytes} }}";

    internal (Uri Uri, string Token) Resolve()
    {
        if (ReconnectAttempts < 0 || ReconnectAttempts == int.MaxValue || ReconnectDelay < TimeSpan.Zero || ReconnectDelay > TimeSpan.FromSeconds(60) ||
            HandshakeTimeout <= TimeSpan.Zero || MaxMessageBytes < 1) throw new ArgumentException("Invalid runtime limits.");
        var host = Host.Contains(':') && !Host.StartsWith('[') ? $"[{Host}]" : Host;
        var url = Url ?? Environment.GetEnvironmentVariable("BATTLE_SERVER_URL");
        if (string.IsNullOrEmpty(url)) url = $"ws://{host}:{Port}{Path}";
        return (ValidateUrl(url), Token ?? Environment.GetEnvironmentVariable("BATTLE_BOT_TOKEN") ?? "");
    }

    public static Uri ValidateUrl(string url, bool requireBotPath = false)
    {
        if (url.Any(char.IsWhiteSpace) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("ws" or "wss") || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0 ||
            url.Contains('?') || url.Contains('#') || uri.Port < 1 || (requireBotPath && uri.AbsolutePath != "/bot"))
            throw new ArgumentException("Use a ws:// or wss:// server URL with no credentials, query or fragment" + (requireBotPath ? ", ending in /bot." : "."));
        return uri;
    }
}

public static class BotRunner
{
    public static GameOver? Run(Bot bot, RunOptions? options = null, CancellationToken cancellationToken = default) =>
        RunAsync(bot, options, cancellationToken).GetAwaiter().GetResult();

    /// <summary>Run matches until stopped or normally closed. Unexpected transport failures throw IOException;
    /// reconnect attempts are allowed only outside active matches.</summary>
    public static async Task<GameOver?> RunAsync(Bot bot, RunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bot);
        options ??= new();
        var (uri, token) = options.Resolve();
        if (Interlocked.CompareExchange(ref bot.Running, 1, 0) != 0) throw new InvalidOperationException("This bot already has an active runtime.");
        bot.Diagnostics = new();
        GameOver? lastResult = null;
        try
        {
            for (var attempt = 0; attempt <= options.ReconnectAttempts; attempt++)
            {
                bot.Phase = "connecting"; bot.Welcome = null; bot.MatchId = ""; bot.LastTick = 0;
                var session = new Session(bot);
                using var socket = new ClientWebSocket();
                using var sendLock = new SemaphoreSlim(1, 1);
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshake.CancelAfter(options.HandshakeTimeout);
                IncomingMessages? incoming = null;
                int? closeCode = null;
                var reason = "client stopped";
                async Task Send(JsonObject message, CancellationToken ct, ReceivedTick? tick = null)
                {
                    await sendLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
                        // Check after callbacks, recording, serialization and any competing raw send.
                        if (tick is not null && !tick.CanSend) { bot.Diagnostics.DiscardedCommands++; return; }
                        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                    }
                    finally { sendLock.Release(); }
                }
                try
                {
                    await socket.ConnectAsync(uri, handshake.Token).ConfigureAwait(false);
                    bot.Sender = (message, ct) => Send(message, ct);
                    await Send(new() { ["type"] = "hello", ["name"] = options.Name, ["version"] = options.Version, ["token"] = token }, handshake.Token).ConfigureAwait(false);
                    incoming = new(socket, options.MaxMessageBytes, connection.Token);
                    var pendingEvents = new List<TickEvent>();
                    var coalesced = 0;
                    while (!session.Stop && !session.Fatal)
                    {
                        if (!await incoming.Reader.WaitToReadAsync(bot.Phase == "connecting" ? handshake.Token : cancellationToken).ConfigureAwait(false)) break;
                        if (!incoming.Reader.TryRead(out var frame)) continue;
                        if (frame.Closed)
                        {
                            closeCode = socket.CloseStatus is { } status ? (int)status : null;
                            reason = string.IsNullOrEmpty(socket.CloseStatusDescription) ? "connection closed" : socket.CloseStatusDescription;
                            if (closeCode is not (1000 or 1001))
                                throw new TransportFailureException($"Server closed the WebSocket abnormally (code {closeCode?.ToString() ?? "none"}).");
                            break;
                        }
                        if (frame.Message is not { } message) { bot.Diagnostics.MalformedFrames++; continue; }
                        options.Recorder?.Record(message);
                        IReadOnlyList<JsonObject> payloads;
                        if (frame.Tick is { } received)
                        {
                            var view = received.View;
                            // Coalesce only adjacent valid ticks in one match. Lifecycle and error
                            // callbacks retain their order, and each consumed original frame is recorded.
                            if (coalesced < IncomingMessages.Capacity && incoming.Reader.TryPeek(out var next) &&
                                next.Tick is { } newer && newer.View.MatchId == view.MatchId && newer.View.Tick > view.Tick)
                            {
                                pendingEvents.AddRange(view.Events);
                                coalesced++; bot.Diagnostics.SkippedTicks++;
                                continue;
                            }
                            if (pendingEvents.Count > 0)
                            {
                                pendingEvents.AddRange(view.Events);
                                view = view with { Events = Array.AsReadOnly(pendingEvents.ToArray()) };
                                pendingEvents.Clear();
                            }
                            coalesced = 0;
                            payloads = session.HandleTick(view);
                        }
                        else payloads = session.Handle(message);
                        if (bot.Phase != "connecting") handshake.CancelAfter(Timeout.InfiniteTimeSpan);
                        foreach (var payload in payloads) await Send(payload, cancellationToken, frame.Tick).ConfigureAwait(false);
                    }
                    if (session.Fatal) reason = $"server rejected connection ({session.FatalCode})";
                    if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", closing.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { reason = "cancelled"; throw; }
                catch (ProtocolMismatchException) { reason = "unsupported protocol version"; throw; }
                catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException)
                {
                    reason = e switch
                    {
                        TransportFailureException limit => limit.Message,
                        WebSocketException ws => $"WebSocket transport failed ({ws.WebSocketErrorCode}).",
                        OperationCanceledException => "handshake timed out",
                        _ => "I/O failure while receiving, recording or sending."
                    };
                    if (bot.Phase == "running" || attempt >= options.ReconnectAttempts || session.Fatal)
                        throw new IOException(reason); // Do not expose an endpoint or credential in an inner exception.
                }
                finally
                {
                    bot.Sender = null;
                    connection.Cancel();
                    if (incoming is not null) await incoming.Completion.ConfigureAwait(false);
                    lastResult = session.Result ?? lastResult;
                    var info = new DisconnectInfo(closeCode, reason, bot.Phase);
                    bot.Diagnostics.LastDisconnect = info;
                    Session.Call(bot, () => bot.OnDisconnect(info));
                }
                if (session.Stop || session.Fatal || bot.Phase == "running" || attempt >= options.ReconnectAttempts) return lastResult;
                await Task.Delay(options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { bot.Sender = null; bot.Phase = "disconnected"; Interlocked.Exchange(ref bot.Running, 0); }
        return lastResult;
    }
}
