using System.Text;
using System.Text.Json.Nodes;
using Naval.Sdk.Internal;

namespace Naval.Sdk;

/// <summary>Records inbound bot views in the Python-compatible JSONL format. Never overwrites a file.</summary>
public sealed class BotRecorder : IDisposable
{
    private readonly StreamWriter writer;
    private static readonly HashSet<string> Types = ["welcome", "configuration", "game_start", "tick", "game_over", "lobby", "error"];
    public string Path { get; }
    public BotRecorder(string path)
    {
        Path = path;
        writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        writer.WriteLine("{\"format\":\"naval-sdk-bot-views\",\"version\":1}");
        writer.Flush();
    }
    public void Record(JsonObject message)
    {
        if (message["type"] is JsonValue v && v.TryGetValue<string>(out var type) && Types.Contains(type))
        {
            writer.WriteLine(Redact(message)!.ToJsonString());
            writer.Flush();
        }
    }
    private static JsonNode? Redact(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key,
            new[] { "token", "password", "credential", "authorization" }.Any(s => p.Key.Contains(s, StringComparison.OrdinalIgnoreCase))
                ? (JsonNode?)JsonValue.Create("[redacted]") : Redact(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Redact).ToArray()),
        _ => node?.DeepClone()
    };
    public void Dispose() => writer.Dispose();
}

public sealed record ReplayDecision(string MatchId, int Tick, JsonObject Command);
public static class Replay
{
    /// <summary>Stream decisions against recorded observations; commands do not re-simulate physics.</summary>
    public static IEnumerable<ReplayDecision> Run(Bot bot, string path)
    {
        if (Interlocked.CompareExchange(ref bot.Running, 1, 0) != 0) throw new InvalidOperationException("Cannot replay into an active bot.");
        try
        {
            bot.Welcome = null; bot.LastTick = 0; bot.MatchId = ""; bot.Diagnostics = new();
            var session = new Session(bot);
            using var source = File.OpenText(path);
            var header = JsonNode.Parse(source.ReadLine() ?? "null");
            if (!JsonNode.DeepEquals(header, JsonNode.Parse("{\"format\":\"naval-sdk-bot-views\",\"version\":1}")))
                throw new FormatException("Unsupported bot-view recording.");
            while (source.ReadLine() is { } line)
            {
                foreach (var command in session.Handle(Wire.Object(JsonNode.Parse(line))))
                    if (Wire.String(command["type"]) == "command") yield return new(Wire.String(command["match_id"]), Wire.Int(command["tick"]), command);
                if (session.Stop || session.Fatal) break;
            }
        }
        finally { bot.Phase = "disconnected"; Interlocked.Exchange(ref bot.Running, 0); }
    }
}
