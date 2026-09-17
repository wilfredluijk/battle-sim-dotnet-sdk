# Contributing

Build with the .NET 10 SDK. Install the .NET 8 runtime to run both target suites.
The library uses built-in .NET APIs; xUnit and the test SDK are development-only.

```sh
dotnet restore BattleSim.sln --locked-mode
dotnet build BattleSim.sln -c Release --no-restore
dotnet test BattleSim.sln -c Release --no-build
dotnet pack src/Naval.Sdk -c Release --no-build -o artifacts
```

GitHub Actions runs the solution on Linux, Windows, and macOS and retains package
and test-result artifacts. Dependency versions and official action revisions are
pinned. Update the lock files when deliberately changing package dependencies.

## Verification

Tests cover wire models, atomic configuration changes, loadouts, command fallback,
diagnostics, recording/replay, participant files, tactical regressions, and real
loopback WebSockets. The Python oracle fixture is generated from the source
revision recorded in [NOTICE](NOTICE). To regenerate it, install that checkout and
its `websockets` dependency in an isolated Python environment, then run:

```sh
PYTHONPATH=/path/to/battle-sim-python-sdk python tools/generate_parity.py
```

Review the resulting fixture changes against the upstream algorithms.

One optional integration test starts an actual local Rust simulator, connects two
.NET bots, updates rules, runs two rounds, and verifies telemetry and replay:

```sh
BATTLE_SIM_SERVER=/absolute/path/to/naval-server dotnet test BattleSim.sln -c Release
```

The test uses a temporary server with synthetic credentials and an ephemeral port.
It never connects to a supplied production endpoint. Without `BATTLE_SIM_SERVER`,
this one test is explicitly skipped. All other tests are self-contained.

Check Markdown links with `python3 tools/check_docs.py`. Keep examples and docs in
step with API changes; the hunter is compiled as part of the solution.

## Release

Update the library's `Version`, package examples, and changelog. The runtime hello
version derives from the assembly version. Build, test, pack, and install the
generated package into a clean sample project. Publish the `.nupkg` and `.snupkg`
as GitHub release assets under a matching `vX.Y.Z` tag. A NuGet.org release is a
separate operation requiring the appropriate feed credentials.
