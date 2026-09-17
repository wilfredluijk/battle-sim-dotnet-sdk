# Powerups

Select zero or two distinct IDs from `welcome.AvailablePowerups`. The server
catalog is authoritative; the SDK retains unknown future IDs.

```csharp
public override IReadOnlyList<string> ChoosePowerups(Welcome welcome) =>
    ["rapid_fire", "heavy_shell"];

public override Command OnTick(WorldView view)
{
    var command = new Command { Throttle = 0.6 };
    if (view.Me.PowerupReady("rapid_fire")) command.ActivatePowerup = "rapid_fire";
    return command;
}
```

Only activate a selected, unused powerup. `PowerupReady(id)` checks the picked
status's `Used` flag; `PowerupActive(id)` checks `ActiveTicksLeft > 0`.
`PowerupActivatedEvent` reports own activations and visible contact activations.
One activation can accompany a shot in the same command; `Gunner.Attempt`
incorporates `command.ActivatePowerup` into the shot's effective weapon rules.

The defaults below match Python 0.5.0. Operators can change them; always use
`Welcome.Rules.Powerups` and the received ship specs. Durations are simulation
ticks, independent of wall-clock pacing.

| ID | Default effect |
| --- | --- |
| `overdrive` | 50 ticks; speed ×1.6, acceleration ×1.6, turn rate ×1.5. |
| `reinforced_hull` | 70 ticks; incoming damage ×0.45. |
| `repair_drones` | 20 HP immediately, then 1 HP/tick for 50 ticks. |
| `smoke_screen` | 80 ticks, radius 70. |
| `rapid_fire` | 50 ticks; gun cooldown ×0.5. |
| `heavy_shell` | 30 ticks; splash radius ×1.5, damage ×1.3. |
| `long_range_salvo` | 40 ticks; range ×1.5, shell speed ×1.6. |
| `awacs_scan` | 60 ticks; sensor range ×2, silent-target jitter 15, confidence 0.6. |
| `silent_running` | 80 ticks; active detection range multiplier 0.5. |
| `counter_battery_trace` | Armed for 60 ticks; reveal duration 15 ticks. |
| `emp_burst` | 40 ticks, radius 130; affected gun cooldown ×2. |
| `decoy_flare` | 60 ticks; decoy distance 80–140. |

`PowerupConfig` exposes all 33 tuning fields in PascalCase. For example,
`LongRangeSpeedMult`, `HeavyShellSplashMult`, `RapidFireCooldownMult`,
`OverdriveAccelMult`, and `EmpGunCooldownMult`. Unknown tuning is preserved in
`Raw`. Parsing validates finite numbers, positive durations/multipliers, integral
counts, confidence bounds, and ordered decoy distances. Zero is allowed for the
same six tuning fields as the Python implementation.

The tactical toolkit does not automatically choose or activate a loadout. Bots
retain that strategic choice. `Intent.Custom` can return a powerup-bearing command;
when using components directly, set activation before calling `Gunner.Attempt`.
