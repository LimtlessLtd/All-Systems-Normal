# All Systems Normal — Project Handoff

Repository: https://github.com/LimtlessLtd/All-Systems-Normal
Playable Pages build: https://limtlessltd.github.io/All-Systems-Normal/

**Current state:** V0.12 + Movement, Perception, Medical & Interaction Polish
**Next recommended milestone:** V0.13 — Hazardous Transport Assignments

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
- Door.IsPassable is crossing truth; crossings revalidate live door state. Crew route planning (`FindPathForCrew`) also plans through closed, unlocked, powered hatches via `CrewDoorInteractionSystem.CanTraverseWhenReached`, because crew open those at the portal. Robots plan with `IsPassable` only.
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
- Functional-room status UI is built by `StationRoomCalloutSystem` and attaches directly to the selected top/bottom room edge. Keep it outside the physical floor, deterministic, and visually tied to its owning room; it shows full room name + power/temperature/O₂ health.
- Hull mass is drawn only from real room/corridor footprints. Do not reintroduce decorative rails/links that imply nonexistent navigation.
- `FacilitySeeder.ApplyIdentityDrivenDetails` adds deterministic room-aware fixtures. Wall equipment is bulkhead-aligned; floor equipment uses collision-aware work bays; all generated fixtures stay inside their owning room.
- Current art direction uses bright white/grey aerospace hulls, walls and machinery around a dark black tiled deck floor in every functional room, with restrained green/orange/red status colour. Room hover/selection must never replace or hide the tiled floor. Avoid neon cyan/blue, brown grime filters or UI labels painted over the physical room floor.
- Room-specific interiors should be visually rich and active: consoles/screens, vents, irrigation, pipes, medical equipment, cameras, airlocks, generator/reactor machinery etc. Repeating presentation animations must use closed/seamless cycles without visible snap-back.
- Browser and server `Home.razor` / `Home.razor.css` / `layout.js` must stay mirrored.
- Robots are selectable map entities with a dedicated Inspector state and explicit **top-down** machine silhouette consistent with the crew camera angle; do not render them as generic dots/cards.
- Desktop workspace is map-first. The default view is the station focus view (map + Inspector) whose toolbar carries mission clock, alerts, pause, speed, zoom/FIT and the CREW / LOG / MESSAGES / OBJECTIVES / MENU overlays; CONSOLE switches to the full console, where Overseer Comms sits full-width directly above the station workspace beneath mission/corporate objectives. The choice is remembered per browser. Pause is a button and the Space key; picking a speed resumes. RESET RUN always asks for confirmation. The player LOG uses `StationLogPresentation` to omit routine movement. **Do not keep Facility Systems as a permanent primary-workspace panel.** Remove it or move non-contextual controls into a secondary utility surface so the station map + Inspector own the valuable screen area.
- The right-side **Inspector is the universal contextual surface for anything clickable**: crew, rooms, doors, robots, turrets/automated defences and future interactable station entities. Selection must show that entity's relevant status, state, goals/motivations where applicable, diagnostics and permitted controls.
- **Telemetry/debugging is not an Inspector tab beside the live station.** Move it to a separate full-screen debug view/route where the station map is not rendered. It must contain no player-critical information because normal release builds may hide/disable the debug view entirely.

Visual invariants: never invent hull/corridor/door geometry, never offset one physical entity separately for aesthetics, and never let decorative fixtures become simulation-authoritative unless the domain contract is explicitly extended. Interactive station buttons must never receive generic `:active` transforms because rooms/fixtures/crew/robots use transforms for authoritative map positioning.

---

## Implemented simulation

Current shared mechanics include:

