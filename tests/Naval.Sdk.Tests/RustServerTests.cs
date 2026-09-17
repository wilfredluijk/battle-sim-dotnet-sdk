using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Naval.Sdk.Tests;

public sealed class RustFactAttribute : FactAttribute
{
    public RustFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BATTLE_SIM_SERVER")))
            Skip = "Set BATTLE_SIM_SERVER to a locally built naval-server binary for real-server integration.";
    }
}

public class RustServerTests
{
    [RustFact]
    public async Task ActualRustServerSupportsConfigurationTelemetryTwoRoundsAndReplay()
    {
        var binary = Environment.GetEnvironmentVariable("BATTLE_SIM_SERVER")!;
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var directory = Path.Combine(Path.GetTempPath(), $"naval-rust-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo(binary) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("BATTLE_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
        start.Environment["BATTLE_ADMIN_PASSWORD"] = "synthetic-dotnet-integration-password";
        start.Environment["RUST_LOG"] = "warn";
        foreach (var arg in new[] { "--port", port.ToString(), "--tick-hz", "20", "--tick-deadline-ms", "40", "--allow-unauthenticated-bots", "--replay-dir", Path.Combine(directory, "replays") }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(3) };
        async Task<JsonObject?> Request(HttpMethod method, string path, JsonObject? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);
            return content.Length == 0 ? null : JsonNode.Parse(content)!.AsObject();
        }
        async Task Wait(Func<Task<bool>> predicate)
        {
            for (var i = 0; i < 200; i++)
            {
                if (await predicate()) return;
                await Task.Delay(25, ct);
            }
            Assert.Fail("Local Rust server did not reach the expected lifecycle state.");
        }
        var bots = new[] { new ObservingBot(), new ObservingBot() };
        Task<GameOver?>[] runs = [];
        try
        {
            await Wait(async () =>
            {
                try
                {
                    var login = await Request(HttpMethod.Post, "/api/login", new() { ["password"] = "synthetic-dotnet-integration-password" });
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!["token"]!.GetValue<string>());
                    return true;
                }
                catch (HttpRequestException) { if (process.HasExited) Assert.Fail("Local Rust server exited during startup."); return false; }
            });
            var path = Path.Combine(directory, "views.jsonl");
            using (var recording = new BotRecorder(path))
            {
                runs = bots.Select((bot, i) => BotRunner.RunAsync(bot, new() { Url = $"ws://127.0.0.1:{port}/bot", Token = "", Name = $"dotnet-test-{i}", Recorder = i == 0 ? recording : null }, ct)).ToArray();
                async Task<bool> Ready()
                {
                    var room = await Request(HttpMethod.Get, "/api/room");
                    var participants = room!["bots"]!.AsArray();
                    return participants.Count == 2 && participants.All(b => b!["ready"]!.GetValue<bool>());
                }
                await Wait(Ready);
                var config = bots[0].Welcome!.Configuration["sim_config"]!.AsObject();
                config["shell_speed"] = 91;
                await Request(HttpMethod.Put, "/api/room/config", config);
                await Wait(() => Task.FromResult(bots.All(b => b.Hashes.Count == 2)));
                await Wait(Ready);
                for (var round = 1; round <= 2; round++)
                {
                    await Request(HttpMethod.Post, "/api/room/start", new());
                    await Wait(() => Task.FromResult(bots.All(b => b.Starts.Count == round && b.LastTick >= 3)));
                    await Request(HttpMethod.Post, "/api/room/abort", new());
                    await Wait(() => Task.FromResult(bots.All(b => b.Ends == round)));
                    if (round == 1) { await Request(HttpMethod.Post, "/api/room/reset", new()); await Wait(Ready); }
                }
                await Task.WhenAll(runs).WaitAsync(ct);
            }
            foreach (var bot in bots)
            {
                Assert.Equal(0, bot.Diagnostics.CallbackErrors); Assert.Equal(0, bot.Diagnostics.MalformedFrames); Assert.Empty(bot.Diagnostics.RejectedCommands);
                Assert.Equal(2, bot.Starts.Select(s => s.MatchId).Distinct().Count());
                Assert.All(bot.Starts, s => Assert.Equal(91, s.ShipSpecs!.ShellSpeed));
                Assert.NotEmpty(bot.Views); Assert.All(bot.Views, v => Assert.NotNull(v.Me.GunCooldownTicksLeft));
            }
            var decisions = Replay.Run(new Bot(), path).ToArray();
            Assert.Equal(bots[0].Views.Count, decisions.Length); Assert.Equal(2, decisions.Select(d => d.MatchId).Distinct().Count());
        }
        finally
        {
            timeout.Cancel();
            try { await Task.WhenAll(runs); } catch (Exception) { /* Preserve the original failure while releasing the local server. */ }
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, errors);
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ObservingBot : Bot
    {
        public ConcurrentQueue<string> Hashes { get; } = new();
        public ConcurrentQueue<GameStart> Starts { get; } = new();
        public ConcurrentQueue<WorldView> Views { get; } = new();
        private int ends;
        public int Ends => Volatile.Read(ref ends);
        public override IReadOnlyList<string> ChoosePowerups(Welcome welcome) => ["rapid_fire", "heavy_shell"];
        public override void OnWelcome(Welcome welcome) => Hashes.Enqueue(welcome.ConfigHash);
        public override void OnGameStartEvent(GameStart start) => Starts.Enqueue(start);
        public override Command OnTick(WorldView view) { Views.Enqueue(view); return new() { Throttle = .1 }; }
        public override bool OnGameOver(GameOver result) => Interlocked.Increment(ref ends) < 2;
    }
}
