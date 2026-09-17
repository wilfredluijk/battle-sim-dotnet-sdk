using Naval.Sdk;
using Naval.Sdk.Tactical;

if (args.Contains("--help"))
{
    Console.WriteLine("HunterBot [--url WS_URL | --host HOST --port PORT] [--env-file PATH]");
    Console.WriteLine("Credentials: BATTLE_BOT_TOKEN or the selected participant file.");
    return 0;
}
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
try
{
    var options = ConnectionArguments.Parse(args).ToRunOptions("dotnet-hunter");
    var result = await BotRunner.RunAsync(new HunterBot(), options, stop.Token);
    return result is null ? 1 : 0;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
catch (Exception e) when (e is ArgumentException or IOException or ProtocolMismatchException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

sealed class HunterBot : TacticalBot
{
    public HunterBot() => SensorPolicy = new PingWhenStale();
    public override Intent Decide(TacticalContext context) => context.Threats.Nearest() is { } target
        ? Intent.Engage(target)
        : Intent.Patrol(new(80, 80, context.MapWidth - 80, context.MapHeight - 80));
    public override bool OnGameOver(GameOver result)
    {
        Console.WriteLine($"Match finished after {result.FinalTick} ticks. Winner: {result.Winner ?? "draw"}.");
        return true;
    }
    public override void OnError(string code, string message) => Console.Error.WriteLine($"Server error: {code}");
    public override void OnTickTiming(TickTiming timing)
    {
        if (timing.OverBudget && (Diagnostics.Overruns == 1 || Diagnostics.Overruns % 50 == 0))
            Console.Error.WriteLine($"Decision took {timing.ElapsedMs:F1} ms against a {timing.DeadlineMs} ms deadline. Reduce work in Decide; expired commands are discarded.");
    }
    public override void OnDisconnect(DisconnectInfo info) =>
        Console.Error.WriteLine($"Disconnected during {info.Phase}; close code: {info.Code?.ToString() ?? "none"}.");
}
