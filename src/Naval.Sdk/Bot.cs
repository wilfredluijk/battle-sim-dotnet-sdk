using System.Text.Json.Nodes;

namespace Naval.Sdk;

public sealed class ProtocolMismatchException(string message) : Exception(message);
public sealed record TickTiming(string MatchId, int Tick, double ElapsedMs, int DeadlineMs, bool OverBudget);
public sealed record DisconnectInfo(int? Code, string Reason, string Phase);
public sealed class RuntimeDiagnostics
{
    public long Ticks { get; internal set; }
    public long Overruns { get; internal set; }
    /// <summary>Queued observations coalesced into a more recent tick, retaining their events.</summary>
    public long SkippedTicks { get; internal set; }
    /// <summary>Computed commands discarded because their tick was superseded or locally expired.</summary>
    public long DiscardedCommands { get; internal set; }
    public long CallbackErrors { get; internal set; }
    public long MalformedFrames { get; internal set; }
    internal Dictionary<string, long> Rejections { get; } = new();
    public IReadOnlyDictionary<string, long> RejectedCommands => Rejections.AsReadOnly();
    public TickTiming? LastTiming { get; internal set; }
    public DisconnectInfo? LastDisconnect { get; internal set; }
}

/// <summary>Override synchronous callbacks to build a bot. Exceptions in callbacks are isolated by the runtime.</summary>
public class Bot
{
    public Welcome? Welcome { get; protected internal set; }
    public int LastTick { get; internal set; }
    public string MatchId { get; internal set; } = "";
    public string Phase { get; internal set; } = "disconnected";
    public RuntimeDiagnostics Diagnostics { get; internal set; } = new();
    internal int Running;
    internal Func<JsonObject, CancellationToken, Task>? Sender;
    public virtual bool AcceptConfiguration(JsonObject configuration, string configHash) => true;
    public virtual void OnWelcome(Welcome welcome) { }
    public virtual IReadOnlyList<string> ChoosePowerups(Welcome welcome) => [];
    public virtual void OnGameStart(int tick, Vec2 startingPosition, double startingHeadingDeg) { }
    public virtual void OnGameStartEvent(GameStart start) => OnGameStart(start.Tick, start.StartingPosition, start.StartingHeadingDeg);
    public virtual Command? OnTick(WorldView view) => new();
    /// <summary>Return false to disconnect; true participates in subsequent matches.</summary>
    public virtual bool OnGameOver(GameOver result) => true;
    public virtual void OnLobby(int tick) { }
    public virtual void OnError(string code, string message) { }
    public virtual void OnTickTiming(TickTiming timing) { }
    public virtual void OnDisconnect(DisconnectInfo info) { }
    public Task RawSendAsync(JsonObject payload, CancellationToken cancellationToken = default) =>
        (Sender ?? throw new InvalidOperationException("No live connection is open."))(payload, cancellationToken);
    public Task<JsonObject> RawReceiveAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The runtime owns the receive loop. Use typed callbacks or a recorder.");
}
