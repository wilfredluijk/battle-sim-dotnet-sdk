namespace Naval.Sdk.Tactical;

public sealed class Track
{
    public int TrackId { get; init; }
    public string Kind { get; set; } = "unknown";
    public Vec2 Pos { get; set; }
    public Vec2 ObservedPos { get; set; }
    public Vec2 Vel { get; set; }
    public int LastSeenTick { get; set; }
    public int FirstSeenTick { get; init; }
    public int LastActiveTick { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = "active";
}

/// <summary>Associates changing contact IDs into stable tracks; velocities use simulation seconds.</summary>
public sealed class Tracker
{
    private readonly double dt, activeGate, passiveGate, alpha;
    private readonly int velocityWindow, staleness;
    private readonly Dictionary<int, Track> tracks = new();
    private readonly Dictionary<int, List<(int Tick, Vec2 Pos)>> history = new();
    private int nextId = 1;

    public Tracker(ShipSpecs specs, int tickHz = 10, double simulationDt = 0.1, double activeGate = 60,
        double passiveBearingGateDeg = 20, double velocityAlpha = 0.3, int velocityWindowTicks = 10, int stalenessTicks = 40)
    {
        ArgumentNullException.ThrowIfNull(specs);
        Internal.Wire.Positive(simulationDt);
        dt = simulationDt; this.activeGate = activeGate; passiveGate = passiveBearingGateDeg;
        alpha = velocityAlpha; velocityWindow = Math.Max(2, velocityWindowTicks); staleness = stalenessTicks;
    }
    public IReadOnlyList<Track> Tracks => tracks.Values.OrderBy(t => t.TrackId).ToArray();
    public Track? Get(int trackId) => tracks.GetValueOrDefault(trackId);
    public void Reset() { tracks.Clear(); history.Clear(); nextId = 1; }
    private Vec2 Predict(Track track, int tick) => track.ObservedPos + track.Vel * (Math.Max(0, tick - track.LastActiveTick) * dt);

    public IReadOnlyList<Track> Update(WorldView view)
    {
        var matched = new HashSet<int>();
        foreach (var contact in view.Contacts.Where(c => c.Range is not null))
        {
            Track? best = null;
            var bestDistance = activeGate;
            foreach (var track in tracks.Values)
            {
                if (matched.Contains(track.TrackId)) continue;
                var distance = Helpers.Distance(Predict(track, view.Tick), contact.Pos);
                if (distance < bestDistance) { bestDistance = distance; best = track; }
            }
            if (best is not null) FoldActive(best, contact, view.Tick);
            else
            {
                best = new Track { TrackId = nextId++, Kind = contact.Kind, Pos = contact.Pos, ObservedPos = contact.Pos,
                    LastSeenTick = view.Tick, FirstSeenTick = view.Tick, LastActiveTick = view.Tick, Confidence = contact.Confidence };
                tracks[best.TrackId] = best;
                history[best.TrackId] = [(view.Tick, contact.Pos)];
            }
            matched.Add(best.TrackId);
        }
        foreach (var contact in view.Contacts.Where(c => c.Range is null))
        {
            Track? best = null;
            var bestBearing = passiveGate;
            foreach (var track in tracks.Values)
            {
                if (matched.Contains(track.TrackId)) continue;
                var bearing = Helpers.BearingTo(view.Me.Pos, Predict(track, view.Tick));
                var delta = Math.Abs(Helpers.SignedBearingDelta(contact.BearingDeg, bearing));
                if (delta < bestBearing) { bestBearing = delta; best = track; }
            }
            if (best is null) continue;
            best.LastSeenTick = view.Tick; best.Confidence = contact.Confidence; best.Source = "passive";
            matched.Add(best.TrackId);
        }
        foreach (var track in tracks.Values)
        {
            if (track.LastActiveTick == view.Tick) continue;
            track.Pos = Predict(track, view.Tick);
            if (track.LastSeenTick != view.Tick) track.Source = "dead_reckoned";
        }
        foreach (var id in tracks.Where(p => view.Tick - p.Value.LastSeenTick < 0 || view.Tick - p.Value.LastSeenTick > staleness).Select(p => p.Key).ToArray())
        { tracks.Remove(id); history.Remove(id); }
        return Tracks;
    }

    private void FoldActive(Track track, Contact contact, int tick)
    {
        var observations = history[track.TrackId];
        observations.Add((tick, contact.Pos));
        if (observations.Count > velocityWindow) observations.RemoveAt(0);
        var seconds = (tick - observations[0].Tick) * dt;
        if (observations.Count >= 2 && seconds > 0)
        {
            var velocity = (contact.Pos - observations[0].Pos) * (1 / seconds);
            track.Vel = track.Vel == default ? velocity : velocity * alpha + track.Vel * (1 - alpha);
        }
        track.ObservedPos = contact.Pos; track.Pos = contact.Pos; track.LastSeenTick = tick; track.LastActiveTick = tick;
        track.Confidence = contact.Confidence; track.Source = "active";
        if (track.Kind == "unknown") track.Kind = contact.Kind;
    }
}
