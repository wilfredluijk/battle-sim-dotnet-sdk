using System.Text.Json.Nodes;

namespace Naval.Sdk.Tests;

public class ProtocolTests
{
    [Fact]
    public void ParsesOriginalPythonFixture()
    {
        var welcome = Fixtures.Welcome;
        Assert.Equal("3.0", welcome.ProtocolVersion);
        Assert.Equal(12, welcome.AvailablePowerups.Count);
        Assert.Equal(70, welcome.ShipSpecs.ShellSpeed);
        Assert.True(welcome.Rules!.SupportsEmptyLoadout);
        Assert.True(welcome.Rules.SupportsOwnShipTelemetry);
        Assert.Equal(0.5, welcome.Rules.Powerups.RapidFireCooldownMult);
        Assert.Equal("fixture-match-1", GameStart.FromJson(Fixtures.Frame("game_start")).MatchId);
        var view = WorldView.FromJson(Fixtures.Frame("tick"));
        Assert.Equal(new Vec2(500, 500), view.Me.Pos);
        Assert.Null(view.Me.GunCooldownTicksLeft);
        Assert.Equal(142, GameOver.FromJson(Fixtures.Frame("game_over")).FinalTick);
    }

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1, 0, 90)]
    [InlineData(0, 1, 180)]
    [InlineData(-1, 0, 270)]
    public void CompassBearings(double x, double y, double expected) => Assert.Equal(expected, Helpers.BearingTo(default, new(x, y)), 8);

    [Theory]
    [InlineData(10, 350, 20)]
    [InlineData(350, 10, -20)]
    [InlineData(180, 0, -180)]
    [InlineData(-1080, 0, 0)]
    public void SignedBearingWrapsLikePython(double target, double current, double expected) => Assert.Equal(expected, Helpers.SignedBearingDelta(target, current), 8);

    [Theory]
    [InlineData(0, 10, 100)]
    [InlineData(5, 10, 200)]
    [InlineData(-10, 10, 50)]
    public void LeadsLinearTarget(double velocity, double speed, double expected) =>
        Assert.Equal(expected, Helpers.LeadTarget(default, new(100, 0), new(velocity, 0), speed)!.Value.X, 8);

    [Fact]
    public void ImpossibleInterceptReturnsNull()
    {
        Assert.Null(Helpers.LeadTarget(default, new(100, 0), new(20, 0), 10));
        Assert.Null(Helpers.LeadTarget(default, new(100, 0), new(10, 0), 10));
        Assert.Null(Helpers.LeadTarget(default, new(100, 0), default, 0));
        Assert.Equal(default(Vec2), Helpers.LeadTarget(default, default, new(10, 0), 10));
    }

    [Fact]
    public void CommandsUseServerFieldNamesAndOmitOptionalFields()
    {
        var command = new Command { Throttle = .6, Rudder = -.2, SensorMode = SensorMode.Passive };
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"command","tick":9,"match_id":"round-2","throttle":0.6,"rudder":-0.2,"sensor_mode":"passive"}"""), command.ToJson(9, "round-2")));
        command.FireAt(new(200, 100), shooterPos: new(100, 100));
        Assert.Equal(new FireCommand(90, 100), command.Fire);
        command.ActivatePowerup = "future-powerup";
        Assert.Equal("future-powerup", command.ToJson(1)["activate_powerup"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e39)]
    public void NonfiniteOrOutOfRangeCommandsAreRejected(double value) => Assert.Throws<ArgumentException>(() => new Command { Throttle = value }.ToJson(1));

    [Theory]
    [InlineData("{\"type\":\"hit\"}")]
    [InlineData("{\"type\":\"shell_splash\",\"pos\":[1]}")]
    [InlineData("{\"type\":\"future\",\"data\":42}")]
    [InlineData("42")]
    [InlineData("null")]
    public void UnknownAndMalformedEventsSurvive(string json)
    {
        var node = JsonNode.Parse(json);
        var parsed = Assert.IsType<UnknownEvent>(TickEvent.FromJson(node));
        Assert.True(JsonNode.DeepEquals(node, parsed.Raw));
    }

    [Fact]
    public void EventsAndPowerupsAreTyped()
    {
        Assert.Equal(new HitEvent(4), TickEvent.FromJson(JsonNode.Parse("""{"type":"hit","amount":4}""")));
        Assert.Equal(new ShellSplashEvent(new(1, 2)), TickEvent.FromJson(JsonNode.Parse("""{"type":"shell_splash","pos":[1,2]}""")));
        Assert.Equal(new PowerupActivatedEvent(true, null, "overdrive"), TickEvent.FromJson(JsonNode.Parse("""{"type":"powerup_activated","own":true,"powerup":"overdrive"}""")));
        var me = Fixtures.Me with { PowerupStatus = [new("rapid_fire"), new("overdrive", true, 5)] };
        Assert.True(me.PowerupReady("rapid_fire"));
        Assert.True(me.PowerupActive("overdrive"));
        Assert.False(me.PowerupReady("overdrive"));
        Assert.False(me.PowerupActive("missing"));
    }

    [Fact]
    public void UnknownConfigurationDataIsPreservedWithoutAliasing()
    {
        var frame = Fixtures.Frame("welcome");
        frame["configuration"]!["future"] = new JsonObject { ["value"] = 4 };
        var welcome = Welcome.FromJson(frame);
        frame["configuration"]!["future"]!["value"] = 10;
        welcome.Configuration["future"]!["value"] = 20;
        welcome.Rules!.Raw["future"]!["value"] = 30;
        Assert.Equal(4, welcome.Configuration["future"]!["value"]!.GetValue<int>());
        Assert.Equal(4, welcome.Rules.Raw["future"]!["value"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("{\"long_range_speed_mult\":0}")]
    [InlineData("{\"overdrive_duration_ticks\":1.5}")]
    [InlineData("{\"rapid_fire_cooldown_mult\":true}")]
    [InlineData("{\"awacs_silent_confidence\":2}")]
    [InlineData("{\"decoy_flare_distance_min\":999}")]
    public void InvalidPowerupRulesAreRejected(string json) => Assert.ThrowsAny<Exception>(() => PowerupConfig.FromJson(JsonNode.Parse(json)!.AsObject()));
}
