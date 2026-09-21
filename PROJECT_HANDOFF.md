# All Systems Normal — Project Handoff

Repository: https://github.com/LimtlessLtd/All-Systems-Normal
Playable Pages build: https://limtlessltd.github.io/All-Systems-Normal/

**Current state:** V0.10D — Large White/Grey Station Deck + External Telemetry
**Next recommended milestone:** V0.10E — Inspector/Debug Separation, Door Interaction + Broader Crew Agency

This file is the authoritative technical handoff. Keep it concise and update sections in place; do not append milestone diaries.

---

## Product and authority model

**All Systems Normal** is an emergent station simulation where the player is the station AI. Humans are autonomous; the player manipulates systems, information and circumstances rather than issuing unit orders.

Non-negotiable rule:

> **The LLM decides what an NPC WANTS to do. Deterministic C# decides what the NPC CAN do and what actually happens.**

Deterministic systems own entity validity, knowledge/perception, containment, routing, physical crossings, skills, time, environment, combat, damage and death.

Core invariants:

- Npc.CurrentRoomId is authoritative containment.
- PositionX / PositionY / Movement are local physical/presentation state.
- Door.IsPassable is navigation truth; crossings revalidate live door state.
- Strategic routing is deterministic A* over real room/corridor/door topology.
- Visible map connections must be real navigable geometry; never draw fake corridor edges.
- Knowledge/evidence is observer-specific and provenance-aware.
- Stable Blazor @key identity is required for map entities.
- Pages remains model/credential-free.
- Campaign persistence stores deliberate continuity, not arbitrary live GameState.

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
- Overseer.Simulation — deterministic mechanics/generation/validation
- Overseer.AI — Ollama cognition and structured adapters
- Overseer.Persistence — versioned campaign persistence
- Overseer.Web — server/Ollama runtime
- Overseer.Web.Client — deterministic static Pages runtime
- Overseer.Simulation.Tests — regression contract

Shared mechanics belong in Domain/Simulation and must not be independently reimplemented in each UI.

---

## Procedural station architecture

Primary files:

- src/Overseer.Domain/StationGenerationModels.cs
- src/Overseer.Simulation/StationGenerator.cs
- src/Overseer.Simulation/FacilitySeeder.cs
- tests/Overseer.Simulation.Tests/ProceduralStationGenerationTests.cs
- tests/Overseer.Simulation.Tests/StationGeometryTests.cs

Generation is deterministic for the same station seed + campaign constraints.

Pipeline:

**campaign constraints → station identity → archetype → room requirements → topology → spatial packing → physical connector passages → doors → fixtures/details → systems/security → validation → presentation**

Current loose archetypes:

- Linear
- CentralHub
- Branching
- Ring
- AsymmetricIndustrial
- Compact
- Sprawling
- MultiSpine
- Retrofit

StationIdentity includes purpose, age, budget, size, crew capacity, industrial intensity, security level, maintenance condition and expansion history. Identity affects topology selection, corridor widths, room scale/grouping, industrial/habitat character, door resistance and deterministic interior detailing.

### Constraint/override layer

StationGenerationConstraints is the hard campaign layer. Procedural choices are soft preferences and never override it.

Supported hooks include:

- required/forbidden rooms and room-count bounds
- required adjacency/separation
- forced archetype/purpose/budget/size/expansion/security
- airlock count and named airlock placement
- turret/robot counts and room placement
- redundant routes required/forbidden
- required chokepoints
- reactor isolation / medical-near-habitat
- mandatory shutdown room
- initially accessible/inaccessible rooms
- environment/system overrides
- procedurally packed authored/set-piece rooms
- fully authored room geometry + connections

This supports fully procedural, constrained procedural, partially authored and fully authored missions through the same Facility/simulation model.

Do not add scenario-specific conditionals inside the generator when a constraint can express the requirement.

### Geometry invariants

Generated stations must preserve:

- real non-overlapping rooms
- real physical corridor rooms and connector passages
- doors only on actual shared boundaries
- usable passage cross-sections
- all geometry inside the station canvas
- functional-room area greater than circulation area
- structurally reachable required rooms unless explicitly authored otherwise
- canonical functional IDs such as control, engineering, reactor and isolation where existing simulation systems require them
- fixtures inside their owning room
- valid airlock/security placement
- deterministic human and robot navigation

