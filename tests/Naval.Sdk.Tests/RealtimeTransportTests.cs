using System.Buffers.Binary;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Naval.Sdk.Internal;

namespace Naval.Sdk.Tests;

public class RealtimeTransportTests
{
    // Raw control frames let us verify a pong while OnTick is still blocked. The pong
    // also proves that all preceding frames have reached the SDK's receive loop.
    private static async Task Write(NetworkStream stream, int opcode, byte[] data, CancellationToken ct)
    {
        var header = data.Length < 126 ? new byte[] { (byte)(0x80 | opcode), (byte)data.Length } :
            new byte[] { (byte)(0x80 | opcode), 126, (byte)(data.Length >> 8), (byte)data.Length };
        await stream.WriteAsync(header, ct); await stream.WriteAsync(data, ct);
    }
    private static Task Send(NetworkStream stream, JsonObject message, CancellationToken ct) =>
        Write(stream, 1, Encoding.UTF8.GetBytes(message.ToJsonString()), ct);
    private static async Task<(int Opcode, byte[] Data)> Read(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[2]; await stream.ReadExactlyAsync(header, ct);
        var length = header[1] & 0x7f;
        if (length == 126) { var extended = new byte[2]; await stream.ReadExactlyAsync(extended, ct); length = BinaryPrimitives.ReadUInt16BigEndian(extended); }
        Assert.NotEqual(127, header[1] & 0x7f); Assert.True((header[1] & 0x80) != 0);
        var mask = new byte[4]; await stream.ReadExactlyAsync(mask, ct);
        var data = new byte[length]; await stream.ReadExactlyAsync(data, ct);
        for (var i = 0; i < data.Length; i++) data[i] ^= mask[i % 4];
        return (header[0] & 0xf, data);
    }
    private static async Task<JsonObject> ReadMessage(NetworkStream stream, CancellationToken ct)
    {
        var (opcode, data) = await Read(stream, ct); Assert.Equal(1, opcode);
        return JsonNode.Parse(data)!.AsObject();
    }
    private static async Task Ping(NetworkStream stream, CancellationToken ct)
    {
        byte[] challenge = [1, 2, 3, 4, 5, 6, 7, 8];
        await Write(stream, 9, challenge, ct);
        var (opcode, data) = await Read(stream, ct);
        Assert.Equal(10, opcode); Assert.Equal(challenge, data);
    }
    private static JsonObject Tick(int tick, string match = "fixture-match-1", int deadline = 5000)
    {
        var message = Fixtures.Frame("tick"); message["tick"] = tick; message["match_id"] = match; message["deadline_ms"] = deadline;
        message["events"] = new JsonArray(new JsonObject { ["type"] = "hit", ["amount"] = tick });
        return message;
    }

