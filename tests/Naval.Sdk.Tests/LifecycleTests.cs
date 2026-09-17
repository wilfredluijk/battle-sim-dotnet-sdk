using System.Text.Json.Nodes;
using Naval.Sdk.Internal;

namespace Naval.Sdk.Tests;

public class LifecycleTests
{
    [Fact]
    public void HandshakeAcknowledgesConfigurationAndLoadoutInOrder()
    {
        var bot = new ProbeBot { Picks = _ => ["rapid_fire", "heavy_shell"] };
        var session = new Session(bot);
        var frames = session.Handle(Fixtures.Frame("welcome"));
        Assert.Equal(new[] { "select_powerups", "ready" }, frames.Select(f => f["type"]!.GetValue<string>()));
        Assert.Equal("fixture-config-1", frames[1]["config_hash"]!.GetValue<string>());
        Assert.Empty(session.Handle(new() { ["type"] = "lobby" }));
    }

    [Fact]
    public void MultipleRoundsResendReadyAndUseNewMatchId()
    {
        var bot = new ProbeBot();
        var session = Fixtures.Start(bot);
        session.Handle(Fixtures.Frame("tick"));
        session.Handle(Fixtures.Frame("game_over"));
        Assert.False(session.Stop);
        Assert.Single(session.Handle(new() { ["type"] = "lobby", ["tick"] = 0 }));
        var start = Fixtures.Frame("game_start"); start["match_id"] = "second";
        session.Handle(start);
        var tick = Fixtures.Frame("tick"); tick["match_id"] = "second";
        var command = Assert.Single(session.Handle(tick));
        Assert.Equal("second", command["match_id"]!.GetValue<string>());
        Assert.Equal(2, bot.Starts); Assert.Equal(1, bot.Ends); Assert.Equal(2, bot.Diagnostics.Ticks);
    }

    [Fact]
    public void BotCanOptOutAfterGameOver()
    {
        var bot = new ProbeBot { Continue = false };
        var session = Fixtures.Start(bot);
        session.Handle(Fixtures.Frame("game_over"));
        Assert.True(session.Stop); Assert.NotNull(session.Result);
    }

    [Theory]
    [InlineData("rapid_fire")]
    [InlineData("rapid_fire,rapid_fire")]
    [InlineData("rapid_fire,missing")]
    [InlineData("rapid_fire,heavy_shell,overdrive")]
    public void InvalidLoadoutLeavesBotUnready(string picks)
    {
        var session = new Session(new ProbeBot { Picks = _ => picks.Split(',') });
        Assert.Empty(session.Handle(Fixtures.Frame("welcome")));
    }

    [Fact]
    public void RefusedConfigurationDoesNotSendReady() => Assert.Empty(new Session(new ProbeBot { Accept = false }).Handle(Fixtures.Frame("welcome")));

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public void ClearingLoadoutRequiresServerCapability(bool supported, int count)
    {
        var bot = new ProbeBot { Picks = w => w.ConfigHash == "updated" ? [] : ["rapid_fire", "heavy_shell"] };
        var session = Fixtures.Start(bot);
        var update = Fixtures.Update(); update["configuration"]!["capabilities"]!["empty_loadout"] = supported;
        var frames = session.Handle(update);
        Assert.Equal(count, frames.Count);
        if (supported) Assert.Empty(frames[0]["powerups"]!.AsArray());
    }

    [Theory]
    [InlineData("{\"simulation_dt\":0}")]
    [InlineData("{\"map\":{\"width\":-1,\"height\":10}}")]
    [InlineData("{\"ship_specs\":{}}")]
    [InlineData("{\"available_powerups\":42}")]
    [InlineData("{\"capabilities\":{\"empty_loadout\":1}}")]
    [InlineData("{\"tick_hz\":1.5}")]
    public void BadConfigurationIsAtomicAndNeverAcknowledged(string changes)
    {
        var bot = new ProbeBot(); var session = Fixtures.Start(bot);
        var original = bot.Welcome;
        var update = Fixtures.Update();
        foreach (var pair in JsonNode.Parse(changes)!.AsObject()) update["configuration"]![pair.Key] = pair.Value?.DeepClone();
        Assert.Empty(session.Handle(update)); Assert.Same(original, bot.Welcome);
        Assert.Equal(1, bot.Diagnostics.MalformedFrames);
        Assert.Single(session.Handle(Fixtures.Frame("tick")));
    }

