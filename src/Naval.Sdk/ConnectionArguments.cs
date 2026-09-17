using System.Text;

namespace Naval.Sdk;

/// <summary>Connection flags and participant files. File contents are parsed as data, never executed.</summary>
public sealed record ConnectionArguments(string? Url = null, string? Host = null, int? Port = null, string? EnvFile = null)
{
    public static ConnectionArguments Parse(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        var values = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            var pair = args[i].Split('=', 2);
            if (pair[0] is not ("--url" or "--host" or "--port" or "--env-file")) throw new ArgumentException("Unknown connection option.");
            var value = pair.Length == 2 ? pair[1] : ++i < args.Length ? args[i] : throw new ArgumentException("Connection option requires a value.");
            if (!values.TryAdd(pair[0], value)) throw new ArgumentException("Duplicate connection option.");
        }
        int? port = null;
        if (values.TryGetValue("--port", out var portText))
        {
            if (!int.TryParse(portText, out var number) || number is < 1 or > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
            port = number;
        }
        return new(values.GetValueOrDefault("--url"), values.GetValueOrDefault("--host"), port, values.GetValueOrDefault("--env-file"));
    }

    public RunOptions ToRunOptions(string name = "bot", string defaultUrl = "ws://localhost:7878/bot")
    {
        if (Url is not null && (Host is not null || Port is not null)) throw new ArgumentException("Use --url or --host/--port, not both.");
        var settings = EnvFile is not null ? ReadParticipantEnvironment(EnvFile) : new Dictionary<string, string>
        {
            ["BATTLE_SERVER_URL"] = Environment.GetEnvironmentVariable("BATTLE_SERVER_URL") ?? "",
            ["BATTLE_BOT_TOKEN"] = Environment.GetEnvironmentVariable("BATTLE_BOT_TOKEN") ?? ""
        };
        var url = Url ?? (settings.GetValueOrDefault("BATTLE_SERVER_URL") is { Length: > 0 } endpoint ? endpoint : defaultUrl);
        if (Host is not null || Port is not null)
        {
            var host = Host ?? "localhost";
            if (host.Contains(':') && !host.StartsWith('[')) host = $"[{host}]";
            url = $"ws://{host}:{Port ?? 7878}/bot";
        }
        var uri = RunOptions.ValidateUrl(url, requireBotPath: true);
        var token = settings.GetValueOrDefault("BATTLE_BOT_TOKEN") ?? "";
        if (uri.Scheme == "wss" && string.IsNullOrWhiteSpace(token)) throw new ArgumentException("A participant credential is required: use --env-file or BATTLE_BOT_TOKEN.");
        return new() { Url = url, Token = token, Name = name };
    }

    public static IReadOnlyDictionary<string, string> ReadParticipantEnvironment(string path)
    {
        byte[] bytes;
        int count;
        try
        {
            using var stream = File.OpenRead(path);
            bytes = new byte[16385];
            count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new ArgumentException("Could not read the participant --env-file."); }
        if (count > 16384) throw new ArgumentException("Participant --env-file must be at most 16 KiB.");
        string content;
        try { content = new UTF8Encoding(false, true).GetString(bytes, 0, count); }
        catch (DecoderFallbackException) { throw new ArgumentException("Participant --env-file must use UTF-8."); }
        var values = new Dictionary<string, string>();
        using var reader = new StringReader(content);
        var number = 0;
        while (reader.ReadLine() is { } line)
        {
            number++;
            var fields = SplitLine(line, number);
            if (fields.Count > 0 && fields[0] == "export") fields.RemoveAt(0);
            if (fields.Count == 0) continue;
            var assignment = fields[0].Split('=', 2);
            if (fields.Count != 1 || assignment.Length != 2) throw new ArgumentException($"Expected KEY=value in --env-file line {number}.");
            if (assignment[0] is not ("BATTLE_SERVER_URL" or "BATTLE_BOT_TOKEN")) throw new ArgumentException($"Unknown setting in --env-file line {number}.");
            if (!values.TryAdd(assignment[0], assignment[1])) throw new ArgumentException($"Duplicate setting in --env-file line {number}.");
        }
        if (new[] { "BATTLE_SERVER_URL", "BATTLE_BOT_TOKEN" }.Any(k => !values.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)))
            throw new ArgumentException("--env-file must define BATTLE_SERVER_URL and BATTLE_BOT_TOKEN.");
        return values;
    }

    private static List<string> SplitLine(string line, int number)
    {
        var fields = new List<string>();
        var word = new StringBuilder();
        var quote = '\0';
        var started = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote == '\0' && c == '#') break;
            if (quote == '\0' && char.IsWhiteSpace(c))
            {
                if (started) { fields.Add(word.ToString()); word.Clear(); started = false; }
                continue;
            }
            started = true;
            if (c == quote) { quote = '\0'; continue; }
            if (quote == '\0' && c is '\'' or '"') { quote = c; continue; }
            if (c == '\\' && quote != '\'')
            {
                if (++i == line.Length) throw new ArgumentException($"Invalid quoting in --env-file line {number}.");
                if (quote == '"' && line[i] is not ('"' or '\\')) word.Append('\\');
                word.Append(line[i]);
            }
            else word.Append(c);
        }
        if (quote != '\0') throw new ArgumentException($"Invalid quoting in --env-file line {number}.");
        if (started) fields.Add(word.ToString());
        return fields;
    }
}
