namespace Naval.Sdk;

/// <summary>A position or velocity in world coordinates (+x east, +y south).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double scale) => new(a.X * scale, a.Y * scale);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public static class Helpers
{
    public static double Distance(Vec2 a, Vec2 b) => (b - a).Length;
    public static double WrapBearing(double degrees) => ((degrees % 360) + 360) % 360;
    /// <summary>Shortest clockwise turn, in [-180, 180), matching the Python implementation.</summary>
    public static double SignedBearingDelta(double target, double current) => WrapBearing(target - current + 180) - 180;
    public static double Clamp(double value, double low, double high) => Math.Clamp(value, low, high);
    public static double BearingTo(Vec2 from, Vec2 to) => WrapBearing(Math.Atan2(to.X - from.X, from.Y - to.Y) * 180 / Math.PI);

    /// <summary>Find an intercept for a constant-velocity target; null means no reachable intercept.</summary>
    public static Vec2? LeadTarget(Vec2 shooterPos, Vec2 targetPos, Vec2 targetVel, double shellSpeed)
    {
        if (!double.IsFinite(shellSpeed) || shellSpeed <= 0) return null;
        var r = targetPos - shooterPos;
        var a = targetVel.X * targetVel.X + targetVel.Y * targetVel.Y - shellSpeed * shellSpeed;
        var b = 2 * (r.X * targetVel.X + r.Y * targetVel.Y);
        var c = r.X * r.X + r.Y * r.Y;
        double t;
        if (Math.Abs(a) < 1e-9)
        {
            if (Math.Abs(b) < 1e-9) { if (c >= 1e-9) return null; t = 0; }
            else { t = -c / b; if (t < 0) return null; }
        }
        else
        {
            var discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return null;
            var root = Math.Sqrt(discriminant);
            var t1 = (-b - root) / (2 * a);
            var t2 = (-b + root) / (2 * a);
            t = Math.Min(t1 >= 0 ? t1 : double.PositiveInfinity, t2 >= 0 ? t2 : double.PositiveInfinity);
        }
        return double.IsFinite(t) ? targetPos + targetVel * t : null;
    }
}
