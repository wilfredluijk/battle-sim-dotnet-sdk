namespace Naval.Sdk.Tactical;

public sealed class Helm
{
    private readonly ShipSpecs specs;
    private readonly PowerupConfig powerups;
    private readonly double width, height, margin, aggression, alignment, minThrottle;
    public Helm(ShipSpecs specs, double mapWidth = 700, double mapHeight = 700, double wallMargin = 30,
        double turnAggressionDeg = 30, double alignThresholdDeg = 10, double minTurnThrottle = 0.55, PowerupConfig? powerups = null)
    {
        specs.Validate();
        this.specs = specs; this.powerups = powerups ?? new(); this.powerups.Validate();
        width = mapWidth; height = mapHeight; margin = wallMargin; aggression = turnAggressionDeg;
        alignment = alignThresholdDeg; minThrottle = minTurnThrottle;
    }
    public (double Throttle, double Rudder) SteerToBearing(SelfState me, double targetBearingDeg, bool respectWalls = true, double desiredThrottle = 1)
    {
        var bearing = respectWalls ? WallOverride(me, targetBearingDeg) : targetBearingDeg;
        var delta = Helpers.SignedBearingDelta(bearing, me.HeadingDeg);
        var rudder = Math.Clamp(delta / aggression, -1, 1);
        var throttle = desiredThrottle;
        if (Math.Abs(delta) > alignment)
        {
            var scale = Math.Clamp((180 - Math.Abs(delta)) / Math.Max(180 - alignment, 1e-6), 0, 1);
            throttle = minThrottle + (desiredThrottle - minThrottle) * scale;
        }
        return (throttle, rudder);
    }
    public (double Throttle, double Rudder) SteerToPoint(SelfState me, Vec2 target, bool respectWalls = true, double desiredThrottle = 1) =>
        SteerToBearing(me, Helpers.BearingTo(me.Pos, target), respectWalls, desiredThrottle);

    private double WallOverride(SelfState me, double target)
    {
        var overdrive = me.PowerupActive("overdrive");
        var acceleration = specs.Acceleration * (overdrive ? powerups.OverdriveAccelMult : 1);
        var turnRate = specs.TurnRateDegPerS * (overdrive ? powerups.OverdriveTurnMult : 1);
        var maxSpeed = specs.MaxForwardSpeed * (overdrive ? powerups.OverdriveSpeedMult : 1);
        var yaw = turnRate * Math.Abs(me.Speed) / maxSpeed;
        var horizon = Math.Max(Math.Abs(me.Speed) / acceleration, Math.Min(90 / Math.Max(yaw, 1e-6), 3));
        var heading = me.HeadingDeg * Math.PI / 180;
        var future = me.Pos + new Vec2(Math.Sin(heading), -Math.Cos(heading)) * (me.Speed * horizon);
        var x = Math.Min(me.Pos.X, future.X) < margin ? 1 : Math.Max(me.Pos.X, future.X) > width - margin ? -1 : 0;
        var y = Math.Min(me.Pos.Y, future.Y) < margin ? 1 : Math.Max(me.Pos.Y, future.Y) > height - margin ? -1 : 0;
        if (x == 0 && y == 0) return target;
        var push = Helpers.BearingTo(default, new(x, y));
        return Math.Abs(Helpers.SignedBearingDelta(target, push)) <= 90 ? target : push;
    }
}
