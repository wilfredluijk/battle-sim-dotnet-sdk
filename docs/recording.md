# Recording and replay

Create a `BotRecorder`, pass it to the runner, and dispose it when the run ends:

```csharp
using var recording = new BotRecorder("match-views.jsonl");
await BotRunner.RunAsync(new MyBot(), new RunOptions
{
    Name = "my-bot",
    Recorder = recording
});
```

The file is created exclusively; an existing file is never overwritten. The first
line is `{"format":"naval-sdk-bot-views","version":1}`, matching the Python SDK.
Only welcome, configuration, game-start, tick, game-over, lobby, and error frames
are recorded. Outbound hello/authentication frames are excluded. Nested field
names containing `token`, `password`, `credential`, or `authorization` are replaced
with `[redacted]`. Arbitrary secrets embedded inside otherwise unmarked strings
cannot be detected automatically.

```csharp
foreach (var decision in Replay.Run(new MyBot(), "match-views.jsonl"))
    Console.WriteLine($"{decision.MatchId}:{decision.Tick} {decision.Command}");
```

`Replay.Run` streams `ReplayDecision` values through the same lifecycle dispatcher
used by live transport. It validates the header and frame shape, supports multiple
rounds and configuration changes, honors `OnGameOver` opt-out, and never opens a
connection. It rejects a bot already owned by a live run or another replay. Dispose
the enumerator (as `foreach` does) when stopping early to release bot ownership.

Observations stay fixed. Different decisions do **not** re-simulate physics or
change later recorded contacts. Use replay to compare decisions, diagnose errors,
and inspect timing; use new server matches to measure strategic outcomes.

Recordings produced by either SDK can be consumed by the other. They contain a
bot's filtered view, not the server's complete simulation replay.
