# API reference

Types below are in `Naval.Sdk`, except tactical types in `Naval.Sdk.Tactical`.
Public parsing methods use `System.Text.Json.Nodes.JsonObject`. JSON numbers must
be numbers, with integral values in integer fields. Nonfinite values and values
outside the server's finite `float` range are rejected.

## Bot callbacks

| Callback | Default / contract |
| --- | --- |
| `AcceptConfiguration(JsonObject, string)` | Return true to accept a validated configuration and hash. False stays unready. Receives a defensive copy. |
| `OnWelcome(Welcome)` | Observe initial or updated rules, including changed game-start and lobby snapshots. |
| `ChoosePowerups(Welcome)` | Return `[]` or two distinct available IDs. Called for each new lobby/configuration. |
| `OnGameStartEvent(GameStart)` | Typed match start; delegates to the positional callback by default. |
| `OnGameStart(int, Vec2, double)` | Tick, spawn position, compass heading. `MatchId` is already populated. |
| `OnTick(WorldView)` | Return a `Command` or null for hold. |
| `OnGameOver(GameOver)` | True continues; false disconnects. |
| `OnLobby(int)` | Called before a new lobby's loadout/readiness. |
| `OnError(string, string)` | Observe a server error code and message. |
| `OnTickTiming(TickTiming)` | Observe callback and serialization time. |
| `OnDisconnect(DisconnectInfo)` | Observe close code, reason, and interrupted phase. |

Callbacks run synchronously and serially on the runner's continuation thread.
An independent socket reader keeps servicing WebSocket control frames while a
callback runs. Adjacent queued ticks from one match can be coalesced: the newest
observation receives their events in order, while intermediate contacts/self states
are skipped. Lifecycle and error callbacks retain their order. See
[timing and connection troubleshooting](troubleshooting.md).
Do not implement them as `async void`; use synchronous decisions over cached data.
Exceptions are isolated and counted; `OnTick` exceptions and invalid commands
produce hold commands. Commands are sent only while their tick is still current
and within its locally measured deadline. `OnGameOver` exceptions retain the default of continuing.
A callback cannot be forcibly interrupted at its deadline.

Bot state: `Welcome`, `LastTick`, `MatchId`, `Phase`, `Diagnostics`. Phases are
`disconnected`, `connecting`, `lobby`, `running`, and `ended`. One runner or replay
may own a bot at a time. `RawSendAsync(JsonObject, CancellationToken)` sends on an
open connection; concurrent sends are serialized. `RawReceiveAsync` rejects calls
because the managed runner owns the reader. Use typed callbacks or a recorder.

## Running and options

`BotRunner.RunAsync(bot, options, cancellationToken)` returns `Task<GameOver?>`.
`BotRunner.Run` is the synchronous wrapper. The result is the most recent match
result, or null when no match completed. Cancellation throws
`OperationCanceledException`. Unsupported versions throw `ProtocolMismatchException`.
Exhausted connection/handshake or established-session transport failures throw a
sanitized `IOException`. Abnormal WebSocket close codes also throw. Normal closes
(1000/1001), fatal server error frames and `OnGameOver` opt-out return the last
result; inspect `LastDisconnect` and `RejectedCommands` to distinguish them.

| `RunOptions` property | Default |
| --- | --- |
| `Host`, `Port`, `Path` | `localhost`, `7878`, `/bot` |
| `Url`, `Token` | null, resolved from environment |
| `Name` | `bot` |
| `Version` | `naval-sdk-dotnet/` plus assembly package version |
| `Recorder` | null |
| `ReconnectAttempts` | 0 |
| `ReconnectDelay` | 1 second; must be 0–60 seconds |
| `HandshakeTimeout` | 10 seconds; covers connecting through welcome |
| `MaxMessageBytes` | 1 MiB, including all fragments |

Retries are bounded and only occur outside an active match. A dropped active
connection forfeits the ship and is never silently resumed. Fatal authentication,
name, or rate-limit errors stop without reconnecting.
Incoming buffering is bounded to 128 frames. If callbacks or recording cannot
keep up even with tick coalescing, the run reports an actionable `IOException`.
`RunOptions.ToString()` redacts the token and omits free-form connection fields.

