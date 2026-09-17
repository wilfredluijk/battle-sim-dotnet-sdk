using System.Text.Json.Nodes;

namespace Naval.Sdk.Tests;

public class RecordingAndCliTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"naval-dotnet-test-{Guid.NewGuid():N}");

    [Fact]
    public void RecordingFiltersRedactsAndReplaysLifecycle()
    {
        var path = TempPath();
        try
        {
            using (var recorder = new BotRecorder(path))
            {
                recorder.Record(new() { ["type"] = "hello", ["token"] = "never-store-this" });
                var welcome = Fixtures.Frame("welcome");
                welcome["configuration"]!["nested"] = JsonNode.Parse("""[{"access_token":"secret","PASSWORD":"secret","credential":"secret","authorization":"secret"}]""");
                recorder.Record(welcome); recorder.Record(Fixtures.Frame("game_start"));
                recorder.Record(Fixtures.Frame("tick")); recorder.Record(Fixtures.Frame("game_over"));
            }
            var data = File.ReadAllText(path);
            Assert.DoesNotContain("secret", data); Assert.DoesNotContain("never-store-this", data); Assert.Contains("[redacted]", data);
            Assert.Throws<IOException>(() => new BotRecorder(path));
            var bot = new ProbeBot { Tick = _ => new() { Throttle = .5 }, Continue = false };
            var decision = Assert.Single(Replay.Run(bot, path));
            Assert.Equal("fixture-match-1", decision.MatchId); Assert.Equal(.5, decision.Command["throttle"]!.GetValue<double>());
            Assert.Equal(1, bot.Starts); Assert.Equal(1, bot.Ends); Assert.Equal("disconnected", bot.Phase);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{}\n")]
    [InlineData("{\"format\":\"naval-sdk-bot-views\",\"version\":2}\n")]
    [InlineData("{\"format\":\"naval-sdk-bot-views\",\"version\":1}\n42\n")]
    public void InvalidRecordingsFailAndReleaseBot(string content)
    {
        var path = TempPath(); var bot = new Bot();
        try
        {
            File.WriteAllText(path, content);
            Assert.Throws<FormatException>(() => Replay.Run(bot, path).ToArray());
            Assert.Equal(0, bot.Running); Assert.Equal("disconnected", bot.Phase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParticipantFileParsesQuotesWithoutShellExpansion()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "# data only\nexport BATTLE_SERVER_URL='wss://example.com/bot'\nBATTLE_BOT_TOKEN='$(whoami) literal $HOME # token' # comment\n");
            var options = ConnectionArguments.Parse(["--env-file", path]).ToRunOptions();
            Assert.Equal("$(whoami) literal $HOME # token", options.Token);
            Assert.Equal("wss://example.com/bot", options.Url);
            var overridden = ConnectionArguments.Parse(["--env-file", path, "--host", "::1", "--port=9876"]).ToRunOptions();
            Assert.Equal("ws://[::1]:9876/bot", overridden.Url); Assert.Equal(options.Token, overridden.Token);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("BATTLE_SERVER_URL=ws://localhost/bot\n")]
    [InlineData("UNKNOWN=private-secret\n")]
    [InlineData("BATTLE_BOT_TOKEN='private-secret\n")]
    [InlineData("BATTLE_BOT_TOKEN=private-secret\nBATTLE_BOT_TOKEN=duplicate\n")]
    public void EnvFileValidationDoesNotEchoValues(string contents)
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, contents);
            var error = Assert.Throws<ArgumentException>(() => ConnectionArguments.ReadParticipantEnvironment(path));
            Assert.DoesNotContain("private-secret", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EnvFilesHaveSizeAndEncodingLimits()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, new string('x', 16385));
            Assert.Throws<ArgumentException>(() => ConnectionArguments.ReadParticipantEnvironment(path));
            File.WriteAllBytes(path, [0xff, 0xfe]);
            Assert.Throws<ArgumentException>(() => ConnectionArguments.ReadParticipantEnvironment(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("https://example.com/bot")]
    [InlineData("ws://name:secret@example.com/bot")]
    [InlineData("ws://example.com/bot?token=secret")]
    [InlineData("ws://example.com/bot#secret")]
    [InlineData("ws://example.com:99999/bot")]
    [InlineData("ws://example.com/a b")]
    public void UnsafeOrInvalidUrlsAreRejectedWithoutEchoing(string url)
    {
        var error = Assert.Throws<ArgumentException>(() => RunOptions.ValidateUrl(url));
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public void ExplicitOptionsTakePrecedenceOverAmbientEnvironment()
    {
        var oldUrl = Environment.GetEnvironmentVariable("BATTLE_SERVER_URL");
        var oldToken = Environment.GetEnvironmentVariable("BATTLE_BOT_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("BATTLE_SERVER_URL", "wss://ambient.example/bot");
            Environment.SetEnvironmentVariable("BATTLE_BOT_TOKEN", "ambient");
            var (url, token) = new RunOptions { Url = "ws://localhost:9876/bot", Token = "" }.Resolve();
            Assert.Equal(9876, url.Port); Assert.Equal("", token);
            Assert.Equal("ambient", new ConnectionArguments(Host: "localhost").ToRunOptions().Token);
            Assert.Throws<ArgumentException>(() => new ConnectionArguments(Url: "ws://localhost/bot", Host: "localhost").ToRunOptions());
        }
        finally { Environment.SetEnvironmentVariable("BATTLE_SERVER_URL", oldUrl); Environment.SetEnvironmentVariable("BATTLE_BOT_TOKEN", oldToken); }
    }
}