    [Fact]
    public async Task BlockedDecisionKeepsHeartbeatAndResumesLatestTickWithAllEventsRecorded()
    {
        using var server = new LocalServer(); using var bot = new BlockingBot();
        var path = Path.Combine(Path.GetTempPath(), $"naval-backlog-{Guid.NewGuid():N}.jsonl");
        try
        {
            using var recorder = new BotRecorder(path);
            var run = BotRunner.RunAsync(bot, new() { Url = server.Url, Recorder = recorder }, server.Token);
            try
            {
                using var stream = await server.Upgrade();
                await ReadMessage(stream, server.Token); await Send(stream, Fixtures.Frame("welcome"), server.Token);
                await ReadMessage(stream, server.Token);
                await Send(stream, Tick(1), server.Token); await bot.Started.Task.WaitAsync(server.Token);
                await Send(stream, Tick(2), server.Token); await Send(stream, Tick(3), server.Token);
                // A malformed future tick must not supersede the latest valid observation.
                await Send(stream, new() { ["type"] = "tick", ["tick"] = 999 }, server.Token);
                await Ping(stream, server.Token);
                Assert.False(bot.Release.IsSet);
                bot.Release.Set();
                var command = await ReadMessage(stream, server.Token);
                Assert.Equal(3, command["tick"]!.GetValue<int>());
                await Send(stream, Fixtures.Frame("game_over"), server.Token);
                Assert.NotNull(await run);
                Assert.Equal(new[] { 1, 3 }, bot.Views.Select(v => v.Tick));
                Assert.Equal(new[] { 2, 3 }, bot.Views.Last().Events.Cast<HitEvent>().Select(e => e.Amount));
                Assert.Equal(1, bot.Diagnostics.SkippedTicks); Assert.Equal(1, bot.Diagnostics.DiscardedCommands);
                Assert.Equal(1, bot.Diagnostics.MalformedFrames);
                var ticks = File.ReadLines(path).Skip(1).Select(l => JsonNode.Parse(l)!).Where(m => m["type"]!.GetValue<string>() == "tick").ToArray();
                Assert.Equal(new[] { 1, 2, 3, 999 }, ticks.Select(t => t["tick"]!.GetValue<int>()));
                Assert.All(ticks.Take(3), t => Assert.Single(t["events"]!.AsArray()));
            }
            finally { bot.Release.Set(); try { await run; } catch (Exception) { /* Preserve assertion failures. */ } }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BufferedLifecycleRetainsOrderAndNeverSendsPreviousMatchCommands()
    {
        using var server = new LocalServer(); using var bot = new BlockingBot { Rounds = 2 };
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url }, server.Token);
        try
        {
            using var stream = await server.Upgrade(); await ReadMessage(stream, server.Token);
            await Send(stream, Fixtures.Frame("welcome"), server.Token); await ReadMessage(stream, server.Token);
            await Send(stream, Fixtures.Frame("game_start"), server.Token);
            await Send(stream, Tick(1), server.Token); await bot.Started.Task.WaitAsync(server.Token);
            await Send(stream, Tick(2), server.Token);
            await Send(stream, Fixtures.Frame("game_over"), server.Token);
            await Send(stream, new() { ["type"] = "lobby", ["tick"] = 0 }, server.Token);
            var start = Fixtures.Frame("game_start"); start["match_id"] = "second";
            await Send(stream, start, server.Token); await Send(stream, Tick(1, "second"), server.Token);
            await Ping(stream, server.Token); bot.Release.Set();
            Assert.Equal("ready", (await ReadMessage(stream, server.Token))["type"]!.GetValue<string>());
            var command = await ReadMessage(stream, server.Token);
            Assert.Equal("second", command["match_id"]!.GetValue<string>()); Assert.Equal(1, command["tick"]!.GetValue<int>());
            await Send(stream, Fixtures.Frame("game_over"), server.Token); await run;
            Assert.Equal(new[] { "start", "tick:1", "tick:2", "end", "lobby", "start", "tick:1", "end" }, bot.Order);
            Assert.Equal(2, bot.Diagnostics.DiscardedCommands);
        }
        finally { bot.Release.Set(); try { await run; } catch (Exception) { } }
    }

