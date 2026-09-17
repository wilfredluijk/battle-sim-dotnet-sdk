# Getting started

## Build from source

Install the .NET 10 SDK to build this repository. Install the .NET 8 runtime as well
to run the entire test suite. Consumers can use the packaged library from .NET 8
or later; the package contains separate .NET 8 and .NET 10 assemblies.

```sh
git clone https://github.com/wilfredluijk/battle-sim-dotnet-sdk.git
cd battle-sim-dotnet-sdk
dotnet build BattleSim.sln -c Release
dotnet run --project examples/HunterBot -- --help
```

To install a package, download the `.nupkg` from the
[GitHub release](https://github.com/wilfredluijk/battle-sim-dotnet-sdk/releases)
into `packages/`, then run this from your bot project:

```sh
dotnet add package Naval.Sdk --version 0.1.0 --source ./packages
```

There is no NuGet.org publication. You can also add a project reference to
`src/Naval.Sdk/Naval.Sdk.csproj` in a local checkout.

## First bot

```csharp
using Naval.Sdk;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
try
{
    await BotRunner.RunAsync(new MyBot(), new RunOptions { Name = "my-bot" }, stop.Token);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }

sealed class MyBot : Bot
{
    public override Command OnTick(WorldView view)
    {
        var command = new Command { Throttle = 0.6, Rudder = 0.2 };
        var target = view.NearestContact();
        if (target is { Kind: "ship" } && view.Me.Ammo > 0)
            command.FireAt(target.Pos, shooterPos: view.Me.Pos);
        return command;
    }
}
```

This simple bot aims at current positions and requests shots without cooldown or
self-splash checks. Use `Gunner` or `TacticalBot` for those checks and moving-target
intercepts. Always pass your own position to `FireAt`; its default origin is `(0,0)`.

## Connection precedence

`RunOptions.Url` overrides `BATTLE_SERVER_URL`, which overrides the URL constructed
from `Host` (localhost), `Port` (7878), and `Path` (/bot). Set `Url` explicitly when
you want to override an ambient endpoint. `Token = null` reads `BATTLE_BOT_TOKEN`;
`Token = ""` explicitly disables that fallback.

For console applications, `ConnectionArguments.Parse(args).ToRunOptions("my-bot")`
supports `--url`, `--host`, `--port`, and `--env-file`. `--url` and host/port flags
are mutually exclusive. Explicit host/port flags override the file/environment URL.
The console helper requires a URL ending in `/bot`, and a credential for `wss://`.
The lower-level runner supports custom WebSocket paths.

Participant files define both settings:

```dotenv
BATTLE_SERVER_URL=wss://operator.example/bot
BATTLE_BOT_TOKEN='your-participant-credential'
```

An explicitly selected file replaces both ambient settings as a pair. It supports
quotes, comments, and optional `export`; it never executes commands or expands
variables. Files must be UTF-8 and at most 16 KiB. URLs must use `ws` or `wss` and
cannot contain credentials, query strings, fragments, or whitespace.

The included hunter reads the same arguments:

```sh
dotnet run --project examples/HunterBot -- --env-file /path/to/participant.env
```
