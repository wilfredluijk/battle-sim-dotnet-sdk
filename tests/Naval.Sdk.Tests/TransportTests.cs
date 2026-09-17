using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Naval.Sdk.Tests;

internal sealed class LocalServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
    public CancellationToken Token => timeout.Token;
    public string Url => $"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/bot";
    public LocalServer() => listener.Start();
    public async Task<WebSocket> Accept() => WebSocket.CreateFromStream(await Upgrade(), true, null, TimeSpan.FromSeconds(20));
    internal async Task<NetworkStream> Upgrade()
    {
        var client = await listener.AcceptTcpClientAsync(Token);
        var stream = client.GetStream();
        var header = new StringBuilder(); var one = new byte[1];
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one, Token) != 1) throw new IOException("Incomplete handshake.");
            header.Append((char)one[0]);
            if (header.Length > 16384) throw new IOException("Oversized handshake.");
        }
        var key = header.ToString().Split("\r\n").Single(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), Token);
        return stream;
    }
    public Task Send(WebSocket socket, JsonObject message) => SendText(socket, message.ToJsonString());
    public async Task SendText(WebSocket socket, string text, bool fragmented = false)
    {
        var bytes = Encoding.UTF8.GetBytes(text); var half = fragmented ? bytes.Length / 2 : bytes.Length;
        await socket.SendAsync(bytes.AsMemory(0, half), WebSocketMessageType.Text, !fragmented, Token);
        if (fragmented) await socket.SendAsync(bytes.AsMemory(half), WebSocketMessageType.Text, true, Token);
    }
    public async Task<JsonObject> Read(WebSocket socket)
    {
        var buffer = new byte[16384]; using var data = new MemoryStream(); ValueWebSocketReceiveResult result;
        do { result = await socket.ReceiveAsync(buffer.AsMemory(), Token); data.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
        return JsonNode.Parse(data.ToArray())!.AsObject();
    }
    public void Dispose() { timeout.Cancel(); listener.Stop(); timeout.Dispose(); }
}

public class TransportTests
{
    [Fact]
    public async Task RealWebSocketHandlesFragmentationMalformedFramesAndTwoRounds()
    {
        using var server = new LocalServer();
        var bot = new ProbeBot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url, Token = "synthetic", Name = "integration" }, server.Token);
        using var socket = await server.Accept();
        var hello = await server.Read(socket);
        Assert.Equal("hello", hello["type"]!.GetValue<string>()); Assert.Equal("synthetic", hello["token"]!.GetValue<string>());
        Assert.Equal("naval-sdk-dotnet/0.1.0", hello["version"]!.GetValue<string>());
        foreach (var malformed in new[] { "null", "[]", "{not-json}" }) await server.SendText(socket, malformed);
        await socket.SendAsync(new byte[] { 1, 2 }.AsMemory(), WebSocketMessageType.Binary, true, server.Token);
        await server.SendText(socket, Fixtures.Frame("welcome").ToJsonString(), fragmented: true);
        Assert.Equal("ready", (await server.Read(socket))["type"]!.GetValue<string>());
        for (var round = 0; round < 2; round++)
        {
            var start = Fixtures.Frame("game_start"); start["match_id"] = $"round-{round}";
            await server.Send(socket, start);
            var tick = Fixtures.Frame("tick"); tick["match_id"] = $"round-{round}";
            await server.Send(socket, tick);
            Assert.Equal($"round-{round}", (await server.Read(socket))["match_id"]!.GetValue<string>());
            if (round == 1) bot.Continue = false;
            await server.Send(socket, Fixtures.Frame("game_over"));
            if (round == 0)
            {
                await server.Send(socket, new() { ["type"] = "lobby", ["tick"] = 0 });
                Assert.Equal("ready", (await server.Read(socket))["type"]!.GetValue<string>());
            }
        }
        Assert.NotNull(await run);
        Assert.Equal(2, bot.Starts); Assert.Equal(2, bot.Ends); Assert.Equal(4, bot.Diagnostics.MalformedFrames);
        Assert.Equal(0, bot.Diagnostics.CallbackErrors); Assert.Equal("disconnected", bot.Phase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bot.RawSendAsync(new()));
    }

    [Fact]
    public async Task LobbyDisconnectCanReconnect()
    {
        using var server = new LocalServer(); var bot = new ProbeBot { Continue = false };
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url, ReconnectAttempts = 1, ReconnectDelay = TimeSpan.Zero }, server.Token);
        using (var first = await server.Accept())
        {
            await server.Read(first); await server.Send(first, Fixtures.Frame("welcome")); await server.Read(first);
            await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "lobby restart", server.Token);
        }
        using var second = await server.Accept();
        await server.Read(second); await server.Send(second, Fixtures.Frame("welcome")); await server.Read(second);
        await server.Send(second, Fixtures.Frame("game_over"));
        Assert.NotNull(await run); Assert.Equal(2, bot.Welcomes);
    }

    [Fact]
    public async Task ActiveMatchDisconnectNeverReconnects()
    {
        using var server = new LocalServer(); var bot = new ProbeBot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url, ReconnectAttempts = 3, ReconnectDelay = TimeSpan.Zero }, server.Token);
        using var socket = await server.Accept();
        await server.Read(socket); await server.Send(socket, Fixtures.Frame("welcome")); await server.Read(socket);
        await server.Send(socket, Fixtures.Frame("game_start")); await server.Send(socket, Fixtures.Frame("tick")); await server.Read(socket);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "match lost", server.Token);
        Assert.Null(await run); Assert.Equal(1, bot.Welcomes); Assert.Equal("running", bot.Diagnostics.LastDisconnect!.Phase);
        Assert.Equal(1000, bot.Diagnostics.LastDisconnect.Code);
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotReconnect()
    {
        using var server = new LocalServer(); var bot = new ProbeBot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url, ReconnectAttempts = 3 }, server.Token);
        using var socket = await server.Accept(); await server.Read(socket);
        await server.Send(socket, new() { ["type"] = "error", ["code"] = "unauthorized", ["message"] = "no" });
        Assert.Null(await run); Assert.Equal(1, bot.Diagnostics.RejectedCommands["unauthorized"]);
    }

    [Fact]
    public async Task CancellationAndConcurrentRunAreHandled()
    {
        using var server = new LocalServer(); using var stop = new CancellationTokenSource(); var bot = new Bot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url }, stop.Token);
        using var socket = await server.Accept(); await server.Read(socket);
        await Assert.ThrowsAsync<InvalidOperationException>(() => BotRunner.RunAsync(bot, new() { Url = server.Url }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bot.RawReceiveAsync());
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal("disconnected", bot.Phase); Assert.Equal("cancelled", bot.Diagnostics.LastDisconnect!.Reason);
    }

    [Fact]
    public async Task HandshakeTimeoutIsBounded()
    {
        using var server = new LocalServer(); var bot = new Bot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url, HandshakeTimeout = TimeSpan.FromMilliseconds(200) }, server.Token);
        using var socket = await server.Accept(); await server.Read(socket);
        await Assert.ThrowsAsync<IOException>(() => run);
        Assert.Equal("disconnected", bot.Phase);
    }
}
