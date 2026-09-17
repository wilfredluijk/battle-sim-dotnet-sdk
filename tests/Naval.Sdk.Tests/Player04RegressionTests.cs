using System.Net.WebSockets;
using System.Text.Json.Nodes;
using Naval.Sdk.Internal;
using Naval.Sdk.Tactical;
namespace Naval.Sdk.Tests;
public class Player04RegressionTests
{
    [Fact]
    public async Task EstablishedLobbySurvivesHandshakeDeadline()
    {
        using var server = new LocalServer(); var bot = new Bot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url, HandshakeTimeout = TimeSpan.FromMilliseconds(300) }, server.Token);
        using var socket = await server.Accept();
        await server.Read(socket); await server.Send(socket, Fixtures.Frame("welcome")); await server.Read(socket);
        await Task.Delay(750, server.Token);
        Assert.False(run.IsCompleted);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "audit complete", server.Token);
        Assert.Null(await run);
    }
    [Fact]
    public async Task UnexpectedTransportAbortIsReportedToCaller()
    {
        using var server = new LocalServer(); var bot = new Bot();
        var run = BotRunner.RunAsync(bot, new() { Url = server.Url }, server.Token);
        using var socket = await server.Accept();
        await server.Read(socket); await server.Send(socket, Fixtures.Frame("welcome")); await server.Read(socket);
        socket.Abort();
        await Assert.ThrowsAsync<IOException>(() => run);
    }
    [Fact]
    public void RunOptionsRedactsCredentials()
    {
        var options = new RunOptions { Token = "synthetic-audit-secret", Url = "wss://user:synthetic-url-secret@example.com/bot" };
        Assert.DoesNotContain("synthetic-audit-secret", options.ToString());
        Assert.DoesNotContain("synthetic-url-secret", options.ToString());
        Assert.Contains("[redacted]", options.ToString());
    }
    [Fact]
    public void GameStartRefreshesFullConfiguration()
    {
        var bot = new Bot(); var session = new Session(bot);
        session.Handle(Fixtures.Frame("welcome"));
        var start = Fixtures.Frame("game_start");
        var config = Fixtures.Frame("welcome")["configuration"]!.DeepClone();
        config["ship_specs"]!["shell_speed"] = 91;
        config["sim_config"]!["active_radar_range"] = 420;
        start["configuration"] = config; start["config_hash"] = "audit-changed";
        start["ship_specs"]!["shell_speed"] = 91;
        session.Handle(start);
        Assert.Equal(91, bot.Welcome!.ShipSpecs.ShellSpeed);
        Assert.Equal(91, bot.Welcome.Rules!.ShipSpecs.ShellSpeed);
        Assert.Equal(420, bot.Welcome.Rules.Sensors.ActiveRadarRange);
        Assert.Equal("audit-changed", bot.Welcome.ConfigHash);
        Assert.Equal(420, bot.Welcome.Configuration["sim_config"]!["active_radar_range"]!.GetValue<int>());
    }
    [Fact]
    public void LobbyRestoresAnnouncedConfiguration()
    {
        var bot = new Bot(); var session = new Session(bot);
        session.Handle(Fixtures.Frame("welcome"));
        var start = Fixtures.Frame("game_start"); start["ship_specs"]!["shell_speed"] = 91;
        session.Handle(start); session.Handle(Fixtures.Frame("game_over"));
        var welcome = Fixtures.Frame("welcome");
        session.Handle(new JsonObject { ["type"] = "lobby", ["tick"] = 0, ["configuration"] = welcome["configuration"]!.DeepClone(), ["config_hash"] = welcome["config_hash"]!.DeepClone() });
        Assert.Equal(Fixtures.Specs.ShellSpeed, bot.Welcome!.ShipSpecs.ShellSpeed);
        Assert.Equal(Fixtures.Specs.ShellSpeed, bot.Welcome.Rules!.ShipSpecs.ShellSpeed);
        Assert.Equal(welcome["config_hash"]!.GetValue<string>(), bot.Welcome.ConfigHash);
    }
    [Fact]
    public void ShellContactDoesNotUpdateShipTrack()
    {
        var tracker = new Tracker(Fixtures.Specs);
        tracker.Update(Fixtures.View(1, contacts: [new("c1", "ship", new(100,100),90,100,1)]));
        var tracks = tracker.Update(Fixtures.View(2, contacts: [new("c2", "shell", new(100,100),90,100,1)]));
        Assert.Equal(2, tracks.Count);
        Assert.Equal(1, tracks.Single(t => t.Kind == "ship").LastActiveTick);
    }
    [Fact]
    public void ExpiredTrackIsNotRevivedBeforePruning()
    {
        var tracker = new Tracker(Fixtures.Specs, stalenessTicks: 2);
        tracker.Update(Fixtures.View(10, contacts: [new("c1", "ship", new(100,100),90,100,1)]));
        var tracks = tracker.Update(Fixtures.View(14, contacts: [new("c2", "ship", new(100,100),90,100,1)]));
        Assert.Equal(14, Assert.Single(tracks).FirstSeenTick);
    }

    [Theory]
    [InlineData("game_start")]
    [InlineData("lobby")]
    public void InvalidLifecycleConfigurationIsAtomic(string type)
    {
        var bot = new ProbeBot(); var session = Fixtures.Start(bot);
        var original = bot.Welcome; var phase = bot.Phase;
        var message = type == "game_start" ? Fixtures.Frame(type) : new JsonObject { ["type"] = "lobby", ["tick"] = 0 };
        message["configuration"] = Fixtures.Frame("welcome")["configuration"]!.DeepClone();
        message["configuration"]!["simulation_dt"] = 0;
        message["config_hash"] = "invalid";
        Assert.Empty(session.Handle(message));
        Assert.Same(original, bot.Welcome); Assert.Equal(phase, bot.Phase);
        Assert.Equal(1, bot.Diagnostics.MalformedFrames);
    }

    [Theory]
    [InlineData("game_start")]
    [InlineData("lobby")]
    public void LifecycleConfigurationChecksProtocolVersion(string type)
    {
        var bot = new Bot(); var session = Fixtures.Start(bot); var original = bot.Welcome;
        var message = type == "game_start" ? Fixtures.Frame(type) : new JsonObject { ["type"] = "lobby", ["tick"] = 0 };
        message["configuration"] = Fixtures.Frame("welcome")["configuration"]!.DeepClone();
        message["configuration"]!["protocol_version"] = "4.0";
        message["config_hash"] = "unsupported";
        Assert.Throws<ProtocolMismatchException>(() => session.Handle(message));
        Assert.Same(original, bot.Welcome); Assert.True(session.Fatal);
    }

    [Fact]
    public void LegacyStartWithOnlyDtUpdatesAllRepresentations()
    {
        var bot = new Bot(); var session = Fixtures.Start(bot);
        var start = Fixtures.Frame("game_start"); start.Remove("ship_specs"); start["simulation_dt"] = .2;
        session.Handle(start);
        Assert.Equal(.2, bot.Welcome!.SimulationDt); Assert.Equal(.2, bot.Welcome.Rules!.SimulationDt);
        Assert.Equal(.2, bot.Welcome.Configuration["simulation_dt"]!.GetValue<double>());
        start.Remove("simulation_dt"); session.Handle(start);
        Assert.Equal(.2, bot.Welcome.SimulationDt);
    }

    [Fact]
    public void UpdatedLobbyAcknowledgesNewHashAndDerivedRules()
    {
        var bot = new Bot(); var session = Fixtures.Start(bot); session.Handle(Fixtures.Frame("game_over"));
        var message = new JsonObject { ["type"] = "lobby", ["tick"] = 0, ["config_hash"] = "new-lobby", ["configuration"] = Fixtures.Frame("welcome")["configuration"]!.DeepClone() };
        message["configuration"]!["sim_config"]!["powerups"]!["rapid_fire_cooldown_mult"] = .25;
        var ready = Assert.Single(session.Handle(message));
        Assert.Equal("new-lobby", ready["config_hash"]!.GetValue<string>());
        Assert.Equal(.25, bot.Welcome!.Rules!.Powerups.RapidFireCooldownMult);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void PassiveShellCannotRefreshShipAndBackwardTickCannotReuseHistory(int tick)
    {
        var tracker = new Tracker(Fixtures.Specs);
        tracker.Update(Fixtures.View(1, contacts: [new("ship", "ship", new(200,100),90,100,1)]));
        var tracks = tracker.Update(Fixtures.View(tick, contacts: [new("shell", "shell", new(200,100),90,null,1)]));
        if (tick == 0) Assert.Empty(tracks);
        else Assert.Equal(1, Assert.Single(tracks).LastSeenTick);
    }

    [Fact]
    public void UnknownContactCanBecomeKnownWithoutLosingTrack()
    {
        var tracker = new Tracker(Fixtures.Specs);
        var old = Assert.Single(tracker.Update(Fixtures.View(1, contacts: [new("a", "unknown", new(200,100),90,100,1)])));
        var current = Assert.Single(tracker.Update(Fixtures.View(2, contacts: [new("b", "ship", new(200,100),90,100,1)])));
        Assert.Same(old, current); Assert.Equal("ship", current.Kind);
    }
}
