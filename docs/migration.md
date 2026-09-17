# Migrating from Python

The reference is Python SDK 0.5.0 at commit
[`816fa9b`](https://github.com/wilfredluijk/battle-sim-python-sdk/commit/816fa9baba75c7294561a6f111aa2735f4bbdd01).
Protocol, algorithms, powerup defaults, and JSONL recordings are retained.

| Python | C# |
| --- | --- |
| `from naval_sdk import ...` | `using Naval.Sdk;` |
| `naval_sdk.tactical` | `Naval.Sdk.Tactical` |
| `(x, y)` | `new Vec2(x, y)` |
| `class MyBot(Bot)` | `sealed class MyBot : Bot` |
| `on_tick(self, view)` | `public override Command OnTick(WorldView view)` |
| `Command(throttle=.6)` | `new Command { Throttle = .6 }` |
| `"active"` / `"passive"` | `SensorMode.Active` / `SensorMode.Passive` |
| `run_async(bot, name="bot")` | `BotRunner.RunAsync(bot, new RunOptions { Name = "bot" })` |
| `run(bot, ...)` | `BotRunner.Run(bot, options)` |
| `choose_powerups` | `ChoosePowerups` returns `IReadOnlyList<string>` |
| `view.me`, `view.nearest_contact()` | `view.Me`, `view.NearestContact()` |
| `Command.fire_at(...)` | `command.FireAt(...)` |
| `from_dict` / `to_dict` | `FromJson` / `ToJson`, using `JsonObject` |
| `ProtocolMismatch` | `ProtocolMismatchException` |
| `with BotRecorder(path)` | `using var recorder = new BotRecorder(path)` |
| `replay(bot, path)` | `Replay.Run(bot, path)` |
| `Intent.patrol((x1,y1,x2,y2))` | `Intent.Patrol(new PatrolRect(x1,y1,x2,y2))` |
| `SensorPolicy` protocol | `ISensorPolicy` interface with optional `Reset()` |

Other snake_case fields and methods become PascalCase. Constructor/optional
parameter names use camelCase. Contact kinds, powerup IDs, protocol field names,
and track source strings retain their wire spellings.

Intentional .NET adaptations:

- Async transport uses `Task`, `CancellationToken`, and built-in `ClientWebSocket`.
  Bot callbacks are synchronous. Cancellation releases ownership and throws the
  standard .NET cancellation exception.
- Records represent immutable value models; commands and track estimates remain
  mutable. Configuration/raw snapshots are defensive copies. Directly constructed
  configurations expose `Validate()`; wire parsing and tactical constructors
  validate rules before use.
- JSON parsing requires actual numbers and integral values for count fields,
  rather than Python's permissive string conversion/truncation. Malformed server
  frames are still isolated and counted.
- Unknown or malformed individual events become `UnknownEvent` with raw JSON.
- `OnGameOver` returns a non-nullable bool: true continues, false disconnects.
- `RawReceiveAsync` explicitly rejects managed-reader access. Use typed callbacks
  and a recorder. `RawSendAsync` is available while a live connection is open.
- Replays own the bot while their iterator is active and reset lifecycle state and
  diagnostics before dispatching. Dispose a partially consumed iterator.
- The runner limits messages to 1 MiB by default and applies a total handshake
  timeout. Both limits are configurable. Fatal server errors stop immediately.
- `SignedBearingDelta` documents [-180,180), the interval the Python code actually
  returns (its original docstring used a different endpoint convention).

The committed oracle compares 140 ticks of tracker, gunner, helm, sensor, evader,
and tactical-bot outputs against Python. Transport and lifecycle tests separately
cover readiness, malformed input, round resets, and reconnect policy.