Packing retries are deterministic and may reject a candidate station; validation must not be weakened merely to make a seed pass.

### Extension points

For new campaigns, prefer configuring ScenarioDefinition.StationConstraints.

Use AuthoredRooms for set pieces whose location may be packed procedurally. Use FullyAuthoredGeometry only for exceptional missions requiring exact geometry.

If a new simulation system needs a room role, either preserve an existing canonical ID or introduce an explicit semantic constraint/model contract; do not infer meaning from screen position.

Both UIs expose a compact GEN // seed inspector with same-seed/new-seed regeneration and generation diagnostics. Keep developer information behind this disclosure rather than adding permanent player clutter.

### Station presentation architecture

Primary presentation helper: `src/Overseer.Simulation/StationPresentationSystem.cs`.

- Both UIs derive the same station identity classes from authoritative `Facility` + `StationGenerationMetadata`, but **do not auto-fit the station back into the viewport**. V0.10D deliberately uses a large pannable virtual deck so physical rooms stay large at 100% zoom.
- `.station-map-camera` is a 3200×2800px virtual deck at 100% zoom. Authoritative station geometry lives inside `.station-authority-layer` at the central 80% (2560×2240px); generated functional rooms are clamped to at least 8% × 9%, which is ~205×202px at default zoom.
- `.station-world` keeps rooms, corridors, doors, fixtures, crew, robots and turrets in one coordinate system. Camera pan/zoom is presentation-only and must never alter simulation coordinates.
- Drag/WASD/arrow panning plus mouse-wheel/± zoom must reach the full virtual deck. Scroll while hovering the station zooms directly; Ctrl/Cmd is not required.
- Functional-room labels/status UI are **external callouts**, built by `StationRoomCalloutSystem`, placed only in the outer 10% deck margin and joined to rooms by leader lines. Callouts must not cover authoritative room/corridor geometry or one another. They always show full room name + power/temperature/O₂ health.
- Hull mass is drawn only from real room/corridor footprints. Do not reintroduce decorative rails/links that imply nonexistent navigation.
- `FacilitySeeder.ApplyIdentityDrivenDetails` adds deterministic room-aware fixtures. Wall equipment is bulkhead-aligned; floor equipment uses collision-aware work bays; all generated fixtures stay inside their owning room.
- V0.10D art direction is bright white/grey aerospace interiors with restrained green/orange/red status colour. Avoid neon cyan/blue, dark card-like rooms, global black/grey room fills, brown grime filters or UI labels painted over the physical room floor.
- Room-specific interiors should be visually rich and active: consoles/screens, vents, irrigation, pipes, medical equipment, cameras, airlocks, generator/reactor machinery etc. Operational animation is presentation-only and should remain subtle enough not to become visual noise.
- Browser and server `Home.razor` / `Home.razor.css` / `layout.js` must stay mirrored.
- Robots are selectable map entities with a dedicated Inspector state and explicit machine silhouette; do not render them as generic dots/cards.
- Desktop workspace is map-first. **Do not keep Facility Systems as a permanent primary-workspace panel.** Remove it or move non-contextual controls into a secondary utility surface so the station map + Inspector own the valuable screen area.
- The right-side **Inspector is the universal contextual surface for anything clickable**: crew, rooms, doors, robots, turrets/automated defences and future interactable station entities. Selection must show that entity's relevant status, state, goals/motivations where applicable, diagnostics and permitted controls.
- **Telemetry/debugging is not an Inspector tab beside the live station.** Move it to a separate full-screen debug view/route where the station map is not rendered. It must contain no player-critical information because normal release builds may hide/disable the debug view entirely.

Visual invariants: never invent hull/corridor/door geometry, never offset one physical entity separately for aesthetics, and never let decorative fixtures become simulation-authoritative unless the domain contract is explicitly extended.

---

## Implemented simulation

Current shared mechanics include:

