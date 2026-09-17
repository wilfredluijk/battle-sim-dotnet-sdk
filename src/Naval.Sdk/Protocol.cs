using System.Text.Json.Nodes;
using Naval.Sdk.Internal;

namespace Naval.Sdk;

public sealed record ShipSpecs(double MaxForwardSpeed, double MaxReverseSpeed, double Acceleration,
    double TurnRateDegPerS, int HullHp, int MaxAmmo, int GunCooldownTicks, double HitRadius,
    double ShellSpeed, double MaxShellRange, double SplashRadius, int MaxSplashDamage)
{
    public void Validate()
    {
        foreach (var n in new double[] { MaxForwardSpeed, MaxReverseSpeed, Acceleration, TurnRateDegPerS,
            HullHp, MaxAmmo, GunCooldownTicks, HitRadius, ShellSpeed, MaxShellRange, SplashRadius, MaxSplashDamage }) Wire.Positive(n);
    }
    public static ShipSpecs FromJson(JsonObject d)
    {
        var result = new ShipSpecs(Wire.Number(d["max_forward_speed"]), Wire.Number(d["max_reverse_speed"]),
            Wire.Number(d["acceleration"]), Wire.Number(d["turn_rate_deg_per_s"]), Wire.Int(d["hull_hp"]),
            Wire.Int(d["max_ammo"]), Wire.Int(d["gun_cooldown_ticks"]), Wire.Number(d["hit_radius"]),
            Wire.Number(d["shell_speed"]), Wire.Number(d["max_shell_range"]), Wire.Number(d["splash_radius"]), Wire.Int(d["max_splash_damage"]));
        result.Validate();
        return result;
    }
}

public sealed record MapInfo(int Width, int Height)
{
    public static MapInfo FromJson(JsonObject d)
    {
        var result = new MapInfo(Wire.Int(d["width"]), Wire.Int(d["height"]));
        Wire.Positive(result.Width); Wire.Positive(result.Height);
        return result;
    }
}

public sealed record MatchConfiguration(string ProtocolVersion, int Revision, double SimulationDt, int TickHz,
    int DeadlineMs, MapInfo Map, ShipSpecs ShipSpecs, SensorConfig Sensors, PowerupConfig Powerups,
    IReadOnlyList<string> AvailablePowerups, int MatchTimeoutTicks, int WallBumpDamage)
{
    private JsonObject raw = new();
    public JsonObject Raw => Wire.Copy(raw);
    public bool SupportsEmptyLoadout => raw["capabilities"]?["empty_loadout"]?.GetValue<bool>() == true;
    public bool SupportsOwnShipTelemetry => raw["capabilities"]?["own_ship_telemetry"]?.GetValue<bool>() == true;

    public static MatchConfiguration FromJson(JsonObject d)
    {
        var sim = Wire.Object(d["sim_config"]);
        if (d["capabilities"] is { } node)
        {
            var capabilities = Wire.Object(node);
            foreach (var key in new[] { "empty_loadout", "own_ship_telemetry" })
                if (capabilities.ContainsKey(key)) _ = capabilities[key]?.GetValue<bool>() ?? throw new FormatException("Expected a capability flag.");
        }
        var catalog = Array.AsReadOnly(Wire.Array(d["available_powerups"]).Select(Wire.String).ToArray());
        if (catalog.Any(string.IsNullOrEmpty)) throw new FormatException("Powerup IDs must be nonempty strings.");
        var result = new MatchConfiguration(Wire.String(d["protocol_version"]), Wire.Int(d, "revision", 1),
            Wire.Number(d["simulation_dt"]), Wire.Int(d["tick_hz"]), Wire.Int(d["deadline_ms"]),
            MapInfo.FromJson(Wire.Object(d["map"])), ShipSpecs.FromJson(Wire.Object(d["ship_specs"])),
            SensorConfig.FromJson(sim), PowerupConfig.FromJson(sim["powerups"] is { } p ? Wire.Object(p) : new()),
            catalog, Wire.Int(d, "match_timeout_ticks", 3000), Wire.Int(sim, "wall_bump_damage", 2)) { raw = Wire.Copy(d) };
        Wire.Nonnegative(result.Revision); Wire.Positive(result.SimulationDt); Wire.Positive(result.TickHz);
        Wire.Positive(result.DeadlineMs); Wire.Positive(result.MatchTimeoutTicks); Wire.Nonnegative(result.WallBumpDamage);
        return result;
    }
}

