# Changelog

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
