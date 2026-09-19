# All Systems Normal

An emergent AI-driven space-station simulation built with .NET 10 and Blazor.

You are the station AI. You do not directly control the crew; you control the environment around them. The humans have needs, memories, beliefs, relationships and — in the full local build — LLM-generated high-level intentions.

## Play in the browser

Static GitHub Pages demo:

https://limtlessltd.github.io/All-Systems-Normal/

The Pages build deliberately does **not** contain API keys or direct LLM access. It runs the full deterministic station simulation plus a lightweight deterministic "Browser demo" mind that exercises the same persistent-intent system.

## Run the real AI build locally

V0.4 uses Microsoft.Extensions.AI with a local Ollama `IChatClient`.

Install Ollama, then download the default development model:

```bash
ollama pull qwen3:4b
```

Make sure Ollama is running, then from the repository root:

```bash
git pull
dotnet restore
dotnet run --project src/Overseer.Web
```

Open the HTTPS URL printed by ASP.NET Core.

By default the game connects to:

```text
http://localhost:11434
qwen3:4b
```

You can override those values with:

```bash
# PowerShell
$env:OLLAMA_ENDPOINT="http://localhost:11434"
$env:OLLAMA_MODEL_NAME="qwen3:4b"

# bash/zsh
export OLLAMA_ENDPOINT="http://localhost:11434"
export OLLAMA_MODEL_NAME="qwen3:4b"
```

If Ollama is unavailable or returns invalid structured output, the station continues running with a deterministic fallback mind. The crew inspector clearly labels each decision as `Ollama`, `Fallback`, `Browser demo`, or `Routine`.

## V0.4 — Autonomous Minds

- provider-neutral cognition through `Microsoft.Extensions.AI.IChatClient`
- local Ollama provider through OllamaSharp
- typed structured LLM responses via `GetResponseAsync<T>`
- one high-level deliberate thought every four simulated minutes
- round-robin cognition so six local agents do not hammer the model at once
- persistent goals that survive across multiple movement ticks
- LLM plans must obey deterministic navigation, doors and action validation
- perception-limited prompts: NPCs do not receive the global event log
- prompt context includes personality, needs, current room, visible people, direct doors, relationships, memories and beliefs
- model-generated room/person targets are strictly validated
- hallucinated targets collapse safely to Idle
- LLMs cannot directly choose lethal attacks; violent outcomes remain deterministic consequences of escalating relationships/stress
- graceful rule-based fallback when Ollama is offline
- GitHub Pages uses a transparent deterministic demo mind instead of exposing model credentials

## Architecture

- **Overseer.Domain** — authoritative world state, memories, relationships and persistent intentions
- **Overseer.Simulation** — deterministic world rules, movement, social consequences and intent execution
- **Overseer.AI** — Microsoft.Extensions.AI cognition, prompts, validation and fallback
- **Overseer.Persistence** — persistence boundary for future save games and long-term memory
- **Overseer.Web** — full ASP.NET/Blazor Server game with local Ollama cognition
- **Overseer.Web.Client** — static WebAssembly demo deployed to GitHub Pages
- **Overseer.Simulation.Tests** — deterministic and AI-boundary regression tests

## Core rule

The LLM decides what an NPC **wants** to do.

The deterministic simulation decides what the NPC **can** do.

A model can decide that Sarah wants to confront Marcus, inspect Engineering, find food, ask Nadia for help, or avoid somebody. It cannot teleport her through a locked door, invent a room, directly change a relationship score, or kill another NPC by returning text.