    [Fact]
    public async Task ExpiredDecisionIsDiscardedEvenWithoutANewerTick()
    {
        using var server = new LocalServer(); using var bot = new BlockingBot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url }, server.Token);
        try
        {
            using var stream = await server.Upgrade(); await ReadMessage(stream, server.Token);
            await Send(stream, Fixtures.Frame("welcome"), server.Token); await ReadMessage(stream, server.Token);
            await Send(stream, Tick(1, deadline: 1), server.Token); await bot.Started.Task.WaitAsync(server.Token);
            await Task.Delay(20, server.Token); bot.Release.Set();
            // The hook returns before sending. Wait for the discarded-command counter instead
            // of supplying a newer tick, so this specifically exercises the deadline guard.
            for (var i = 0; i < 200 && bot.Diagnostics.DiscardedCommands == 0; i++) await Task.Delay(5, server.Token);
            Assert.Equal(1, bot.Diagnostics.DiscardedCommands);
            await Send(stream, Tick(2), server.Token);
            Assert.Equal(2, (await ReadMessage(stream, server.Token))["tick"]!.GetValue<int>());
            await Send(stream, Fixtures.Frame("game_over"), server.Token); await run;
            Assert.Equal(1, bot.Diagnostics.Overruns);
        }
        finally { bot.Release.Set(); try { await run; } catch (Exception) { } }
    }

    [Fact]
    public async Task BacklogIsBoundedAndFailureIsActionable()
    {
        using var server = new LocalServer(); using var client = new ClientWebSocket();
        var connect = client.ConnectAsync(new Uri(server.Url), server.Token);
        using var socket = await server.Accept(); await connect;
        var incoming = new IncomingMessages(client, 1024, server.Token);
        for (var i = 0; i <= IncomingMessages.Capacity; i++) await server.Send(socket, new() { ["type"] = "future" });
        await incoming.Completion.WaitAsync(server.Token);
        while (incoming.Reader.TryRead(out _)) { }
        var error = await Assert.ThrowsAsync<TransportFailureException>(async () => await incoming.Reader.WaitToReadAsync(server.Token));
        Assert.Contains("backlog", error.Message);
    }

    [Fact]
    public async Task ActiveTransportAbortThrowsAndDoesNotReconnect()
    {
        using var server = new LocalServer(); var bot = new ProbeBot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url, ReconnectAttempts = 2 }, server.Token);
        using var socket = await server.Accept(); await server.Read(socket);
        await server.Send(socket, Fixtures.Frame("welcome")); await server.Read(socket);
        await server.Send(socket, Tick(1)); await server.Read(socket); socket.Abort();
        var error = await Assert.ThrowsAsync<IOException>(() => run);
        Assert.Contains("WebSocket", error.Message); Assert.Null(error.InnerException);
        Assert.Equal(1, bot.Welcomes); Assert.Equal("running", bot.Diagnostics.LastDisconnect!.Phase);
    }

    [Fact]
    public async Task LobbyTransportAbortCanRetry()
    {
        using var server = new LocalServer(); var bot = new ProbeBot { Continue = false };
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url, ReconnectAttempts = 1, ReconnectDelay = TimeSpan.Zero }, server.Token);
        using (var first = await server.Accept())
        {
            await server.Read(first); await server.Send(first, Fixtures.Frame("welcome")); await server.Read(first);
            first.Abort();
        }
        using var second = await server.Accept(); await server.Read(second);
        await server.Send(second, Fixtures.Frame("welcome")); await server.Read(second);
        await server.Send(second, Fixtures.Frame("game_over"));
        Assert.NotNull(await run); Assert.Equal(2, bot.Welcomes);
    }

    [Fact]
    public async Task TimingHookCannotSendAnExpiredCommand()
    {
        using var server = new LocalServer(); using var bot = new BlockingBot { BlockTimingHook = true };
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url }, server.Token);
        try
        {
            using var stream = await server.Upgrade(); await ReadMessage(stream, server.Token);
            await Send(stream, Fixtures.Frame("welcome"), server.Token); await ReadMessage(stream, server.Token);
            await Send(stream, Tick(1, deadline: 200), server.Token); await bot.Started.Task.WaitAsync(server.Token);
            Assert.False(bot.Diagnostics.LastTiming!.OverBudget);
            await Task.Delay(250, server.Token); bot.Release.Set();
            for (var i = 0; i < 200 && bot.Diagnostics.DiscardedCommands == 0; i++) await Task.Delay(5, server.Token);
            Assert.Equal(1, bot.Diagnostics.DiscardedCommands);
            await Send(stream, Tick(2), server.Token);
            Assert.Equal(2, (await ReadMessage(stream, server.Token))["tick"]!.GetValue<int>());
            await Send(stream, Fixtures.Frame("game_over"), server.Token); await run;
        }
        finally { bot.Release.Set(); try { await run; } catch (Exception) { } }
    }

    [Fact]
    public async Task AbnormalCloseReportsCodeWithoutLeakingServerText()
    {
        using var server = new LocalServer(); var bot = new Bot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url }, server.Token);
        using var socket = await server.Accept(); await server.Read(socket);
        await server.Send(socket, Fixtures.Frame("welcome")); await server.Read(socket);
        await socket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "synthetic-private-token", server.Token);
        var error = await Assert.ThrowsAsync<IOException>(() => run);
        Assert.Contains("1011", error.Message); Assert.DoesNotContain("synthetic-private-token", error.ToString());
        Assert.Equal(1011, bot.Diagnostics.LastDisconnect!.Code);
    }

    private sealed class BlockingBot : Bot, IDisposable
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        internal List<WorldView> Views { get; } = [];
        internal List<string> Order { get; } = [];
        internal int Rounds { get; init; } = 1;
        internal bool BlockTimingHook { get; init; }
        private int ends;
        public override Command OnTick(WorldView view)
        {
            Views.Add(view); Order.Add($"tick:{view.Tick}");
            if (Views.Count == 1 && !BlockTimingHook) Block();
            return new();
        }
        public override void OnTickTiming(TickTiming timing) { if (Views.Count == 1 && BlockTimingHook) Block(); }
        private void Block() { Started.SetResult(); if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); }
        public override void OnGameStartEvent(GameStart start) => Order.Add("start");
        public override bool OnGameOver(GameOver result) { Order.Add("end"); return ++ends < Rounds; }
        public override void OnLobby(int tick) => Order.Add("lobby");
        public void Dispose() => Release.Dispose();
    }
}
