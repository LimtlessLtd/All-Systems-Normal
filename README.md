# All Systems Normal

An emergent AI-driven facility simulation built with .NET 10 and Blazor.

The player is the facility AI. You do not directly control the crew; you control the environment around them: doors, power, communications, cameras, alarms, temperature, ventilation, and other systems. Human behaviour is autonomous and will eventually be driven by an LLM-backed decision layer, while the world itself remains deterministic C#.

## Architecture

- **Overseer.Domain** — world state and shared game contracts.
- **Overseer.Simulation** — deterministic simulation rules, actions, and facility seeding.
- **Overseer.AI** — AI decision boundary. LLM integration lives here and never directly mutates world state.
- **Overseer.Persistence** — persistence boundary for later save games and memories.
- **Overseer.Web** — Blazor control-room UI.
- **Overseer.Simulation.Tests** — simulation tests.

## Run

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Overseer.Web
```

Open the URL printed by ASP.NET Core.

## Core rule

The LLM may decide what an NPC **wants** to do. Only the deterministic simulation decides what an NPC **can** do.

That separation is the foundation for reproducible, testable emergent gameplay.
