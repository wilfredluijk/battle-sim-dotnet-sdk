using System.Text.Json.Nodes;
using Naval.Sdk.Tactical;

namespace Naval.Sdk.Tests;

public class TacticalTests
{
    private static Track Target(Vec2? pos = null, int tick = 0) => new() { TrackId = 1, Kind = "ship", Pos = pos ?? new(200, 100), ObservedPos = pos ?? new(200, 100), LastActiveTick = tick };

    [Fact]
    public void TacticalOutputsMatch140TicksFromOriginalPythonSdk()
    {
        var oracle = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "python-parity.json")))!;
        var tracker = new Tracker(Fixtures.Specs, tickHz: 60);
        var gunner = new Gunner(Fixtures.Specs); var helm = new Helm(Fixtures.Specs); var evader = new Evader();
        ISensorPolicy[] policies = [new AlwaysActive(), new AlwaysPassive(), new DutyCycle(), new PingWhenStale()];
        var hunter = new Hunter(); hunter.OnWelcome(Fixtures.Welcome);
        foreach (var item in oracle["cases"]!.AsArray())
        {
            var view = WorldView.FromJson(item!["view"]!.AsObject());
            var tracks = tracker.Update(view);
            var expected = item["tracks"]!.AsArray();
            Assert.Equal(expected.Count, tracks.Count);
            for (var i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i]; var e = expected[i]!;
                Assert.Equal(e["track_id"]!.GetValue<int>(), t.TrackId);
                Assert.Equal(e["last_active_tick"]!.GetValue<int>(), t.LastActiveTick);
                Assert.Equal(e["last_seen_tick"]!.GetValue<int>(), t.LastSeenTick);
                Assert.Equal(e["source"]!.GetValue<string>(), t.Source);
                Assert.Equal(e["kind"]!.GetValue<string>(), t.Kind);
                Near(e["pos"]![0]!.GetValue<double>(), t.Pos.X); Near(e["pos"]![1]!.GetValue<double>(), t.Pos.Y);
                Near(e["vel"]![0]!.GetValue<double>(), t.Vel.X); Near(e["vel"]![1]!.GetValue<double>(), t.Vel.Y);
            }
            gunner.Update(view);
            var solution = tracks.Count > 0 ? gunner.Solve(view.Me, tracks[0], view) : null;
            if (item["solution"] is { } shot)
            {
                Assert.NotNull(solution); Near(shot["bearing_deg"]!.GetValue<double>(), solution.BearingDeg); Near(shot["range"]!.GetValue<double>(), solution.Range);
            }
            else Assert.Null(solution);
            var steering = helm.SteerToPoint(view.Me, new(400, 200));
            Near(item["helm"]![0]!.GetValue<double>(), steering.Throttle); Near(item["helm"]![1]!.GetValue<double>(), steering.Rudder);
            for (var i = 0; i < policies.Length; i++) Assert.Equal(item["sensors"]![i]!.GetValue<string>(), policies[i].Choose(view, tracker).ToString().ToLowerInvariant());
            CompareJson(item["evade"], evader.Update(view)?.ToJson(view.Tick, view.MatchId));
            CompareJson(item["command"], hunter.OnTick(view).ToJson(view.Tick, view.MatchId));
        }
    }

    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-8);
    private static void CompareJson(JsonNode? expected, JsonNode? actual)
    {
        if (expected is null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        if (expected is JsonObject o)
        {
            Assert.Equal(o.Count, actual.AsObject().Count);
            foreach (var p in o) CompareJson(p.Value, actual[p.Key]);
        }
        else if (expected is JsonValue v && v.TryGetValue<double>(out var number)) Near(number, Internal.Wire.Number(actual));
        else Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected {expected}, actual {actual}");
    }

    [Theory]
    [InlineData(5)] [InlineData(10)] [InlineData(20)] [InlineData(60)]
    public void TrackerUsesSimulationTimeNotPacing(int hz)
    {
        var tracker = new Tracker(Fixtures.Specs, tickHz: hz);
        for (var tick = 0; tick < 100; tick++) tracker.Update(Fixtures.View(tick, contacts: [new($"{tick}", "ship", new(200 + tick * .9, 100), 90, 100, 1)]));
        Assert.Equal(9, Assert.Single(tracker.Tracks).Vel.X, 8);
    }

    [Fact]
    public void PassiveObservationsNeverSpawnOrRefreshActiveFix()
    {
        var tracker = new Tracker(Fixtures.Specs); var policy = new PingWhenStale();
        Contact passive = new("passive", "ship", new(350, 100), 90, null, .6);
        Assert.Empty(tracker.Update(Fixtures.View(0, contacts: [passive])));
        tracker.Update(Fixtures.View(0, contacts: [passive with { Range = 100, Pos = new(200, 100) }]));
        for (var tick = 1; tick < 100; tick++)
        {
            var view = Fixtures.View(tick, contacts: [passive]); tracker.Update(view);
            if (tick >= 4) Assert.Equal(SensorMode.Active, policy.Choose(view, tracker));
        }
        Assert.Equal(0, Assert.Single(tracker.Tracks).LastActiveTick);
    }

    [Fact]
    public void NewRoundClearsEveryTacticalSubsystem()
    {
        var hunter = new Hunter(); hunter.OnWelcome(Fixtures.Welcome);
        hunter.OnTick(Fixtures.View(90, contacts: [new("one", "ship", new(200, 100), 90, 100, 1)], events: [new HitEvent(1)]));
        hunter.Gunner!.NoteFired(90);
        hunter.OnGameStart(0, new(100, 100), 90);
        Assert.Empty(hunter.Tracker!.Tracks); Assert.Equal(0, hunter.Gunner.NextFireTick); Assert.Equal(EvaderState.Idle, hunter.Evader!.State);
        hunter.OnTick(Fixtures.View(0)); Assert.Empty(hunter.Tracker.Tracks);
    }

    [Fact]
    public void TrackerPrunesBackwardTicksAndStaleTracks()
    {
        var tracker = new Tracker(Fixtures.Specs);
        tracker.Update(Fixtures.View(100, contacts: [new("one", "ship", new(200, 100), 90, 100, 1)]));
        Assert.Empty(tracker.Update(Fixtures.View(0)));
        tracker.Update(Fixtures.View(0, contacts: [new("one", "ship", new(200, 100), 90, 100, 1)]));
        Assert.Empty(tracker.Update(Fixtures.View(41)));
    }

    [Fact]
    public void WeaponsRespectSameTickPowerupsEmpAndRustRounding()
    {
        var gunner = new Gunner(Fixtures.Specs);
        var me = Fixtures.Me with { PowerupStatus = [new("rapid_fire"), new("long_range_salvo", true, 30)] };
        var command = new Command { ActivatePowerup = "rapid_fire" };
        Assert.True(gunner.Attempt(command, me, Target(), Fixtures.View(me: me))); Assert.Equal(8, gunner.NextFireTick);
        Assert.Equal((112d, 450d, 15d, 8), gunner.EffectiveWeapons(me, "rapid_fire"));
        Assert.Equal(15, gunner.EffectiveWeapons(me with { EmpTicksLeft = 10 }, "rapid_fire").Cooldown);
        Assert.NotNull(new Gunner(Fixtures.Specs).Solve(me, Target(new(500, 100)), Fixtures.View(me: me)));
        Assert.Null(new Gunner(Fixtures.Specs).Solve(Fixtures.Me, Target(new(500, 100)), Fixtures.View()));
    }

    [Fact]
    public void GunnerUsesAuthoritativeCooldownAndReconcilesRejectedShots()
    {
        var gunner = new Gunner(Fixtures.Specs); gunner.NoteFired(100);
        Assert.True(gunner.CanFire(Fixtures.View(), Fixtures.Me with { GunCooldownTicksLeft = 0 }));
        Assert.False(gunner.CanFire(Fixtures.View(200), Fixtures.Me with { GunCooldownTicksLeft = 8 }));
        gunner.Reset();
        Assert.True(gunner.Attempt(new(), Fixtures.Me, Target(), Fixtures.View()));
        Assert.True(gunner.Attempt(new(), Fixtures.Me, Target(tick: 1), Fixtures.View(1)));
        var spent = Fixtures.Me with { Ammo = 249 };
        Assert.False(gunner.Attempt(new(), spent, Target(tick: 2), Fixtures.View(2, spent)));
    }

    [Fact]
    public void SplashGuardAccountsForHeavyShellAndFutureOwnPosition()
    {
        var gunner = new Gunner(Fixtures.Specs, powerups: new() { HeavyShellSplashMult = 2 });
        var me = Fixtures.Me with { PowerupStatus = [new("heavy_shell")] };
        Assert.NotNull(gunner.Solve(me, Target(new(135, 100)), Fixtures.View(me: me)));
        Assert.Null(gunner.Solve(me, Target(new(135, 100)), Fixtures.View(me: me), "heavy_shell"));
        Assert.Null(gunner.Solve(me with { Speed = 9 }, Target(new(134, 100)), Fixtures.View(me: me)));
    }

    [Fact]
    public void EvasionPreemptsCustomIntentAndFlipsAfterAnotherHit()
    {
        var evader = new Evader(2, 2);
        Assert.Equal(1, evader.Update(Fixtures.View(0, events: [new HitEvent(1)]))!.Rudder);
        Assert.Null(evader.Update(Fixtures.View(2)));
        Assert.Equal(-1, evader.Update(Fixtures.View(3, events: [new HitEvent(1)]))!.Rudder);
        evader.Reset(); Assert.Null(evader.Update(Fixtures.View()));
        var bot = new CustomBot(); bot.OnWelcome(Fixtures.Welcome);
        Assert.Equal(-.5, bot.OnTick(Fixtures.View()).Throttle);
        Assert.Equal(1, bot.OnTick(Fixtures.View(1, events: [new HitEvent(1)])).Throttle);
    }

    private sealed class Hunter : TacticalBot
    {
        internal Hunter() => SensorPolicy = new PingWhenStale();
        public override Intent Decide(TacticalContext context) => context.Threats.Nearest() is { } t ? Intent.Engage(t) : Intent.Patrol(new(80, 80, 620, 620));
    }
    private sealed class CustomBot : TacticalBot
    {
        public override Intent Decide(TacticalContext context) => Intent.Custom(new() { Throttle = -.5 });
    }
}
