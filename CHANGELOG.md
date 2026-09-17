# Changelog

## Unreleased

- Keep WebSocket reads and heartbeat handling independent of synchronous bot callbacks.
  Coalesce adjacent queued ticks, carry their events forward, and discard superseded
  or locally expired commands. Add `SkippedTicks` and `DiscardedCommands` diagnostics.
- Bound incoming buffering and report overload instead of silently losing lifecycle frames.
- Report exhausted transport failures as sanitized `IOException`s, including failures
  after welcome; keep lobby retries and never reconnect an active ship.
- Apply full configuration snapshots on game start and lobby reset, including hashes,
  sensors, powerups and typed rules. Keep legacy specs/dt updates consistent.
- Prevent ship/shell track association and expire tracks before matching new contacts.
- Redact credentials and omit free-form connection fields from `RunOptions.ToString()`.
- Add heartbeat, timing recovery, lifecycle, tracking and credential regression tests.

## 0.1.0 — 2026-09-17

Initial C#/.NET port of battle-sim Python SDK 0.5.0, revision `816fa9b`.

- .NET 8 and .NET 10 library with no external runtime dependencies.
- Protocol 3.x models, validated configuration/readiness, loadouts and powerups.
- Async WebSocket runtime, multiple rounds, cancellation, bounded lobby reconnects.
- Tactical tracking, fire control, steering, evasion, sensor policies and intents.
- Runtime diagnostics and Python-compatible redacted JSONL recording/replay.
- Participant environment-file parser and runnable hunter example.
- Developer guide, migration reference, tests and cross-platform CI.

Validated locally with 94 passing tests per target, including a 140-tick Python
comparison and two-match integration against the actual Rust server.
