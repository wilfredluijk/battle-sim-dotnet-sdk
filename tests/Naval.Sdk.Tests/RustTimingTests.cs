using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Xunit.Abstractions;
namespace Naval.Sdk.Tests;
public class RustTimingTests(ITestOutputHelper output)
{
    [RustFact]
    public async Task SlowCallbacksDiscardObsoleteCommandsAndRecoverWithoutWrongTicks()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var directory = Path.Combine(Path.GetTempPath(), "player04-timing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("BATTLE_SIM_SERVER")!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("BATTLE_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
        start.Environment["BATTLE_ADMIN_PASSWORD"] = "synthetic-local-audit";
        start.Environment["RUST_LOG"] = "warn";
        foreach (var arg in new[] { "--port", port.ToString(), "--tick-hz", "10", "--tick-deadline-ms", "80", "--allow-unauthenticated-bots", "--replay-dir", directory }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var stop = new CancellationTokenSource();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(2) };
        async Task<JsonObject> Request(HttpMethod method, string path, JsonObject? body = null)
        {
            using var request = new HttpRequestMessage(method,path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            return text.Length == 0 ? new() : JsonNode.Parse(text)!.AsObject();
        }
        async Task Wait(Func<Task<bool>> predicate)
        {
            for (var i = 0; i < 300; i++) { if (await predicate()) return; await Task.Delay(25,timeout.Token); }
            Assert.Fail("Local audit server did not reach expected state.");
        }
        var recovering = new SlowBot();
        Task<GameOver?>[] runs = [];
        try
        {
            await Wait(async () => {
                try {
                    var login = await Request(HttpMethod.Post,"/api/login",new() { ["password"] = "synthetic-local-audit" });
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",login["token"]!.GetValue<string>()); return true;
                } catch (HttpRequestException) { return false; }
            });
            runs = new Bot[] {new Bot(), recovering}.Select((bot,i) => BotRunner.RunAsync(bot,new() { Url = $"ws://127.0.0.1:{port}/bot", Token = "", Name = i == 0 ? "fast" : "slow" },stop.Token)).ToArray();
            await Wait(async () => { var r=await Request(HttpMethod.Get,"/api/room"); var bots=r["bots"]!.AsArray(); return bots.Count == 2 && bots.All(b=>b!["ready"]!.GetValue<bool>()); });
            await Request(HttpMethod.Post,"/api/room/start",new());
            await Wait(async () => (await Request(HttpMethod.Get,"/api/room"))["tick"]!.GetValue<int>() >= 30);
            var room = await Request(HttpMethod.Get,"/api/room");
            var bots = room["bots"]!.AsArray();
            var fast = bots.Single(b=>b!["name"]!.GetValue<string>() == "fast")!["diagnostics"]!;
            var slow = bots.Single(b=>b!["name"]!.GetValue<string>() == "slow")!["diagnostics"]!;
            output.WriteLine("Fast bot: " + fast.ToJsonString()); output.WriteLine("Bot recovering from slow callbacks: " + slow.ToJsonString());
            Assert.True(fast["accepted"]!.GetValue<int>() >= 20);
            Assert.Equal(0,fast["wrong_tick"]!.GetValue<int>());
            Assert.Equal(0, slow["wrong_tick"]!.GetValue<int>());
            Assert.Equal(0, slow["late"]!.GetValue<int>());
            Assert.True(slow["accepted"]!.GetValue<int>() >= 15);
            Assert.True(recovering.Diagnostics.DiscardedCommands >= 2);
            Assert.True(recovering.Diagnostics.SkippedTicks >= 2);
            Assert.True(recovering.Diagnostics.Overruns >= 2);
        }
        finally
        {
            stop.Cancel(); try { await Task.WhenAll(runs); } catch (OperationCanceledException) { }
            if (!process.HasExited) process.Kill(entireProcessTree:true);
            await process.WaitForExitAsync(); await Task.WhenAll(stdout,stderr); Directory.Delete(directory,true);
        }
    }
    private sealed class SlowBot : Bot
    {
        private int calls;
        public override Command OnTick(WorldView view) { if (++calls <= 2) Thread.Sleep(350); return new(); }
    }
}
