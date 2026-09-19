# All Systems Normal

An emergent AI-driven facility simulation built with .NET 10 and Blazor.

The player is the facility AI. You do not directly control the crew; you control the environment around them: doors, power, communications, cameras, alarms, temperature, ventilation, and other systems. Human behaviour is autonomous and will eventually be driven by an LLM-backed decision layer, while the world itself remains deterministic C#.

## Play in the browser

The repository includes a standalone Blazor WebAssembly client designed for GitHub Pages:

https://limtlessltd.github.io/All-Systems-Normal/

The Pages build contains the deterministic simulation only. LLM calls will remain behind a server-side API when AI behaviour is added, so no provider secrets are ever shipped to the browser.

## Architecture

- **Overseer.Domain** — world state and shared game contracts.
- **Overseer.Simulation** — deterministic simulation rules, actions, and facility seeding.
- **Overseer.AI** — AI decision boundary. LLM integration lives here and never directly mutates world state.
- **Overseer.Persistence** — persistence boundary for later save games and memories.
- **Overseer.Web** — ASP.NET/Blazor server app for the full game.
- **Overseer.Web.Client** — standalone WebAssembly demo deployed to GitHub Pages.
- **Overseer.Simulation.Tests** — simulation tests.

## Run the server app locally

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Overseer.Web
```

## Run the GitHub Pages client locally

```bash
dotnet run --project src/Overseer.Web.Client
```

## Core rule

The LLM may decide what an NPC **wants** to do. Only the deterministic simulation decides what an NPC **can** do.

That separation is the foundation for reproducible, testable emergent gameplay.