public sealed record Welcome(string BotId, string ShipId, MapInfo Map, int TickHz, ShipSpecs ShipSpecs)
{
    public IReadOnlyList<string> AvailablePowerups { get; init; } = [];
    public double SimulationDt { get; init; } = 0.1;
    public string ProtocolVersion { get; init; } = "1.0";
    public string ConfigHash { get; init; } = "";
    private JsonObject configuration = new();
    public JsonObject Configuration => Wire.Copy(configuration);
    public MatchConfiguration? Rules { get; private init; }

    public Welcome WithConfiguration(JsonObject data, string configHash)
    {
        if (string.IsNullOrEmpty(configHash)) throw new ArgumentException("Configuration hash is required.");
        var rules = MatchConfiguration.FromJson(data);
        return this with { configuration = Wire.Copy(data), ConfigHash = configHash, Rules = rules,
            ShipSpecs = rules.ShipSpecs, Map = rules.Map, TickHz = rules.TickHz, SimulationDt = rules.SimulationDt,
            ProtocolVersion = rules.ProtocolVersion, AvailablePowerups = rules.AvailablePowerups };
    }

    public static Welcome FromJson(JsonObject d)
    {
        var data = d["configuration"] is { } c ? Wire.Object(c) : new();
        return new(Wire.String(d["bot_id"]), Wire.String(d["ship_id"]), MapInfo.FromJson(Wire.Object(d["map"])),
            Wire.Int(d["tick_hz"]), ShipSpecs.FromJson(Wire.Object(d["ship_specs"])))
        {
            AvailablePowerups = Wire.Strings(d, "available_powerups"),
            SimulationDt = Wire.Positive(Wire.Number(d, "simulation_dt", 0.1)),
            ProtocolVersion = Wire.String(d, "protocol_version", "1.0"), ConfigHash = Wire.String(d, "config_hash", ""),
            configuration = Wire.Copy(data), Rules = data.Count == 0 ? null : MatchConfiguration.FromJson(data)
        };
    }
}

public sealed record PowerupStatus(string Id, bool Used = false, int ActiveTicksLeft = 0)
{
    public static PowerupStatus FromJson(JsonObject d) => new(Wire.String(d["id"]), Wire.Bool(d, "used"), Wire.Int(d, "active_ticks_left", 0));
}

public sealed record GameStart(int Tick, Vec2 StartingPosition, double StartingHeadingDeg,
    ShipSpecs? ShipSpecs = null, double SimulationDt = 0.1, string MatchId = "")
{
    public static GameStart FromJson(JsonObject d) => new(Wire.Int(d["tick"]), Wire.Point(d["starting_position"]),
        Wire.Number(d["starting_heading_deg"]), d["ship_specs"] is { } s ? ShipSpecs.FromJson(Wire.Object(s)) : null,
        Wire.Positive(Wire.Number(d, "simulation_dt", 0.1)), Wire.String(d, "match_id", ""));
}

public sealed record SelfState(Vec2 Pos, double HeadingDeg, double Speed, int Hp, int Ammo, double Rudder, double Throttle)
{
    public IReadOnlyList<string> SelectedPowerups { get; init; } = [];
    public IReadOnlyList<PowerupStatus> PowerupStatus { get; init; } = [];
    public int? GunCooldownTicksLeft { get; init; }
    public int EmpTicksLeft { get; init; }
    public PowerupStatus? Powerup(string id) => PowerupStatus.FirstOrDefault(p => p.Id == id);
    public bool PowerupReady(string id) => Powerup(id) is { Used: false };
    public bool PowerupActive(string id) => Powerup(id) is { ActiveTicksLeft: > 0 };
    public static SelfState FromJson(JsonObject d) => new(Wire.Point(d["pos"]), Wire.Number(d["heading_deg"]),
        Wire.Number(d["speed"]), Wire.Int(d["hp"]), Wire.Int(d["ammo"]), Wire.Number(d["rudder"]), Wire.Number(d["throttle"]))
    {
        SelectedPowerups = Wire.Strings(d, "selected_powerups"),
        PowerupStatus = Array.AsReadOnly(Wire.Items(d, "powerup_status").Select(p => Naval.Sdk.PowerupStatus.FromJson(Wire.Object(p))).ToArray()),
        GunCooldownTicksLeft = d["gun_cooldown_ticks_left"] is { } cd ? Math.Max(0, Wire.Int(cd)) : null,
        EmpTicksLeft = Math.Max(0, Wire.Int(d, "emp_ticks_left", 0))
    };
}