    [Fact]
    public void ConfigurationRefreshesAllDerivedFields()
    {
        var bot = new ProbeBot(); var session = Fixtures.Start(bot); var update = Fixtures.Update();
        update["configuration"]!["map"]!["width"] = 900;
        update["configuration"]!["simulation_dt"] = .2;
        update["configuration"]!["tick_hz"] = 60;
        Assert.Single(session.Handle(update));
        Assert.Equal(900, bot.Welcome!.Map.Width); Assert.Equal(.2, bot.Welcome.SimulationDt); Assert.Equal(60, bot.Welcome.TickHz);
        Assert.Equal("updated", bot.Welcome.ConfigHash);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("4.0")]
    [InlineData("3")]
    [InlineData("3.x")]
    public void ProtocolMismatchStopsSession(string version)
    {
        var frame = Fixtures.Frame("welcome"); frame["protocol_version"] = version;
        var session = new Session(new Bot());
        Assert.Throws<ProtocolMismatchException>(() => session.Handle(frame)); Assert.True(session.Fatal);
    }

    [Fact]
    public void GameStartSpecsRefreshBeforeStartCallback()
    {
        var bot = new ProbeBot(); var session = Fixtures.Start(bot);
        var start = Fixtures.Frame("game_start"); start["ship_specs"]!["shell_speed"] = 123; start["simulation_dt"] = .2;
        session.Handle(start);
        Assert.Equal(2, bot.Welcomes); Assert.Equal(2, bot.Starts);
        Assert.Equal(123, bot.Welcome!.ShipSpecs.ShellSpeed); Assert.Equal(.2, bot.Welcome.SimulationDt);
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("nan")]
    [InlineData("unknown_powerup")]
    [InlineData("sensor")]
    public void InvalidCommandsFallBackAndNextTickContinues(string kind)
    {
        var bot = new ProbeBot { Tick = _ => kind switch
        {
            "throws" => throw new InvalidOperationException(), "nan" => new() { Throttle = double.NaN },
            "sensor" => new() { SensorMode = (SensorMode)42 }, _ => new() { ActivatePowerup = "unknown" }
        } };
        var session = Fixtures.Start(bot);
        foreach (var tick in new[] { 1, 2 })
        {
            var frame = Fixtures.Frame("tick"); frame["tick"] = tick;
            var command = Assert.Single(session.Handle(frame));
            Assert.Equal(0, command["throttle"]!.GetValue<double>()); Assert.Equal(tick, command["tick"]!.GetValue<int>());
        }
        Assert.Equal(2, bot.Diagnostics.CallbackErrors); Assert.Equal(2, bot.Diagnostics.Ticks);
    }

    [Fact]
    public void NullCommandIsAValidHold()
    {
        var bot = new ProbeBot { Tick = _ => null }; var session = Fixtures.Start(bot);
        Assert.Single(session.Handle(Fixtures.Frame("tick"))); Assert.Equal(0, bot.Diagnostics.CallbackErrors);
    }

    [Fact]
    public void DiagnosticsCountErrorsMalformedFramesAndOverruns()
    {
        var bot = new ProbeBot { Tick = _ => { Thread.Sleep(5); return new(); } }; var session = Fixtures.Start(bot);
        session.Handle(new() { ["type"] = "tick" });
        session.Handle(new() { ["type"] = "error", ["code"] = "stale_tick", ["message"] = "old" });
        var tick = Fixtures.Frame("tick"); tick["deadline_ms"] = 0; session.Handle(tick);
        Assert.Equal(1, bot.Diagnostics.MalformedFrames); Assert.Equal(1, bot.Diagnostics.RejectedCommands["stale_tick"]);
        Assert.Equal(1, bot.Diagnostics.Overruns); Assert.True(bot.Diagnostics.LastTiming!.OverBudget);
    }
}