- rooms/corridors/doors/fixtures and deterministic A* movement
- door lock/open/manual override/bypass/damage/repair/weld/barricade counterplay
- power, equipment wear/repair, life support and environmental propagation
- physical airlocks, pressure cycling and decompression
- hydroponics, provisions, cooking, eating and human routines
- autonomous crew with skills, traits, relationships, beliefs, memories and persistent intents
- suspicion/evidence, investigation, testimony and account comparison
- broadcasts/private messages interpreted as claims rather than truth
- MR-series autonomous robot behaviour and grounded crew countermeasures
- ST-series fixed turret behaviour with compartment/range/ammo/heat authority in deterministic C#
- five ordered campaign assignments, corporate directives, carry-over consequences and endings
- browser-local campaign persistence
- mirrored Pages/server station UI, resizable panels, large pannable deck camera, wheel/WASD/drag zoom/pan, audio/music, speech/thought bubbles and physical entity animation
- external room telemetry callouts with leader lines and no room/corridor overlap
- bright white/grey spacecraft interior art direction with animated consoles/screens/vents/irrigation/pipes/medical/camera/airlock/machinery cues
- selectable MR robots with dedicated Inspector telemetry; crew and friendly robots physically approach actual fixtures/equipment while working where an interaction point exists
- transient cognition diagnostics via `CognitionTelemetrySystem`; Ollama traces retain prompt/raw response/validated intent, browser/rule-based minds emit the same decision shape
- missing-person logic treats routine separation as normal: concern is measured in hours, Concerned-stage absence does not pre-empt work, and shared concern does not instantly interrupt the listener
- server/Ollama already generates fresh six-person rosters with 1–3 mechanically meaningful traits for new non-continuing sessions; browser Pages remains model-free

Useful subsystem anchors:

- src/Overseer.Simulation/ScenarioSystems.cs
- src/Overseer.Simulation/RobotSystem.cs
- src/Overseer.Simulation/RobotCountermeasureSystem.cs
- src/Overseer.Simulation/TurretSystem.cs
- src/Overseer.Simulation/TurretCountermeasureSystem.cs
- src/Overseer.Simulation/EnvironmentSystem.cs
- src/Overseer.Simulation/StationUpkeepSystem.cs
- src/Overseer.Simulation/CrewProvisioningSystem.cs
- src/Overseer.Simulation/BrowserMindSystem.cs
- src/Overseer.AI/NpcPromptBuilder.cs
- src/Overseer.AI/OllamaAiDecisionService.cs
- both GameSession.cs implementations
- both Home.razor / Home.razor.css implementations

---

## Persistence boundary

Implementation: src/Overseer.Persistence/CampaignStateSerializer.cs, format version 1, browser key all-systems-normal.campaign.v1.

Persist deliberate campaign continuity such as mission history, sponsor state, continuing crew identity/traits/skills, relationships, bounded important memories, credibility/suspicion, health/presence consequences, equipment condition and provisions.

Do not persist live movement, intents, jobs, investigations or cognition telemetry unless the save contract is explicitly redesigned.

---

## Mandatory Git/validation workflow

Always follow:

**main → new feature branch → implementation → tests/build/publish → PR → green CI → merge into main → verify post-merge GitHub Pages**

Do not leave completed green work in an open PR unless explicitly instructed.

Standard gate:

- dotnet build Overseer.slnx -c Release
- dotnet test tests/Overseer.Simulation.Tests/Overseer.Simulation.Tests.csproj -c Release --no-build
- dotnet publish src/Overseer.Web.Client/Overseer.Web.Client.csproj -c Release -o release --no-restore

.github/workflows/pages.yml runs these on PRs and deploys Pages from main.

---

## Next milestone — V0.10E Inspector/Debug Separation, Door Interaction + Broader Crew Agency

This is the immediate pass before malware work.

Required scope:

1. **Dedicated debug telemetry screen**
   - Move cognition/event/model telemetry to a separate full-screen debug route/view; do not render the station map there.
   - Debug telemetry must never be required to play the game and should be easy to hide/disable for normal release users.
   - Keep raw LLM prompts/responses, validated actions and simulation diagnostics there.

2. **Declutter Facility Systems**
   - Remove the permanent Facility Systems panel from the primary station workspace.
   - Put genuinely useful controls into the selected entity's Inspector where contextual.
   - Move any remaining global/diagnostic controls to secondary utility/debug UI rather than consuming map space.

