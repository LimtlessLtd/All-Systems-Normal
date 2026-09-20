# All Systems Normal — Project Handoff

Repository: https://github.com/LimtlessLtd/All-Systems-Normal  
Playable Pages build: https://limtlessltd.github.io/All-Systems-Normal/

**Current state:** V0.9B — Fixed Security Turret & Human Counterplay  
**Next recommended milestone:** V0.9C — Contained Security-Network Malware & Crew Recovery

This file is the authoritative handoff. Keep it concise and update sections in place; do not append milestone diaries.

---

## Product and authority model

**All Systems Normal** is an emergent space-station simulation where the player is the station AI. Humans are autonomous; the player manipulates station systems, information and circumstances rather than issuing unit orders.

Non-negotiable rule:

> **The LLM decides what an NPC WANTS to do. Deterministic C# decides what the NPC CAN do and what actually happens.**

LLMs may choose intentions, dialogue, beliefs and reasoning. Deterministic systems must validate entity existence, NPC knowledge/perception, reachability, physical location, door/path state, legality, skills, time, damage and death.

Never allow LLM-driven world mutation, teleportation, invented entities or remote omniscient knowledge.

Core invariants:

- Npc.CurrentRoomId is authoritative containment.
- PositionX/PositionY and Movement are local physical/presentation state.
- Door.IsPassable is navigation truth; crossings revalidate current door state.
- Strategic routing uses deterministic A* over actual station topology.
- Knowledge/evidence is observer-specific and provenance-aware.
- Map entities require stable Blazor @key identity.
- GitHub Pages remains model/credential-free.
- Campaign persistence stores deliberate continuity only, never arbitrary live GameState.

---

## Stack and runtime split

- .NET 10 / C#
- Blazor Server + ASP.NET Core
- Blazor WebAssembly
- xUnit
- Microsoft.Extensions.AI
- OllamaSharp / local Ollama

Projects:

- Overseer.Domain — authoritative contracts/state
- Overseer.Simulation — deterministic mechanics/validation
- Overseer.AI — Ollama cognition and structured adapters
- Overseer.Persistence — versioned campaign persistence
- Overseer.Web — server/Ollama runtime
- Overseer.Web.Client — deterministic static Pages runtime
- Overseer.Simulation.Tests — regression contract

Server uses Ollama for NPC cognition/crew generation/message interpretation with deterministic fallbacks. Pages uses browser-safe deterministic cognition. Shared mechanics belong in Domain/Simulation and must not be independently reimplemented in each UI.

Default Ollama configuration is currently qwen3:4b at http://localhost:11434, overridable by configuration/environment.

---

## Current implemented state

The station already supports:

- physical rooms, connector hallways, doors, fixtures and A* movement
- door locking/opening, manual override, bypass, damage, repair, welding and barricading
- power, equipment wear/repair, life support, oxygen/CO2/pressure, temperature and ventilation
- pressure-cycled airlock operation and unsafe decompression consequences
- hydroponics, food stores, cooking, eating and routine human needs
- autonomous crew with skills, generated traits, relationships, memories, beliefs, persistent intents and observer-specific knowledge
- suspicion/evidence with provenance, investigation, testimony, account comparison and credibility
- broadcasts/private Overseer messages interpreted as claims rather than truth
- corporate directives, five ordered campaign assignments, carry-over consequences, sponsor reveal and explicit endings
- browser-local versioned campaign persistence; mid-assignment live simulation is intentionally not persisted
- resizable IDE-style UI, map zoom, speech/thought bubbles, event audio and ambient music

### MR-1 autonomous robot

StationRobot, RobotSystem and RobotCountermeasureSystem provide one physical MR-1 platform.

- Overseer may set Friendly / Neutral / Hostile policy and remote power only while its control link exists.
- Friendly repairs real faults; Neutral patrols/charges; Hostile physically navigates and attacks only under deterministic range/cadence rules.
- Crew can locally shut down, isolate its Engineering control link, deny charging, damage it and locally reprogram/reboot it.
- Robot actions create observer-local evidence.
- Both runtimes expose the same authoritative state and link-gated controls.

### ST-1 fixed security turret

V0.9B adds one SecurityTurret (st-1) mounted in the Central Corridor, implemented primarily by TurretSystem.cs and TurretCountermeasureSystem.cs.

Authoritative turret state includes installed room/position, integrity, policy, armed state, remote-link isolation, dedicated power feed, ammunition, heat, tracked target and firing cadence.

Overseer controls only Safe / ProtectOverseer / SuppressCrew policy and armed/disarmed state. Both require the remote control link. There is no click-to-damage command.

