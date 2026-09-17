using System.Collections;

namespace Naval.Sdk.Tactical;

public enum IntentKind { Engage, Patrol, RetreatTo, Hold, Custom }
public readonly record struct PatrolRect(double X1, double Y1, double X2, double Y2);
public sealed record Intent(IntentKind Kind, Track? Target = null, PatrolRect? Rect = null, Vec2? Point = null, Command? Command = null)
{
    public static Intent Engage(Track target) => new(IntentKind.Engage, Target: target);
    public static Intent Patrol(PatrolRect rect) => new(IntentKind.Patrol, Rect: rect);
    public static Intent RetreatTo(Vec2 point) => new(IntentKind.RetreatTo, Point: point);
    public static Intent Hold() => new(IntentKind.Hold);
    public static Intent Custom(Command command) => new(IntentKind.Custom, Command: command);
}
public sealed class ThreatList(IReadOnlyList<Track> tracks, Vec2 mePos) : IReadOnlyCollection<Track>
{
    public IReadOnlyList<Track> Tracks { get; } = tracks;
    public int Count => Tracks.Count;
    public Track? Nearest() => Tracks.MinBy(t => Helpers.Distance(mePos, t.Pos));
    public Track? Farthest() => Tracks.MaxBy(t => Helpers.Distance(mePos, t.Pos));
    public Track? ById(int trackId) => Tracks.FirstOrDefault(t => t.TrackId == trackId);
    public IEnumerator<Track> GetEnumerator() => Tracks.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
public sealed record TacticalContext(WorldView View, SelfState Me, ShipSpecs Specs, Tracker Tracker,
    ThreatList Threats, double MapWidth, double MapHeight);

/// <summary>Combines evasion, intent, navigation, sensor policy and fire control.</summary>
public class TacticalBot : Bot
{
    public Tracker? Tracker { get; protected set; }
    public Gunner? Gunner { get; protected set; }
    public Helm? Helm { get; protected set; }
    public Evader? Evader { get; protected set; }
    public ISensorPolicy SensorPolicy { get; set; } = new AlwaysActive();
    private int patrolCorner;
    public virtual Intent Decide(TacticalContext context) => Intent.Hold();
    public virtual void OnTacticalWelcome(Welcome welcome) { }
    public override void OnWelcome(Welcome welcome)
    {
        Welcome = welcome;
        Tracker = new(welcome.ShipSpecs, welcome.TickHz, welcome.SimulationDt);
        Gunner = new(welcome.ShipSpecs, powerups: welcome.Rules?.Powerups, simulationDt: welcome.SimulationDt);
        Helm = new(welcome.ShipSpecs, welcome.Map.Width, welcome.Map.Height, powerups: welcome.Rules?.Powerups);
        Evader ??= new();
        OnTacticalWelcome(welcome);
    }
    public override void OnGameStart(int tick, Vec2 startingPosition, double startingHeadingDeg)
    {
        Tracker?.Reset(); Gunner?.Reset(); Evader?.Reset(); SensorPolicy.Reset(); patrolCorner = 0;
    }
    public override Command OnTick(WorldView view)
    {
        if (Tracker is null || Gunner is null || Helm is null || Evader is null || Welcome is null) return new();
        var tracks = Tracker.Update(view);
        Gunner.Update(view);
        var threats = new ThreatList(tracks.Where(t => t.Kind == "ship").ToArray(), view.Me.Pos);
        var context = new TacticalContext(view, view.Me, Welcome.ShipSpecs, Tracker, threats, Welcome.Map.Width, Welcome.Map.Height);
        if (Evader.Update(view) is { } evasion)
        {
            evasion.SensorMode = SensorPolicy.Choose(view, Tracker);
            return evasion;
        }
        var intent = Decide(context);
        if (intent.Kind == IntentKind.Custom) return intent.Command ?? new();
        var command = ToCommand(intent, context);
        command.SensorMode = SensorPolicy.Choose(view, Tracker);
        var target = intent.Kind == IntentKind.Engage && intent.Target is not null ? intent.Target : intent.Kind == IntentKind.Hold ? null : threats.Nearest();
        if (target is not null) Gunner.Attempt(command, view.Me, target, view);
        return command;
    }
    private Command ToCommand(Intent intent, TacticalContext context)
    {
        Vec2? point = intent.Kind switch
        {
            IntentKind.Engage => intent.Target?.Pos,
            IntentKind.RetreatTo => intent.Point,
            IntentKind.Patrol when intent.Rect is { } rect => PatrolWaypoint(rect, context.Me.Pos),
            _ => null
        };
        if (point is null) return new();
        var (throttle, rudder) = Helm!.SteerToPoint(context.Me, point.Value);
        return new() { Throttle = throttle, Rudder = rudder };
    }
    private Vec2 PatrolWaypoint(PatrolRect rect, Vec2 position)
    {
        Vec2[] corners = [new(rect.X1, rect.Y1), new(rect.X2, rect.Y1), new(rect.X2, rect.Y2), new(rect.X1, rect.Y2)];
        if (Helpers.Distance(position, corners[patrolCorner]) < 25) patrolCorner = (patrolCorner + 1) % 4;
        return corners[patrolCorner];
    }
}
