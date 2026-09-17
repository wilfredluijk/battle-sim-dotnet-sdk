using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Naval.Sdk.Internal;

internal sealed class Session(Bot bot)
{
    private bool readySent;
    private IReadOnlyList<string>? loadout;
    internal GameOver? Result { get; private set; }
    internal bool Stop { get; private set; }
    internal bool Fatal { get; private set; }

    internal static T Call<T>(Bot bot, Func<T> callback, T fallback)
    {
        try { return callback(); }
        catch (Exception) { bot.Diagnostics.CallbackErrors++; return fallback; }
    }
    internal static void Call(Bot bot, Action callback) => Call(bot, () => { callback(); return true; }, false);

    private IReadOnlyList<JsonObject> Ready()
    {
        var welcome = bot.Welcome;
        if (welcome is null) return [];
        readySent = false;
        if (!Call(bot, () => bot.AcceptConfiguration(welcome.Configuration, welcome.ConfigHash), false)) return [];
        var picks = Call<IReadOnlyList<string>?>(bot, () => bot.ChoosePowerups(welcome), null)?.ToArray();
        if (picks is null || (picks.Length != 0 && picks.Length != 2) || picks.Distinct().Count() != picks.Length ||
            picks.Any(p => p is null || !welcome.AvailablePowerups.Contains(p))) return [];
        if (picks.Length == 0 && loadout is { Count: > 0 } && welcome.Rules?.SupportsEmptyLoadout != true) return [];
        var messages = new List<JsonObject>();
        if (picks.Length > 0 || loadout is { Count: > 0 })
            messages.Add(new() { ["type"] = "select_powerups", ["powerups"] = new JsonArray(picks.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) });
        loadout = picks;
        readySent = true;
        messages.Add(new() { ["type"] = "ready", ["config_hash"] = welcome.ConfigHash });
        return messages;
    }

    internal IReadOnlyList<JsonObject> Handle(JsonObject message)
    {
        try
        {
            switch (Wire.String(message, "type", ""))
            {
                case "welcome":
                    var welcome = Welcome.FromJson(message);
                    Wire.CheckProtocol(welcome.ProtocolVersion);
                    welcome = welcome.WithConfiguration(Wire.Object(message["configuration"]), Wire.String(message["config_hash"]));
                    Wire.CheckProtocol(welcome.ProtocolVersion);
                    bot.Welcome = welcome; bot.Phase = "lobby";
                    Call(bot, () => bot.OnWelcome(welcome));
                    return readySent ? [] : Ready();
                case "configuration":
                    if (bot.Welcome is null) return [];
                    var updated = bot.Welcome.WithConfiguration(Wire.Object(message["configuration"]), Wire.String(message["config_hash"]));
                    Wire.CheckProtocol(updated.ProtocolVersion);
                    bot.Welcome = updated; readySent = false;
                    Call(bot, () => bot.OnWelcome(updated));
                    return Ready();
                case "game_start":
                    var start = GameStart.FromJson(message);
                    if (start.MatchId.Length == 0 || bot.Welcome is null) throw new FormatException("A match ID and welcome are required.");
                    if (start.ShipSpecs is { } specs && (specs != bot.Welcome.ShipSpecs || start.SimulationDt != bot.Welcome.SimulationDt))
                    {
                        bot.Welcome = bot.Welcome with { ShipSpecs = specs, SimulationDt = start.SimulationDt };
                        Call(bot, () => bot.OnWelcome(bot.Welcome));
                    }
                    bot.Phase = "running"; bot.MatchId = start.MatchId; bot.LastTick = start.Tick;
                    Call(bot, () => bot.OnGameStartEvent(start));
                    break;
                case "tick":
                    var view = WorldView.FromJson(message);
                    if (view.MatchId.Length == 0 || bot.Welcome is null) throw new FormatException("A match ID and welcome are required.");
                    bot.LastTick = view.Tick; bot.MatchId = view.MatchId; bot.Phase = "running";
                    return [MakeCommand(view)];
                case "game_over":
                    Result = GameOver.FromJson(message); bot.Phase = "ended";
                    Stop = !Call(bot, () => bot.OnGameOver(Result), true); readySent = false;
                    break;
                case "lobby":
                    var tick = Wire.Int(message, "tick", 0);
                    bot.Phase = "lobby"; bot.LastTick = tick; bot.MatchId = "";
                    Call(bot, () => bot.OnLobby(tick));
                    if (!readySent) { loadout = null; return Ready(); }
                    break;
                case "error":
                    var code = Wire.String(message, "code", "unknown");
                    bot.Diagnostics.Rejections[code] = bot.Diagnostics.Rejections.GetValueOrDefault(code) + 1;
                    Fatal |= code is "unauthorized" or "invalid_name" or "duplicate_name" or "rate_limited";
                    var text = Wire.String(message, "message", "");
                    Call(bot, () => bot.OnError(code, text));
                    break;
            }
        }
        catch (ProtocolMismatchException) { Fatal = true; throw; }
        catch (Exception e) when (Wire.IsMalformed(e)) { bot.Diagnostics.MalformedFrames++; }
        return [];
    }

    private JsonObject MakeCommand(WorldView view)
    {
        var started = Stopwatch.GetTimestamp();
        var command = Call<Command?>(bot, () => bot.OnTick(view), null) ?? new();
        JsonObject payload;
        try
        {
            payload = command.ToJson(view.Tick, view.MatchId);
            if (command.ActivatePowerup is { } p && bot.Welcome?.AvailablePowerups.Contains(p) != true)
                throw new ArgumentException("Unknown powerup activation.");
            _ = payload.ToJsonString();
        }
        catch (Exception e) when (Wire.IsMalformed(e))
        {
            bot.Diagnostics.CallbackErrors++;
            payload = new Command().ToJson(view.Tick, view.MatchId);
        }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var timing = new TickTiming(view.MatchId, view.Tick, elapsed, view.DeadlineMs, elapsed > view.DeadlineMs);
        bot.Diagnostics.Ticks++; bot.Diagnostics.LastTiming = timing;
        if (timing.OverBudget) bot.Diagnostics.Overruns++;
        Call(bot, () => bot.OnTickTiming(timing));
        return payload;
    }
}
