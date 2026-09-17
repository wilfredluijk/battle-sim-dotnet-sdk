namespace Naval.Sdk.Tactical;

public interface ISensorPolicy
{
    SensorMode Choose(WorldView view, Tracker tracker);
    void Reset() { }
}
public sealed class AlwaysActive : ISensorPolicy
{
    public SensorMode Choose(WorldView view, Tracker tracker) => SensorMode.Active;
}
public sealed class AlwaysPassive : ISensorPolicy
{
    public SensorMode Choose(WorldView view, Tracker tracker) => SensorMode.Passive;
}
public sealed class DutyCycle(int activeTicks = 10, int passiveTicks = 20) : ISensorPolicy
{
    public SensorMode Choose(WorldView view, Tracker tracker) =>
        view.Tick % Math.Max(1, activeTicks + passiveTicks) < activeTicks ? SensorMode.Active : SensorMode.Passive;
}
public sealed class PingWhenStale(int staleThresholdTicks = 4) : ISensorPolicy
{
    public SensorMode Choose(WorldView view, Tracker tracker)
    {
        var tracks = tracker.Tracks.Where(t => t.Kind == "ship").ToArray();
        return tracks.Length == 0 || tracks.Max(t => view.Tick - t.LastActiveTick) >= staleThresholdTicks ? SensorMode.Active : SensorMode.Passive;
    }
}

public enum EvaderState { Idle, Evading, Cooldown }
public class Evader
{
    private readonly int evasionTicks, cooldownTicks;
    private readonly double throttle, initialSign;
    private double rudderSign;
    private int stateUntil;
    public EvaderState State { get; private set; }
    public Evader(int evasionTicks = 15, int cooldownTicks = 10, double throttle = 1, double initialRudderSign = 1)
    {
        this.evasionTicks = evasionTicks; this.cooldownTicks = cooldownTicks; this.throttle = throttle;
        initialSign = initialRudderSign >= 0 ? 1 : -1; rudderSign = initialSign;
    }
    public virtual void Reset() { State = EvaderState.Idle; stateUntil = 0; rudderSign = initialSign; }
    public virtual Command? Update(WorldView view)
    {
        if (State != EvaderState.Idle && view.Tick >= stateUntil)
        {
            if (State == EvaderState.Evading) { State = EvaderState.Cooldown; stateUntil = view.Tick + cooldownTicks; }
            else State = EvaderState.Idle;
        }
        if (view.Events.Any(e => e is HitEvent) && State is EvaderState.Idle or EvaderState.Cooldown)
        {
            if (State == EvaderState.Cooldown) rudderSign = -rudderSign;
            State = EvaderState.Evading; stateUntil = view.Tick + evasionTicks;
        }
        return State == EvaderState.Evading ? new Command { Throttle = throttle, Rudder = rudderSign } : null;
    }
}
