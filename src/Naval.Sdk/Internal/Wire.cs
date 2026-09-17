using System.Text.Json;
using System.Text.Json.Nodes;

namespace Naval.Sdk.Internal;

internal static class Wire
{
    internal static JsonObject Object(JsonNode? node) => node as JsonObject ?? throw new FormatException("Expected a JSON object.");
    internal static JsonArray Array(JsonNode? node) => node as JsonArray ?? throw new FormatException("Expected a JSON array.");
    internal static string String(JsonNode? node) => node?.GetValue<string>() ?? throw new FormatException("Expected a string.");
    internal static string String(JsonObject o, string key, string fallback) => o[key] is { } n ? String(n) : fallback;
    internal static double Finite(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue) throw new ArgumentException("Number must fit the server's finite f32 range.");
        return value;
    }
    internal static double Number(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var d)) return Finite(d);
            if (value.TryGetValue<int>(out var i)) return i;
            if (value.TryGetValue<long>(out var l)) return Finite(l);
            if (value.TryGetValue<float>(out var f)) return Finite(f);
            if (value.TryGetValue<decimal>(out var m)) return Finite((double)m);
        }
        throw new FormatException("Expected a number.");
    }
    internal static double Number(JsonObject o, string key, double fallback) => o[key] is { } n ? Number(n) : fallback;
    internal static int Int(JsonNode? node)
    {
        var n = Number(node);
        if (n != Math.Truncate(n) || n < int.MinValue || n > int.MaxValue) throw new FormatException("Expected a 32-bit integer.");
        return (int)n;
    }
    internal static int Int(JsonObject o, string key, int fallback) => o[key] is { } n ? Int(n) : fallback;
    internal static bool Bool(JsonObject o, string key, bool fallback = false) => o[key]?.GetValue<bool>() ?? fallback;
    internal static double Positive(double n) => Finite(n) > 0 ? n : throw new ArgumentException("Number must be positive.");
    internal static double Nonnegative(double n) => Finite(n) >= 0 ? n : throw new ArgumentException("Number must be nonnegative.");
    internal static Vec2 Point(JsonNode? node)
    {
        var a = Array(node);
        if (a.Count != 2) throw new FormatException("Expected two coordinates.");
        return new(Number(a[0]), Number(a[1]));
    }
    internal static JsonArray Items(JsonObject o, string key) => o[key] is { } n ? Array(n) : [];
    internal static IReadOnlyList<string> Strings(JsonObject o, string key) => System.Array.AsReadOnly(Items(o, key).Select(String).ToArray());
    internal static JsonObject Copy(JsonObject o) => (JsonObject)o.DeepClone();
    internal static bool IsMalformed(Exception e) => e is JsonException or FormatException or ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException or IndexOutOfRangeException;
    internal static void CheckProtocol(string version)
    {
        var parts = version.Split('.');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p.Any(c => c < '0' || c > '9')) || !int.TryParse(parts[0], out var major) || major != 3)
            throw new ProtocolMismatchException("This SDK supports server protocol 3.x.");
    }
}
