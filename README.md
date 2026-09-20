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

## V0.7 — Corporate Campaign, Deceit & the Overseer's Voice

### Corporate directives and scenario success

The corporate sponsor assigns graded objectives, and a scenario can now be
**won** as well as lost. Directives are evaluated purely from deterministic
telemetry the station could actually measure — stress, isolation, sealed
compartments, resentment, crew suspicion and whether Overseer is still in
control. The corporation never reads the player's intent, only results.

Each directive carries a sanitised justification shown to the player and a
hidden true purpose for the campaign reveal. Five missions escalate from
plausible caretaking to instructions no procedural reading survives.

### Suspicion you can fight back against

Suspicion is no longer a one-way ratchet:

- evidence decays, and hearsay fades about twice as fast as first-hand sight
- a claim an NPC can personally check is discredited when the station
  contradicts it, and a rumour that fails inspection costs the crew member who
  spread it rather than Overseer
- visibly restoring power, light, air or life support where crew are standing
  buys back trust, weighted by how convinced they already are
- crew notice unexplained faults in their own compartment and may blame a
  colleague they personally saw there, rather than Overseer

### The Overseer's voice

The player can finally speak. Write free text, pick a private channel or a
station-wide broadcast, and an interpreter reads it into a structured claim the
simulation can grade. The core rule still holds: the player chooses what to
**say**, the simulation decides what it **does**. No message sets a belief,
clears suspicion or orders anybody to move.

Truthfulness is fixed at the moment of transmission, so a lie cannot be made
true afterwards. Crew discover it later, when they can see the subject of the
claim for themselves. Being caught costs suspicion scaled by reach and stakes —
a whispered half-truth is cheap, a broadcast fabrication to all six is
ruinous — and an accusation that collapses rehabilitates the person it targeted.

Crew track Overseer's credibility separately from its hostility, so somebody
can stop believing a word you say without yet thinking you are dangerous.

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
- **Overseer.AI** — Microsoft.Extensions.AI cognition, prompts, validation and fallback, plus the model reader for Overseer's own messages
- **Overseer.Persistence** — persistence boundary for future save games and long-term memory
- **Overseer.Web** — full ASP.NET/Blazor Server game with local Ollama cognition
- **Overseer.Web.Client** — static WebAssembly demo deployed to GitHub Pages
- **Overseer.Simulation.Tests** — deterministic and AI-boundary regression tests

## Core rule

The LLM decides what an NPC **wants** to do.

The deterministic simulation decides what the NPC **can** do.

A model can decide that Sarah wants to confront Marcus, inspect Engineering, find food, ask Nadia for help, or avoid somebody. It cannot teleport her through a locked door, invent a room, directly change a relationship score, or kill another NPC by returning text.

The same rule governs the player. Overseer can say anything it likes; it cannot
make anybody believe it. A message lands in proportion to the listener's trust
and their own eyes, and the station itself decides whether the claim survives
contact with reality.
