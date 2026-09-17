# Developer guide

Naval.Sdk ports the Python SDK 0.5.0 to C# for .NET 8 and .NET 10. It speaks the
same protocol 3.x, uses the same compass coordinates, and retains the tactical
algorithms and bot-view recording format.

1. [Getting started](getting-started.md): installation, first bot, connections.
2. [API reference](api.md): callbacks, models, runtime options, diagnostics.
3. [Protocol and lifecycle](protocol.md): handshake, readiness, rounds, errors.
4. [Tactical toolkit](tactics.md): tracking, aiming, navigation, sensor policies.
5. [Powerups](powerups.md): selection, activation, and all twelve effects.
6. [Recording and replay](recording.md): offline decision evaluation.
7. [Migrating from Python](migration.md): names and intentional .NET differences.
8. [Hosted server run review, 17 September 2026](run-review-2026-09-17.md): results
   and command diagnostics from the last three live matches.
9. [Timing and connection troubleshooting](troubleshooting.md): wrong ticks,
   decision overruns, dropped commands and disconnects.

The [complete hunter example](../examples/HunterBot/Program.cs) builds with the
solution. The library has no external runtime dependencies. Test packages are
limited to the test project.

Choose `Bot` when you want full control of each command. Choose `TacticalBot` when
you want to express intent and let tracking, evasion, steering, and fire control
produce the command. All components can also be used independently.