3. **Universal contextual Inspector**
   - Clicking crew shows identity, role, traits/personality, needs, beliefs, current goal/intent, current physical action, relationships/evidence and location.
   - Clicking rooms shows environment, systems, fixtures, occupants, faults and available room controls.
   - Clicking doors shows open/closed/locked/damaged state, access/authority, adjacent rooms and available controls.
   - Clicking MR robots and ST turrets/automated defences shows operational state, policy, task/target, power/ammo/heat/link state and valid commands.
   - Use the same extensible selection model for future clickable entities; avoid one-off side panels.

4. **Physical sliding doors + crew door use**
   - Animate doors opening/closing as sci-fi sliding panels that retract out of the passage and slide back into place.
   - Door animation must reflect authoritative door state and never determine passability itself.
   - Humans should physically open an unlocked closed door when traversing it and close it again when appropriate rather than requiring the player to micromanage normal passage.
   - Initial access rule: ordinary crew may open/close unlocked doors; only suitably skilled/authorised roles (at minimum Engineer/Technician, with scenario/security overrides where appropriate) may lock/unlock doors. Keep deterministic C# authoritative over permissions, timing and state transitions.
   - Preserve existing damaged/manual/bypass/weld/barricade mechanics and revalidate passability at crossing time.

5. **Broader LLM-driven emergent agency**
   - Expand the high-level action/affordance vocabulary substantially beyond the current rigid intent menu so Ollama minds can originate more varied work, social, investigative, cooperative, deceptive, improvised and self-preservation goals.
   - Prefer capability/affordance descriptions in prompts over hardcoded scripts telling the model what to choose.
   - Deterministic C# still validates entity knowledge, targets, prerequisites, skills, routes, tools, duration, permissions and physical outcomes.
   - Preserve ongoing meaningful work from frivolous replanning, but allow surprising valid behaviour when the world state supports it.
   - Browser/Pages remains model-free and needs a deterministic fallback covering the same action contracts.

Regression coverage must include selection/Inspector routing, hidden/non-critical debug telemetry, crew door permissions + automatic traversal opening/closing, authoritative sliding-door states and expanded action validation.

---

## Subsequent milestone — V0.11 Contained Security-Network Malware & Crew Recovery

Build one narrow malware vertical slice around the existing MR/ST control links; do not create a broad hacking framework.

Required scope:

1. Explicit deterministic security-network compromise state, entry path and lifecycle.
2. Player issues only a high-level malware action; C# validates network reachability and affected assets.
3. Initial infection scope is MR/ST control links only and cannot bypass physical combat/range rules.
4. Compromise/recovery creates observer-local evidence and diagnostics.
5. Humans can detect, physically isolate and purge/reimage the controller with skill/time requirements.
6. Browser fallback and Ollama cognition receive grounded diagnostics and choose high-level responses only.
7. Mirror state/controls in both UIs.
8. Cover authority, reachability, containment, recovery, evidence and unchanged physical combat with regression tests.

Keep deferred: station-wide self-propagation, malware families, direct malware damage, self-destruct mechanics or lethal outcomes delegated to an LLM.

---

## Deferred gameplay candidates after V0.11

Keep these as deliberate follow-on designs rather than slipping them into unrelated passes:

- **Scenario roster policy:** Ollama crew generation already produces unique names/personality/skills/1–3 mechanical traits. Add an explicit scenario choice between fresh generated crew and campaign-continuing crew; Pages should use seeded deterministic roster variation rather than a model call.
- **Hazardous transport assignments:** support missions carrying a hardened prisoner, hostile organism or other contained threat. Model containment/protocols/escape state deterministically, let crew form grounded responses, and allow Overseer to help or hinder survival without delegating combat, escape success or lethality to an LLM.
- **Diagnostics expansion:** cognition telemetry should eventually cover every model-backed interaction type (crew generation, message interpretation and future planners), while remaining bounded/transient by default.

## Handoff prompt rule

Future chat prompts should be short: tell the next agent to read this file, inspect current main/recent PR/CI/Pages state, implement the documented next milestone, follow the mandatory workflow, and update this file.

Put technical detail here, not in the chat handoff prompt.
