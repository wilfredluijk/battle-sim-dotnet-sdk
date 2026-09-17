# Naval.Sdk

C#/.NET SDK for building bots for the battle-sim naval simulator, ported from
[battle-sim-python-sdk](https://github.com/wilfredluijk/battle-sim-python-sdk).
Targets **.NET 8 and .NET 10**, supports **server protocol 3.x**, and has no external
runtime dependencies. MIT licensed.

The SDK handles authentication, configuration agreement, match lifecycle, command
validation, diagnostics, and optional reconnects. The tactical toolkit provides
tracking, aiming, navigation, sensors, evasion, and powerup-aware fire control.
Recordings are compatible with the Python SDK's bot-view JSONL format.

## Quick start

Install the .NET 10 SDK, clone this repository, and run the included hunter:

```sh
git clone https://github.com/wilfredluijk/battle-sim-dotnet-sdk.git
cd battle-sim-dotnet-sdk
dotnet run --project examples/HunterBot -- --url ws://localhost:7878/bot
```

For an operator-provided participant file:

```sh
dotnet run --project examples/HunterBot -- --env-file /path/to/participant.env
```

Alternatively, set `BATTLE_SERVER_URL` and `BATTLE_BOT_TOKEN`. Credentials are never
accepted as command-line flags. The default endpoint is `ws://localhost:7878/bot`.

## Write a bot

Create a console project and reference the library from your checkout:

```sh
dotnet new console -n MyBot
dotnet add MyBot/MyBot.csproj reference battle-sim-dotnet-sdk/src/Naval.Sdk/Naval.Sdk.csproj
```

Replace `MyBot/Program.cs` with:

```csharp
using Naval.Sdk;

await BotRunner.RunAsync(new MyBot(), new RunOptions { Name = "my-bot" });

sealed class MyBot : Bot
{
    public override Command OnTick(WorldView view) =>
        new() { Throttle = 0.6, Rudder = 0.2 };
}
```

Use `TacticalBot.Decide` to express `Engage`, `Patrol`, `RetreatTo`, `Hold`, or
`Custom` intents. See the [hunter example](examples/HunterBot/Program.cs) and
[developer guide](docs/index.md).

## Packages and development

GitHub [releases](https://github.com/wilfredluijk/battle-sim-dotnet-sdk/releases)
include `Naval.Sdk.0.1.0.nupkg` and symbols. Download the package into a local feed:

```sh
dotnet add MyBot/MyBot.csproj package Naval.Sdk --version 0.1.0 --source ./packages
```

The package is distributed through GitHub releases; it is not published on NuGet.org.
To build, test (requires both .NET 8 and .NET 10 runtimes), and pack:

```sh
dotnet build BattleSim.sln -c Release
dotnet test BattleSim.sln -c Release --no-build
dotnet pack src/Naval.Sdk -c Release --no-build -o artifacts
```

See [CONTRIBUTING.md](CONTRIBUTING.md), [API reference](docs/api.md),
[Python migration guide](docs/migration.md), and [source attribution](NOTICE).