## Models

All wire models have `FromJson(JsonObject)` (events accept a `JsonNode?`).

| Model | Fields and conveniences |
| --- | --- |
| `Vec2` | `X`, `Y`, `Length`, addition, subtraction, scalar multiplication. |
| `MapInfo` | `Width`, `Height`. |
| `ShipSpecs` | Forward/reverse speed, acceleration, turn rate, hull HP, ammo, cooldown, hit radius, shell speed/range, splash radius/damage; `Validate()`. |
| `Welcome` | Bot/ship IDs, map, pacing, specs, powerup catalog, simulation dt, protocol version, config hash, `Configuration`, typed `Rules`; `WithConfiguration`. |
| `MatchConfiguration` | Version, revision, dt, pacing, deadline, map, specs, sensors, powerups, catalog, timeout, wall damage, capabilities, defensive `Raw`. |
| `SensorConfig` | Active range/noise, passive hearing ranges and bearing noise; `Validate()`. |
| `PowerupConfig` | Every Python tuning field, converted to PascalCase; defensive `Raw`, `Validate()`. |
| `GameStart` | Tick, position, heading, optional specs, dt, match ID. |
| `SelfState` | Position, heading, speed, HP, ammo, rudder, throttle, selected IDs, statuses, nullable gun cooldown, EMP ticks; `Powerup`, `PowerupReady`, `PowerupActive`. |
| `Contact` | ID, string kind, position, bearing, nullable range, confidence. |
| `WorldView` | Tick, deadline, self state (`Me` alias), contacts, events, match ID; `NearestContact()` considers only ranged contacts. |
| `GameOver` | Nullable winner, final tick, replay ID. |
| `PowerupStatus` | ID, used flag, active ticks remaining. |

Events are `HitEvent`, `ShellSplashEvent`, `PowerupActivatedEvent`, or
`UnknownEvent`. Unknown and malformed individual events retain their raw JSON
without discarding the entire tick. Contact kinds and powerup IDs remain strings
so future values can pass through. Parsed lists are read-only snapshots.

## Commands and helpers

`Command` has mutable `Throttle`, `Rudder`, `SensorMode` (default Active), optional
`Fire` and optional `ActivatePowerup`. `SensorMode` is the Active/Passive enum.
`FireCommand` contains `BearingDeg` and `Range`.

`Command.FireAt(targetPos, shooterPos = null, targetVel = null, shellSpeed = 70,
range = null, lead = true)` mutates and returns the command. It falls back to the
current target position if no intercept exists. Supply authoritative shell speed
from the welcome. `ToJson(tick, matchId)` returns the wire command with optional
fields omitted; the server clamps throttle and rudder to [-1,1].

`Helpers`: `Distance`, `WrapBearing`, `SignedBearingDelta`, `Clamp`, `BearingTo`,
`LeadTarget`. Compass 0° is north (-y), 90° east (+x); angles increase clockwise.
`SignedBearingDelta` uses [-180,180), matching Python's actual implementation.
`LeadTarget` returns null when there is no nonnegative-time intercept.

## Diagnostics

`RuntimeDiagnostics` contains `Ticks`, `Overruns`, `SkippedTicks`, `DiscardedCommands`, `CallbackErrors`,
`MalformedFrames`, `RejectedCommands` keyed by code, `LastTiming`, and
`LastDisconnect`. A live run/replay starts fresh diagnostics; reconnect attempts
within one run accumulate them. `Ticks` counts decisions actually evaluated;
`SkippedTicks` counts intermediate observations coalesced into a newer tick;
`DiscardedCommands` counts computed commands suppressed before sending. Timing
covers decision and serialization work, excluding queue delay, network latency
and the `OnTickTiming` hook itself. The send guard also accounts for time spent
queued, recording, waiting for another send and in that hook. The SDK does not
log hello frames or credentials. Raw server error/close text is supplied to your
callbacks; decide what your application's logger should retain.