Deterministic C# owns target eligibility, same-compartment coverage/range, cadence, hit/miss, damage, ammunition, heat/cooling, power, death and evidence. A turret cannot observe or fire through another compartment.

Arming, tracking and firing generate observer-local evidence.

Grounded crew counterplay:

- local disarm
- Engineering network isolation
- Engineering power denial
- local physical sabotage/damage
- skilled local reprogramming after disarm

Remote Engineering counterplay requires personally held threat evidence. Network isolation blocks later Overseer policy/arming commands but does not erase local turret state.

Browser fallback cognition, Ollama prompts/validation, both session loops and both UIs understand the same turret actions/state. Regression coverage is in TurretSystemTests.cs.

---

## Persistence boundary

Persistence implementation: src/Overseer.Persistence/CampaignStateSerializer.cs, format version 1, browser key all-systems-normal.campaign.v1.

Persist only deliberate campaign continuity such as mission history, sponsor compliance/reveal, continuing crew identity/traits/skills, relationships, bounded important memories, credibility/suspicion, health/presence consequences, equipment condition and provisions.

Do not persist live movement, intents, jobs, investigations or other transient simulation state unless a future milestone explicitly redesigns the save contract.

---

## Important implementation anchors

Inspect current code before changing a subsystem.

- src/Overseer.Domain/Models.cs
- src/Overseer.Simulation/FacilitySeeder.cs
- src/Overseer.Simulation/ScenarioSystems.cs
- src/Overseer.Simulation/RobotSystem.cs
- src/Overseer.Simulation/RobotCountermeasureSystem.cs
- src/Overseer.Simulation/TurretSystem.cs
- src/Overseer.Simulation/TurretCountermeasureSystem.cs
- src/Overseer.Simulation/BrowserMindSystem.cs
- src/Overseer.AI/NpcPromptBuilder.cs
- src/Overseer.AI/OllamaAiDecisionService.cs
- src/Overseer.Web/Services/GameSession.cs
- src/Overseer.Web.Client/Services/GameSession.cs
- both Home.razor files
- tests/Overseer.Simulation.Tests/

Do not rely on historical test counts or old PR descriptions.

---

## Mandatory Git/validation workflow

Always follow:

main → new feature branch → implementation → tests/build/publish → PR → green CI → merge into main → verify post-merge GitHub Pages

Do not leave completed green work in an open PR unless explicitly instructed.

Standard validation:

- dotnet build Overseer.slnx -c Release
- dotnet test tests/Overseer.Simulation.Tests/Overseer.Simulation.Tests.csproj -c Release --no-build
- dotnet publish src/Overseer.Web.Client/Overseer.Web.Client.csproj -c Release -o release --no-restore

.github/workflows/pages.yml runs those gates on PRs and deploys Pages from main.

Before handoff: final PR head green, merge, verify the main Pages deployment, then leave this file current and concise.

---

## Next milestone — V0.9C Contained Security-Network Malware & Crew Recovery

Add one narrow malware vertical slice that builds on MR-1/ST-1 control links. Do not create a broad hacking framework.

Required scope:

1. Add one explicit security-network compromise/infection state with a deterministic entry path and lifecycle.
2. Overseer may deploy only a high-level malware action against a reachable security asset/network; deterministic C# decides whether the path exists and what systems are affected.
3. Initial scope affects MR-1/ST-1 control-link behaviour only; do not spread across every station subsystem.
4. Infection may interfere with crew isolation/reprogramming or temporarily alter control ownership, but may not directly deal damage or bypass robot/turret physical firing rules.
5. Compromise, anomalous commands and recovery attempts create observer-local evidence/diagnostic clues.
6. Humans can detect, physically isolate and purge/reimage the compromised controller from appropriate Engineering/local hardware with skill/time requirements.
7. Browser fallback and Ollama cognition receive only grounded evidence/diagnostics and choose high-level response intentions.
8. Surface compromise/recovery state in both Pages and server UIs.
9. Add regression coverage for authority, network reachability, isolation containment, recovery, evidence and unchanged physical combat rules.

Keep deferred: broad malware families, self-propagating station-wide infection, multiple weapon classes, robot/turret self-destruct, or lethal outcomes delegated directly to an LLM.

---

## Handoff prompt rule

Future chat prompts should be short: tell the next agent to read this file in full, inspect current main/PR/CI/Pages state, implement the single documented next milestone, and follow the mandatory branch→PR→green CI→merge→Pages workflow.

Put technical detail here, not in the chat handoff prompt.
