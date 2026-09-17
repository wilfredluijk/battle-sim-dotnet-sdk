namespace Naval.Sdk.Tactical;

public sealed record FireSolution(double BearingDeg, double Range, Vec2 AimPos, int TargetId);
public sealed class Gunner
{
    private readonly ShipSpecs specs;
    private readonly PowerupConfig powerups;
    private readonly double dt, splashMargin;
    private readonly int maxAge;
    private readonly bool requireActive;
    private (int Tick, int Ammo)? pending;
    public int NextFireTick { get; private set; }

    public Gunner(ShipSpecs specs, double selfSplashMargin = 1.5, int maxActiveAgeTicks = 5,
        bool requireRecentActive = true, PowerupConfig? powerups = null, double simulationDt = 0.1)
    {
        specs.Validate();
        Internal.Wire.Positive(simulationDt);
        if (!double.IsFinite(selfSplashMargin) || selfSplashMargin < 1) throw new ArgumentException("Splash margin must be finite and at least one.");
        this.specs = specs; this.powerups = powerups ?? new(); this.powerups.Validate();
        dt = simulationDt; splashMargin = selfSplashMargin; maxAge = maxActiveAgeTicks; requireActive = requireRecentActive;
    }

    public (double Speed, double Range, double SplashRadius, int Cooldown) EffectiveWeapons(SelfState me, string? activatePowerup = null)
    {
        bool Active(string id) => me.PowerupActive(id) || (activatePowerup == id && me.PowerupReady(id));
        var speed = specs.ShellSpeed * (Active("long_range_salvo") ? powerups.LongRangeSpeedMult : 1);
        var range = specs.MaxShellRange * (Active("long_range_salvo") ? powerups.LongRangeRangeMult : 1);
        var splash = specs.SplashRadius * (Active("heavy_shell") ? powerups.HeavyShellSplashMult : 1);
        var cooldown = specs.GunCooldownTicks * (Active("rapid_fire") ? powerups.RapidFireCooldownMult : 1);
        if (me.EmpTicksLeft > 0) cooldown *= powerups.EmpGunCooldownMult;
        return (speed, range, splash, Math.Max(1, (int)Math.Floor(cooldown + 0.5)));
    }

    public void Update(WorldView view)
    {
        if (view.Me.GunCooldownTicksLeft is { } cooldown) { NextFireTick = view.Tick + cooldown; pending = null; }
        else if (pending is { } p && view.Tick > p.Tick)
        {
            if (view.Me.Ammo >= p.Ammo) NextFireTick = view.Tick;
            pending = null;
        }
    }
    public bool CanFire(WorldView view, SelfState me) =>
        (me.GunCooldownTicksLeft is { } cd ? cd == 0 : view.Tick >= NextFireTick) && me.Ammo > 0;

    /// <summary>Compute a shot without changing cooldown state. Call NoteFired only if it is sent.</summary>
    public FireSolution? Solve(SelfState me, Track track, WorldView view, string? activatePowerup = null)
    {
        if (!CanFire(view, me) || (requireActive && view.Tick - track.LastActiveTick > maxAge)) return null;
        var weapons = EffectiveWeapons(me, activatePowerup);
        if (Helpers.LeadTarget(me.Pos, track.Pos, track.Vel, weapons.Speed) is not { } aim) return null;
        var range = Helpers.Distance(me.Pos, aim);
        if (range > weapons.Range) return null;
        var flight = Math.Max(1, Math.Ceiling(range / (weapons.Speed * dt))) * dt;
        var heading = me.HeadingDeg * Math.PI / 180;
        var futureMe = me.Pos + new Vec2(Math.Sin(heading), -Math.Cos(heading)) * (me.Speed * flight);
        if (Math.Min(range, Helpers.Distance(futureMe, aim)) < specs.HitRadius + weapons.SplashRadius * splashMargin) return null;
        return new(Helpers.BearingTo(me.Pos, aim), range, aim, track.TrackId);
    }
    public bool Attempt(Command command, SelfState me, Track track, WorldView view)
    {
        Update(view);
        var solution = Solve(me, track, view, command.ActivatePowerup);
        if (solution is null) return false;
        command.Fire = ToFireCommand(solution);
        NoteFired(view.Tick, EffectiveWeapons(me, command.ActivatePowerup).Cooldown, me.Ammo);
        return true;
    }
    public static FireCommand ToFireCommand(FireSolution solution) => new(solution.BearingDeg, solution.Range);
    public void NoteFired(int tick, int? cooldownTicks = null, int? ammo = null)
    {
        NextFireTick = tick + (cooldownTicks ?? specs.GunCooldownTicks);
        pending = ammo is { } count ? (tick, count) : null;
    }
    public void Reset() { NextFireTick = 0; pending = null; }
}