public sealed record Contact(string Id, string Kind, Vec2 Pos, double BearingDeg, double? Range, double Confidence)
{
    public static Contact FromJson(JsonObject d) => new(Wire.String(d["id"]), Wire.String(d, "kind", "unknown"), Wire.Point(d["pos"]),
        Wire.Number(d["bearing_deg"]), d["range"] is { } r ? Wire.Number(r) : null, Wire.Number(d, "confidence", 0));
}

public abstract record TickEvent
{
    public static TickEvent FromJson(JsonNode? node)
    {
        try
        {
            var d = Wire.Object(node);
            return Wire.String(d, "type", "unknown") switch
            {
                "hit" => new HitEvent(Wire.Int(d["amount"])),
                "shell_splash" => new ShellSplashEvent(Wire.Point(d["pos"])),
                "powerup_activated" => new PowerupActivatedEvent(d["own"]?.GetValue<bool>() ?? throw new FormatException(),
                    d["contact_id"] is { } id ? Wire.String(id) : null, Wire.String(d["powerup"])),
                _ => new UnknownEvent(node?.DeepClone())
            };
        }
        catch (Exception e) when (Wire.IsMalformed(e)) { return new UnknownEvent(node?.DeepClone()); }
    }
}
public sealed record HitEvent(int Amount) : TickEvent;
public sealed record ShellSplashEvent(Vec2 Pos) : TickEvent;
public sealed record PowerupActivatedEvent(bool Own, string? ContactId, string Powerup) : TickEvent;
public sealed record UnknownEvent(JsonNode? Raw) : TickEvent;

public sealed record WorldView(int Tick, int DeadlineMs, SelfState SelfState, IReadOnlyList<Contact> Contacts,
    IReadOnlyList<TickEvent> Events, string MatchId = "")
{
    public SelfState Me => SelfState;
    public Contact? NearestContact() => Contacts.Where(c => c.Range is not null).MinBy(c => c.Range);
    public static WorldView FromJson(JsonObject d) => new(Wire.Int(d["tick"]), Wire.Int(d["deadline_ms"]),
        SelfState.FromJson(Wire.Object(d["self"])),
        Array.AsReadOnly(Wire.Items(d, "contacts").Select(c => Contact.FromJson(Wire.Object(c))).ToArray()),
        Array.AsReadOnly(Wire.Items(d, "events").Select(TickEvent.FromJson).ToArray()), Wire.String(d, "match_id", ""));
}

public sealed record GameOver(string? Winner, int FinalTick, string ReplayId)
{
    public static GameOver FromJson(JsonObject d) => new(d["winner"] is { } w ? Wire.String(w) : null,
        Wire.Int(d["final_tick"]), Wire.String(d["replay_id"]));
}

public enum SensorMode { Active, Passive }
public sealed record FireCommand(double BearingDeg, double Range)
{
    public JsonObject ToJson() => new() { ["bearing_deg"] = Wire.Finite(BearingDeg), ["range"] = Wire.Finite(Range) };
}

public sealed class Command
{
    public double Throttle { get; set; }
    public double Rudder { get; set; }
    public SensorMode SensorMode { get; set; } = SensorMode.Active;
    public FireCommand? Fire { get; set; }
    public string? ActivatePowerup { get; set; }

    public Command FireAt(Vec2 targetPos, Vec2? shooterPos = null, Vec2? targetVel = null,
        double shellSpeed = 70, double? range = null, bool lead = true)
    {
        var origin = shooterPos ?? default;
        var aim = lead && targetVel is { } v && v != default ? Helpers.LeadTarget(origin, targetPos, v, shellSpeed) ?? targetPos : targetPos;
        Fire = new(Helpers.BearingTo(origin, aim), range ?? Helpers.Distance(origin, aim));
        return this;
    }

    public JsonObject ToJson(int tick, string matchId = "")
    {
        if (!Enum.IsDefined(SensorMode)) throw new ArgumentException("Invalid sensor mode.");
        if (ActivatePowerup is "") throw new ArgumentException("Powerup ID must not be empty.");
        var result = new JsonObject { ["type"] = "command", ["tick"] = tick, ["throttle"] = Wire.Finite(Throttle),
            ["rudder"] = Wire.Finite(Rudder), ["sensor_mode"] = SensorMode == SensorMode.Active ? "active" : "passive" };
        if (matchId.Length > 0) result["match_id"] = matchId;
        if (Fire is not null) result["fire"] = Fire.ToJson();
        if (ActivatePowerup is not null) result["activate_powerup"] = ActivatePowerup;
        return result;
    }
}
