# Tactical toolkit

All tactical classes live in `Naval.Sdk.Tactical`. `TacticalBot` wires them
together, while each component is independently usable with a low-level `Bot`.

```csharp
using Naval.Sdk;
using Naval.Sdk.Tactical;

sealed class Hunter : TacticalBot
{
    public Hunter() => SensorPolicy = new PingWhenStale();
    public override Intent Decide(TacticalContext context) =>
        context.Threats.Nearest() is { } target
            ? Intent.Engage(target)
            : Intent.Patrol(new(80, 80, context.MapWidth - 80, context.MapHeight - 80));
}
```

## Orchestration

Each tick updates tracker and gunner state, builds a context, then checks the
evader. An evasive command preempts intent and gunfire, while retaining the sensor
policy. Otherwise `Decide` returns an intent. `Custom` supplies a command directly
and bypasses steering, sensor, and gunner overlays. Other intents steer through
the helm, apply sensors, and optionally fire.

| Intent | Behavior |
| --- | --- |
| `Engage(track)` | Steer toward and fire at that track. |
| `Patrol(PatrolRect)` | Visit rectangle corners, advance within 25 units, fire at the nearest threat. |
| `RetreatTo(Vec2)` | Navigate to a point and fire at the nearest threat. |
| `Hold()` | Zero throttle/rudder; no gunner shot. |
| `Custom(Command)` | Use the supplied command directly unless evasion already preempted. |

`TacticalContext` exposes `View`, `Me`, `Specs`, `Tracker`, `Threats`, `MapWidth`,
and `MapHeight`. `ThreatList` is an iterable ship-track collection with `Count`,
`Nearest()`, `Farthest()`, and `ById()`.

Override `OnTacticalWelcome` to customize initialized subsystems. Every match
start resets tracker, gunner, evader, patrol cursor, and sensor policy. If you
override `OnGameStart` or `OnGameStartEvent`, call the base implementation to retain
these resets. Configuration changes rebuild tracker, gunner, and helm.

## Tracker

`Tracker(specs, tickHz = 10, simulationDt = .1, activeGate = 60,
passiveBearingGateDeg = 20, velocityAlpha = .3, velocityWindowTicks = 10,
stalenessTicks = 40)` associates active contacts greedily by predicted distance.
Per-tick contact IDs are unstable; `TrackId` is locally stable. Only active
(ranged) contacts create tracks. Passive contacts associate by bearing and cannot
refresh an active position fix. Unobserved tracks are dead-reckoned; old or
backward-tick tracks are pruned before association, so they cannot be revived by a
new observation. Known contact kinds must match: a shell cannot update a ship
track, including through a passive observation. Unknown contacts may associate
with a known kind; an unknown track can become a known kind after an active fix.

Call `Update(view)` once per tick; use `Tracks`, `Get(id)`, and `Reset()`. Tracks
expose `Pos`, `ObservedPos`, `Vel` (units/second), first/last seen ticks, last active
tick, confidence, kind, and source (`active`, `passive`, `dead_reckoned`). Velocity
uses a rolling observation baseline and exponential smoothing. Association is
heuristic and can confuse nearby targets, as in the original Python algorithm.

## Gunner

`Gunner(specs, selfSplashMargin = 1.5, maxActiveAgeTicks = 5,
requireRecentActive = true, powerups = null, simulationDt = .1)` tracks firing
attempts and reconciles authoritative cooldowns or unchanged ammunition after a
rejected shot.

`Solve(me, track, view, activatePowerup = null)` is side-effect-free and returns
`FireSolution?`. It checks ammo, cooldown, active-fix age, intercept feasibility,
maximum range, and distance from both current and predicted own position to the
splash. Prediction quantizes flight time to simulation ticks. The splash margin
scales splash radius beyond the hull's hit radius.

`Attempt(command, me, track, view)` updates state, solves, attaches `Fire`, and
records the attempted shot. For manual control, use `ToFireCommand(solution)` and
`NoteFired(tick, cooldownTicks = null, ammo = null)`. `Update`, `CanFire`,
`NextFireTick`, and `Reset` expose state management.

`EffectiveWeapons(me, activatePowerup)` returns speed, range, splash radius, and
cooldown, incorporating active effects or a ready effect activated in that command.
Rapid fire uses positive-half-up rounding to match Rust (15 × .5 becomes 8).
Long-range salvo, heavy shell, rapid fire, and EMP use acknowledged tuning.

## Helm and evader

`Helm(specs, mapWidth = 700, mapHeight = 700, wallMargin = 30,
turnAggressionDeg = 30, alignThresholdDeg = 10, minTurnThrottle = .55,
powerups = null)` exposes `SteerToBearing` and `SteerToPoint`. Both return a named
`(Throttle, Rudder)` tuple and accept `respectWalls` and `desiredThrottle`.
Steering tapers throttle for large turns. Wall avoidance predicts braking and
turning distance, including overdrive; it is a heuristic, not collision proof.

`Evader(evasionTicks = 15, cooldownTicks = 10, throttle = 1,
initialRudderSign = 1)` reacts to hits. `Update(view)` returns an override command
while evading, otherwise null. Another hit during cooldown flips rudder direction.
`State` and `Reset()` expose the state machine. Subclass it for custom behavior.

## Sensor policies

Implement `ISensorPolicy.Choose(view, tracker)` and optionally `Reset()`, or use:

| Policy | Decision |
| --- | --- |
| `AlwaysActive` | Active every tick. |
| `AlwaysPassive` | Passive every tick. |
| `DutyCycle(10, 20)` | Ten active ticks then twenty passive ticks. |
| `PingWhenStale(4)` | Active with no ship tracks or if any ship's active fix is at least four ticks old. |

Passive observations cannot keep a fix fresh. A fresh neighbor cannot conceal
another stale track from `PingWhenStale`.
