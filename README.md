# All Systems Normal

An emergent AI-driven facility simulation built with .NET 10 and Blazor.

You are the station AI. You do not directly control the crew; you control the environment around them: doors, power, cameras, lighting, communications, and eventually temperature, ventilation, alarms, robots, and other systems.

## Play in the browser

GitHub Pages build:

https://limtlessltd.github.io/All-Systems-Normal/

## V0.2 — Station View

The station is now the primary gameplay surface:

- top-down 2D facility overview
- live room power, lighting, camera, temperature and oxygen state
- visible open / closed / locked doors
- selectable rooms and crew
- crew telemetry and current intent inspector
- camera blind spots that hide crew from the map
- interactive room power, cameras, lights and doors
- run / pause / speed controls
- deterministic crew routines so people visibly move around the facility
- animated crew transitions between rooms
- event stream showing system and movement activity

The GitHub Pages version runs the deterministic simulation entirely in the browser.

## Architecture

- **Overseer.Domain** — world state and shared game contracts.
- **Overseer.Simulation** — authoritative deterministic simulation, movement and action validation.
- **Overseer.AI** — AI decision boundary. LLM integration lives here and never directly mutates world state.
- **Overseer.Persistence** — persistence boundary for later save games and memories.
- **Overseer.Web** — ASP.NET/Blazor server app for the full game.
- **Overseer.Web.Client** — standalone WebAssembly build for GitHub Pages.
- **Overseer.Simulation.Tests** — deterministic simulation tests.

## Run locally

Full server app:

```bash
dotnet run --project src/Overseer.Web
```

Browser-only WebAssembly build:

```bash
dotnet run --project src/Overseer.Web.Client
```

Run all checks:

```bash
dotnet build Overseer.slnx
dotnet test
```

## Core rule

The LLM may decide what an NPC **wants** to do. Only the deterministic simulation decides what an NPC **can** do.

That separation is the foundation for reproducible, testable emergent gameplay.
