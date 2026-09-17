using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
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

    /// <summary>Run matches until stopped or disconnected. Reconnect only outside active matches.</summary>
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
                handshake.CancelAfter(options.HandshakeTimeout);
                int? closeCode = null;
                var reason = "client stopped";
                async Task Send(JsonObject message, CancellationToken ct)
                {
                    var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
                    await sendLock.WaitAsync(ct).ConfigureAwait(false);
                    try { await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
                    finally { sendLock.Release(); }
                }
                try
                {
                    await socket.ConnectAsync(uri, handshake.Token).ConfigureAwait(false);
                    bot.Sender = Send;
                    await Send(new() { ["type"] = "hello", ["name"] = options.Name, ["version"] = options.Version, ["token"] = token }, handshake.Token).ConfigureAwait(false);
                    while (!session.Stop && !session.Fatal)
                    {
                        var frame = await Receive(socket, options.MaxMessageBytes, bot.Phase == "connecting" ? handshake.Token : cancellationToken).ConfigureAwait(false);
                        if (frame.Closed)
                        {
                            closeCode = socket.CloseStatus is { } status ? (int)status : null;
                            reason = socket.CloseStatusDescription ?? "connection closed";
                            break;
                        }
                        if (frame.Text is null) { bot.Diagnostics.MalformedFrames++; continue; }
                        JsonObject message;
                        try { message = Wire.Object(JsonNode.Parse(frame.Text)); }
                        catch (Exception e) when (Wire.IsMalformed(e)) { bot.Diagnostics.MalformedFrames++; continue; }
                        options.Recorder?.Record(message);
                        foreach (var payload in session.Handle(message)) await Send(payload, cancellationToken).ConfigureAwait(false);
                    }
                    if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", closing.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { reason = "cancelled"; throw; }
                catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException)
                {
                    reason = "connection failed or handshake timed out";
                    if (bot.Phase == "connecting" && (attempt >= options.ReconnectAttempts || session.Fatal))
                        throw new IOException(reason); // Do not expose an endpoint or credential in an inner exception.
                }
                finally
                {
                    bot.Sender = null;
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
            if (message.Length + result.Count > maxBytes) throw new IOException("Server message exceeded the configured size limit.");
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        if (binary) return (false, null);
        try { return (false, new UTF8Encoding(false, true).GetString(message.GetBuffer(), 0, (int)message.Length)); }
        catch (DecoderFallbackException) { return (false, null); }
    }
}
