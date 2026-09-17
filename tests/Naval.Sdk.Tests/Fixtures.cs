using System.Text.Json.Nodes;
using Naval.Sdk.Internal;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Naval.Sdk.Tests;

internal static class Fixtures
{
    internal static JsonObject Frame(string key) => JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "protocol3.json")))![key]!.AsObject();
    internal static Welcome Welcome => Sdk.Welcome.FromJson(Frame("welcome"));
    internal static ShipSpecs Specs => Welcome.ShipSpecs;
    internal static SelfState Me => new(new(100, 100), 90, 0, 100, 250, 0, 0);
    internal static WorldView View(int tick = 0, SelfState? me = null, IReadOnlyList<Contact>? contacts = null, IReadOnlyList<TickEvent>? events = null) =>
        new(tick, 80, me ?? Me, contacts ?? [], events ?? [], "test-match");
    internal static JsonObject Update() => new() { ["type"] = "configuration", ["config_hash"] = "updated", ["configuration"] = Frame("welcome")["configuration"]!.DeepClone() };
    internal static Session Start(Bot bot)
    {
        var session = new Session(bot);
        session.Handle(Frame("welcome"));
        session.Handle(Frame("game_start"));
        return session;
    }
}

internal sealed class ProbeBot : Bot
{
    internal Func<WorldView, Command?> Tick { get; set; } = _ => new();
    internal Func<Welcome, IReadOnlyList<string>> Picks { get; set; } = _ => [];
    internal bool Accept { get; set; } = true;
    internal bool Continue { get; set; } = true;
    internal int Starts { get; private set; }
    internal int Welcomes { get; private set; }
    internal int Ends { get; private set; }
    public override Command? OnTick(WorldView view) => Tick(view);
    public override bool AcceptConfiguration(JsonObject configuration, string configHash) => Accept;
    public override IReadOnlyList<string> ChoosePowerups(Welcome welcome) => Picks(welcome);
    public override void OnWelcome(Welcome welcome) => Welcomes++;
    public override void OnGameStart(int tick, Vec2 position, double heading) => Starts++;
    public override bool OnGameOver(GameOver result) { Ends++; return Continue; }
}