- rooms/corridors/doors/fixtures, deterministic strategic A* routing and collision-aware local movement around physical fixtures
- door lock/open/manual override/bypass/damage/repair/weld/barricade counterplay
- physical electrical/mechanical infrastructure: reactor/generator output, distribution bus efficiency, capacitor energy buffering, machine loads, load shedding, powered door actuators, coolant pumps, oxygen generation, CO₂ scrubbing, water recycling and control-network camera reachability
- deterministic equipment wear/repair plus rare seeded unexpected fault events; qualified crew physically travel to and service degraded machinery
- life support and environmental propagation depend on the actual powered utility chain rather than a standalone boolean
- physical airlocks, pressure cycling and decompression
- hydroponics, provisions, cooking, eating and human routines
- autonomous crew with skills, traits, relationships, beliefs, memories and persistent intents
- memory fades: `MemorySalience` scores importance × a half-life that grows with importance (trivia fades in hours, defining moments last about a day); prompts use the most salient memories now, and `MemoryRetentionSystem` caps each crew member at 40 memories every 30 minutes and forgets faded trivia older than a day. Campaign carry-over still keeps the most important memories.
- conversations carry content via `ConversationTopicSystem`: doubts about Overseer, gossip about a third crew member (nudges the listener's view of them by trust; friends of the subject push back), passing on recent notable memories (never news about the listener), wellbeing and small talk. Arguments name a cause (Overseer disagreement, grievance). Informative talk leaves listener memories and appears in the player LOG; Overseer beliefs remain evidence-driven. Social rolls include the station seed and pairing order rotates.
- suspicion/evidence, investigation, testimony and account comparison
- broadcasts/private messages interpreted as claims rather than truth
- MR-series autonomous robot behaviour and grounded crew countermeasures
- ST-series fixed turret behaviour with compartment/range/ammo/heat authority in deterministic C#
- contained MR/ST security-controller malware lifecycle with deterministic reachability, observer-local diagnostics, physical isolation and timed purge/reimage recovery
- five ordered campaign assignments, corporate directives, carry-over consequences and endings
- browser-local campaign persistence
- mirrored Pages/server station UI, resizable panels, large pannable deck camera, wheel/WASD/drag zoom/pan, audio/music, speech/thought bubbles, seamless physical entity animation and dotted green next-segment crew movement intent
- room telemetry attaches directly to the owning room's top/bottom edge rather than floating elsewhere
- bright white/grey spacecraft interior art direction with animated consoles/screens/vents/irrigation/pipes/medical/camera/airlock/machinery cues
- one shared `StationSelection` / `StationInspectionSystem` contract drives the contextual Inspector for rooms, crew, doors, MR robots and ST turrets
- the default workspace is the station focus view with pause, crew roster, notable-event log and menu overlays; CONSOLE shows objectives/directives + full-width Overseer Comms + map/Inspector; the old permanent Facility Systems panel is removed
- map camera panning never pointer-captures on pointerdown; capture begins only once a press moves past the pan threshold, so plain clicks on `[data-station-interactive]` entities still reach them while drags that start on rooms/corridors still pan (and do not select); this is regression-tested in both runtimes
- rooms, crew, doors, MR robots, ST turrets, physical machinery and key overview status readouts all route through the same Inspector
- maintainable machinery is bound to physical room fixtures via `RoomFixture.DeviceId`; door entities carry visible local control pads without cluttering corridor fixture geometry
- `/debug` is a separate full-screen non-gameplay diagnostics surface; it contains cognition traces, raw Ollama prompt/response data, event logs and generation diagnostics and can be disabled without changing simulation authority
- authoritative sliding doors animate from `Door.IsOpen`; leaves retract fully clear of the walking line, mobile entities render above the hatch plane, ordinary crew automatically open traversable closed/unlocked hatches and they auto-close after traffic, while deterministic role/skill rules gate lock/unlock
- the station exterior uses a dark-space fallback plus NASA/ESA/CSA/STScI JWST SMACS 0723 imagery; bright white/grey styling is reserved for the spacecraft itself
- `CrewAffordanceSystem` is the shared capability catalog for Ollama and browser fallback; deterministic systems still validate knowledge, targets, routes, skills, permissions and outcomes
- expanded grounded crew agency includes cooperative, investigative, deceptive, safety and local door intentions; deception never directly edits another NPC's beliefs
- selectable MR robots with dedicated Inspector telemetry; crew and friendly robots physically approach actual fixtures/equipment while working where an interaction point exists
- transient cognition diagnostics via `CognitionTelemetrySystem`; Ollama traces retain prompt/raw response/validated intent, browser/rule-based minds emit the same decision shape
- missing-person logic treats routine separation as normal: concern is measured in hours, Concerned-stage absence does not pre-empt work, and shared concern does not instantly interrupt the listener
- scenario roster provenance is explicit: fresh scenarios use Ollama/server generation or deterministic seeded Pages generation; continuing scenarios reconstruct persisted campaign crew and never silently substitute a new roster
- deterministic perception uses human forward-cone/open-door LOS and omnidirectional longer-range machine sensors; hostile assets cannot magically acquire unseen targets
- medical treatment/resurrection is simulation-authoritative and resource/power/body gated; blood evidence persists physically until a capable actor cleans it
- survival intents persist through real station traversal; local fixture avoidance must never stall an actor on a zero-length waypoint

Useful subsystem anchors:

- src/Overseer.Simulation/ScenarioSystems.cs
- src/Overseer.Simulation/SeededCrewRosterGenerator.cs
- src/Overseer.Simulation/CampaignProgressionSystem.cs
- src/Overseer.Simulation/RobotSystem.cs
- src/Overseer.Simulation/RobotCountermeasureSystem.cs
- src/Overseer.Simulation/TurretSystem.cs
- src/Overseer.Simulation/TurretCountermeasureSystem.cs
- src/Overseer.Domain/SecurityMalware.cs
- src/Overseer.Simulation/SecurityMalwareSystem.cs
- src/Overseer.Simulation/EnvironmentSystem.cs
- src/Overseer.Simulation/StationUpkeepSystem.cs
- src/Overseer.Simulation/StationDeviceControlSystem.cs
- src/Overseer.Simulation/StationInteractionSystems.cs
- src/Overseer.Simulation/CrewProvisioningSystem.cs
- src/Overseer.Simulation/LocalMovementSystem.cs
- src/Overseer.Simulation/PerceptionSystem.cs
- src/Overseer.Simulation/MedicalSystem.cs
- src/Overseer.Simulation/MedicalEvidenceSystem.cs
- src/Overseer.Simulation/IntentExecutionSystem.cs
- src/Overseer.Simulation/BrowserMindSystem.cs
- src/Overseer.AI/NpcPromptBuilder.cs
- src/Overseer.AI/RuleBasedAiDecisionService.cs
- src/Overseer.AI/OllamaAiDecisionService.cs
- both GameSession.cs implementations
- both Home.razor / Home.razor.css implementations

---

## Persistence boundary

Implementation: src/Overseer.Persistence/CampaignStateSerializer.cs, format version 1, browser key all-systems-normal.campaign.v1.

Persist deliberate campaign continuity such as mission history, corporate directive state, continuing crew identity/traits/skills, relationships, bounded important memories, credibility/suspicion, health/presence consequences, equipment condition and provisions.

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

## Current implementation contracts

- Universal Inspector, station-map click routing, physical machinery/device bindings, sliding doors, power/life-support dependencies, malware recovery, roster policy and campaign persistence remain authoritative shared systems.
- Pages remains model/credential-free; server/Ollama and browser surfaces must preserve simulation parity. Mirrored station UI files stay synchronized.
- `/debug` is a separate diagnostics surface and may contain cognition traces plus raw LLM request/response data; no player-critical control may depend on it.
- Fixture collision is authoritative for mobile entities. Local navigation stays freeform visually but uses deterministic collision-aware routing internally. The current visibility-graph safety margin is deliberately small (`0.35`) because physical clearance is already enforced separately; fallbacks must never reselect the actor's current waypoint.
- The seed-1 Storage regression (`Marcus Reed` at the formerly stuck door approach) protects the 24-hour provisioning/maintenance lifecycle from fixture-routing starvation regressions. Do not weaken the lifecycle assertions.
- Human movement uses continuous local-motion state, faster door approach/traversal, contextual hand/arm animation only during actual hands-on actions, and correctly centred robot selection affordances.
- Room status strips attach to the owning room's top/bottom edge. Reactor air-handler visuals must remain inside their machinery footprint. Station Overview exposes simulation speed controls.
- Human LOS is directional and door/geometry aware; robot/turret sensor LOS is deterministic and cannot acquire targets through walls. Previously acquired hostile targets may continue to be pursued under the existing deterministic rules.
- Medical care, resurrection and blood evidence are deterministic C# systems. Resurrection requires a powered medbay, resources/charge and a present recoverable body.
- The procedure in progress lives in `Npc.MedicalActionKind` (never read back from `CurrentAction`, which other systems rewrite). A doctor mid-procedure and a patient waiting in a medbay that can treat them are protected from routine errands; leaving the medbay abandons the procedure. A patient waiting in the medbay calls the doctor in as medical duty. Injured crew are only routed to the medbay when a doctor, supplies and a safe medbay exist; an existing trip is kept rather than recreated, and plans with urgency ≥ 97 are never overridden. Witnesses rethink once per injury (`Npc.NoticedInjuredCrewIds`), not every minute.
- Movement/perception/medical regression coverage is concentrated in `MovementPerceptionMedicalPolishTests.cs` plus the existing lifecycle, robot, UI and procedural-generation suites.

## Next milestone — V0.13 Hazardous Transport Assignments

Add scenario-defined missions carrying a hardened prisoner, hostile organism or other contained threat. Keep containment, protocols, escape state, combat, damage and lethality deterministic; crew cognition may decide responses, and Overseer may help or hinder those responses without owning physical outcomes.

Keep unrelated simulation expansion out of this pass.

---

## Deferred after V0.13

- **Diagnostics expansion:** cognition telemetry should eventually cover every model-backed interaction type (crew generation, message interpretation and future planners), while remaining bounded/transient by default.

## Handoff prompt rule

Future chat prompts should be short: tell the next agent to read this file, inspect current main/recent PR/CI/Pages state, implement the documented next milestone, follow the mandatory workflow, and update this file.

Put technical detail here, not in the chat handoff prompt.
