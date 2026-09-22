# All Systems Normal — Project Handoff

Repository: https://github.com/LimtlessLtd/All-Systems-Normal
Playable Pages build: https://limtlessltd.github.io/All-Systems-Normal/

**Current state:** V0.13 — Complete Hazardous Transport Assignments shipped (Containment Transfer is a standalone win/lose assignment with deterministic physical hatch escape/recapture/combat/lethality)
**Next recommended milestone:** pick up a V0.13 follow-up below, or an item from "Known issues and audit follow-ups"

`PROJECT_HANDOFF.md` is the **single authoritative source** for repository architecture, invariants, roadmap, priorities and developer handoff state. Other documents may provide historical or explanatory context only; they must not define competing requirements or future-work plans. If another document conflicts with this file, this file wins. Keep it concise, update sections in place and do not append milestone diaries.

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
- Overseer.Web.UI — the one station console (Razor Class Library): `Pages/Home.razor(.css)`, `Pages/Debug.razor(.css)`, `wwwroot/layout.js`, `wwwroot/audio.js`
- Overseer.Web — server/Ollama host
- Overseer.Web.Client — deterministic static Pages host
- Overseer.Simulation.Tests — regression contract

Shared mechanics belong in Domain/Simulation and must not be independently reimplemented in each UI.

One UI, two hosts. `StationSession` (Overseer.Simulation) owns the station, the tick pipeline and every operator verb; each host's `GameSession` subclass supplies only crew creation and the think step (Ollama on the server, `BrowserMindSystem` on Pages). Pages inject `StationSession` and carry no `@rendermode` — the server applies `InteractiveServer` globally on `<Routes>` (with `/Error` excluded) and registers the library with `AddAdditionalAssemblies` on both `MapRazorComponents` and its router; the client router does the same. Scripts load from `_content/Overseer.Web.UI/`. Never add a host-local copy of a console page or script (`StationSessionTests` guards this).

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
- Functional-room status UI is built by `StationRoomCalloutSystem` and attaches directly to the selected top/bottom room edge. Keep it outside the physical floor, deterministic, and visually tied to its owning room; it shows full room name + power/temperature/O₂ health. Each label is exactly as wide as its room (`LabelWidth`, capped at ~260px), applied inline with `box-sizing: border-box`, and a collision pass moves a label to its room's other edge if two still meet in a shared gap — so labels never overlap.
- Hull mass is drawn only from real room/corridor footprints. Do not reintroduce decorative rails/links that imply nonexistent navigation.
- `FacilitySeeder.ApplyIdentityDrivenDetails` adds deterministic room-aware fixtures. Wall equipment is bulkhead-aligned; floor equipment uses collision-aware work bays; all generated fixtures stay inside their owning room.
- Current art direction uses bright white/grey aerospace hulls, walls and machinery around a dark black tiled deck floor in every functional room, with restrained green/orange/red status colour. Room hover/selection must never replace or hide the tiled floor. Avoid neon cyan/blue, brown grime filters or UI labels painted over the physical room floor.
- Room-specific interiors should be visually rich and active: consoles/screens, vents, irrigation, pipes, medical equipment, cameras, airlocks, generator/reactor machinery etc. Repeating presentation animations must use closed/seamless cycles without visible snap-back.
- Both runtimes render the same `src/Overseer.Web.UI` console; change it once.
- The station header lets its controls wrap onto further rows beside the title; right-aligned controls must never sit on a non-wrapping line (they slide over the title when they do not fit).
- `Home.razor.css` was pruned of dead and overridden rules only (computed styles verified identical). When restyling, edit the existing rule instead of appending another override layer at the end of the file.
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
- hydroponics with typed visible crops (tomato, potato, apple, grape, banana, tobacco, wheat), provisions, cooked meals, raw-food fallback, food preferences and mood/stress consequences
- deterministic daily routines with day/night shifts, scheduled sleep, sleep debt, fatigue-driven movement slowdown and cognitive skill penalties
- fresh scenarios scale to roughly 12 crew while preserving campaign-continuing roster provenance; Ollama may generate the six specialist roles first and deterministic supplementation fills the larger roster, while `Prisoner` is scenario-composed and never part of the generated-role contract
- spontaneous social conflict pressure can escalate into deterministic fights from stress, personality, relationships, grievances and circumstances
- deterministic fire/smoke hazards expose composable crew affordances (fight fire, evacuate, seal, vent) rather than scripted response trees; LLM/browser cognition chooses desired responses and C# validates reachability, equipment, pressure and outcomes
- prisoner/containment: prisoner roles, danger levels, violence bias and secure containment rooms; `containment-transfer` is a complete standalone assignment (see V0.13 section below) with deterministic escape opportunity/pressure, recapture, prisoner-guard combat/lethality and a mandatory chain-of-custody directive
- autonomous crew with skills, traits, relationships, beliefs, memories and persistent intents; every fresh roster (demo, seeded-browser or Ollama-generated) starts with deterministic relationship texture rather than a flat 50/50 — `FacilitySeeder.InitialBond` hashes each unordered name pair to seed a small, reproducible slice of rivalries and close bonds (with per-direction jitter so a bond need not be perfectly symmetric) before any explicit demo overrides are layered on
- memory fades: `MemorySalience` scores importance × a half-life that grows with importance (trivia fades in hours, defining moments last about a day); prompts use the most salient memories now, and `MemoryRetentionSystem` caps each crew member at 40 memories every 30 minutes and forgets faded trivia older than a day. Campaign carry-over still keeps the most important memories.
- conversations carry content via `ConversationTopicSystem`: doubts about Overseer, gossip about a third crew member (nudges the listener's view of them by trust; friends of the subject push back), passing on recent notable memories (never news about the listener), wellbeing and small talk. Arguments name a cause (Overseer disagreement, grievance). Informative talk leaves listener memories and appears in the player LOG; Overseer beliefs remain evidence-driven. Social rolls include the station seed and pairing order rotates.
- suspicion/evidence, investigation, testimony and account comparison
- broadcasts/private messages interpreted as claims rather than truth
- MR-series autonomous robot behaviour and grounded crew countermeasures
- ST-series fixed turret behaviour with compartment/range/ammo/heat authority in deterministic C#
- contained MR/ST security-controller malware lifecycle with deterministic reachability, observer-local diagnostics, physical isolation and timed purge/reimage recovery
- five ordered campaign assignments, corporate directives, carry-over consequences and endings
- browser-local campaign persistence
- one shared Pages/server station UI, resizable panels, large pannable deck camera, wheel/WASD/drag zoom/pan, audio/music, speech/thought bubbles, seamless physical entity animation and selected-unit destination/route visualization
- room telemetry attaches directly to a generation-owned top/bottom edge reservation; procedural packing treats the status-plate strip as occupied geometry so plates cannot overlap rooms, corridors or each other
- bright white/grey spacecraft interior art direction with animated consoles/screens/vents/irrigation/pipes/medical/camera/airlock/machinery cues
- one shared `StationSelection` / `StationInspectionSystem` contract drives the contextual Inspector for rooms, crew, doors, MR robots and ST turrets
- the default workspace is the station focus view with pause, crew roster, notable-event log and menu overlays; CONSOLE shows objectives/directives + full-width Overseer Comms + map/Inspector; the old permanent Facility Systems panel is removed
- map camera panning never pointer-captures on pointerdown; capture begins only once a press moves past the pan threshold, so plain clicks on `[data-station-interactive]` entities still reach them while drags that start on rooms/corridors still pan (and do not select); this is regression-tested in the shared console
- rooms, crew, doors, MR robots, ST turrets, physical machinery and key overview status readouts all route through the same Inspector
- maintainable machinery is bound to physical room fixtures via `RoomFixture.DeviceId`; door entities carry visible local control pads without cluttering corridor fixture geometry
- `/debug` is a separate full-screen non-gameplay diagnostics surface; it contains cognition traces, raw Ollama prompt/response data, event logs and generation diagnostics and can be disabled without changing simulation authority
- authoritative sliding doors animate from `Door.IsOpen`; leaves retract fully clear of the walking line, mobile entities render above the hatch plane, ordinary crew automatically open traversable closed/unlocked hatches and they auto-close after traffic, while deterministic role/skill rules gate lock/unlock
- the station exterior uses a darker astronomical backdrop with restrained animated glow/twinkle; bright white/grey styling is reserved for the spacecraft itself
- `CrewAffordanceSystem` is the shared capability catalog for Ollama and browser fallback; deterministic systems still validate knowledge, targets, routes, skills, permissions and outcomes
- expanded grounded crew agency includes cooperative, investigative, deceptive, safety and local door intentions; deception never directly edits another NPC's beliefs
- selectable MR robots with dedicated Inspector telemetry; crew and friendly robots physically approach actual fixtures/equipment while working where an interaction point exists
- transient cognition diagnostics via `CognitionTelemetrySystem`; Ollama traces retain prompt/raw response/validated intent, browser/rule-based minds emit the same decision shape
- missing-person logic treats routine separation as normal: concern is measured in hours, Concerned-stage absence does not pre-empt work, and shared concern does not instantly interrupt the listener
- CrewLifecycleAuditSystem guarantees crew death/removal transitions are logged and bodies/presence state remain explainable instead of silently disappearing
- Station alerts retain recent actionable history; hover shows the last five and clicking an alert selects/navigates to its related crew member, room, robot, turret or device
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
- src/Overseer.Simulation/PrisonerContainmentSystem.cs
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
- src/Overseer.Simulation/StationSession.cs (shared session; runtime `GameSession` subclasses in each host)
- src/Overseer.Web.UI/Pages/Home.razor / Home.razor.css

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

.github/workflows/pages.yml runs these on PRs and deploys Pages from main. Its concurrency group is scoped per-ref (`pages-${{ github.ref }}`) so that a PR branch's CI runs never cancel another PR's or main's in-progress run — with several agents pushing concurrently, a shared unscoped group previously let a PR push cancel the production deploy mid-flight. Keep it scoped per-ref.

---

## Current implementation contracts

- Universal Inspector, station-map click routing, physical machinery/device bindings, sliding doors, power/life-support dependencies, malware recovery, roster policy and campaign persistence remain authoritative shared systems.
- Pages remains model/credential-free; server/Ollama and browser surfaces must preserve simulation parity. Both run the same `StationSession` pipeline and the same `Overseer.Web.UI` console.
- `/debug` is a separate diagnostics surface and may contain cognition traces plus raw LLM request/response data; no player-critical control may depend on it.
- Fixture collision is authoritative for mobile entities. Local navigation stays freeform visually but uses deterministic collision-aware routing internally. Fixture layout reserves the inward approach to every real door portal, wall-mounted equipment interaction points are chosen on the room side, and local routing falls back to a deterministic occupancy-grid detour if the visibility graph dead-ends. The current visibility-graph safety margin is deliberately small (`0.35`) because physical clearance is already enforced separately; fallbacks must never reselect the actor's current waypoint.
- The seed-1 Storage regression (`Marcus Reed` at the formerly stuck door approach) protects the 24-hour provisioning/maintenance lifecycle from fixture-routing starvation regressions. Do not weaken the lifecycle assertions.
- Human movement uses continuous local-motion state, faster door approach/traversal, contextual hand/arm animation only during actual hands-on actions, and correctly centred robot selection affordances.
- Room status strips use `Room.StatusPlateSide` chosen during generation; `StationGenerator` reserves and validates their external envelopes against all station geometry and other plates. Normal generation prefers larger organised rooms but must retain deterministic late-attempt packing fallback for cramped seeds; crowded-seed retry budget may increase, but geometry validation must never be weakened. Reactor air-handler visuals must remain inside their machinery footprint. Station Overview exposes simulation speed controls.
- Human LOS is directional and door/geometry aware, and light-dependent: if either end of the sightline is unpowered or unlit, human range drops to 35% (sightings, blood evidence and violence attribution all follow). Witnesses of violence in the same compartment identify the attacker by distance and light rather than facing; otherwise they only hear a struggle. Robot/turret sensor LOS is deterministic, light-independent and cannot acquire targets through walls. Previously acquired hostile targets may continue to be pursued under the existing deterministic rules.
- Medical care, resurrection and blood evidence are deterministic C# systems. Resurrection requires a powered medbay, resources/charge and a present recoverable body.
- The procedure in progress lives in `Npc.MedicalActionKind` (never read back from `CurrentAction`, which other systems rewrite). A doctor mid-procedure and a patient waiting in a medbay that can treat them are protected from routine errands; leaving the medbay abandons the procedure. A patient waiting in the medbay calls the doctor in as medical duty. Injured crew are only routed to the medbay when a doctor, supplies and a safe medbay exist; an existing trip is kept rather than recreated, and plans with urgency ≥ 97 are never overridden. Witnesses rethink once per injury (`Npc.NoticedInjuredCrewIds`), not every minute.
- Movement/perception/medical regression coverage is concentrated in `MovementPerceptionMedicalPolishTests.cs`; emergent routines/hazards/containment coverage is in `EmergentWorldSystemsTests.cs` plus the existing lifecycle, robot, UI and procedural-generation suites.
- LLM freedom direction: keep expanding deterministic affordances and world-state observability rather than scripted plans; converge browser/server fallback cognition onto one utility scorer; let the model compose multi-step intentions from atomic actions; record failed intentions/frustration as memories; replace omniscient target locations with last-seen/search knowledge; allow hazard-response coordination via shared claims/messages while C# remains sole authority over physics, access, resources, damage and death.

## Deferred emergent-agency architecture

This section is the authoritative replacement for the former `docs/EMERGENT_AGENCY_PLAN.md`. It is deliberately deferred behind the current V0.13 milestone unless the owner explicitly reprioritises it.

Goal: widen NPC/LLM freedom through composable world affordances, not a hard-coded decision tree. The core authority rule remains unchanged: the mind proposes intent; deterministic C# validates capability, applies physics/resources/access/skills and resolves outcomes.

Required architecture:

- Preserve both **rule determinism** (model output never mutates state directly) and **replay determinism** (station seed + recorded mind decisions can reproduce a run).
- Planned sequence: shared deterministic roll + decision journal/state hash → rejection feedback → generic tag interaction engine → interaction/hazard content → bounded plans/triggers/goal predicates → structured claims/pacts → shared utility scoring and last-seen/search cognition → headless replay/metrics.
- A future generic interaction verb may compose methods such as strike/pry/cut/heat/cool/rewire/overload/drain/spill/tinker/salvage against deterministic fixture/device/context tags. The LLM chooses the desired interaction; C# owns target validity, matching rules, skill/resource checks and weighted deterministic outcomes.
- Interaction consequences must flow through a closed/shared effect layer rather than bespoke per-rule mutation code. Ambient world reactions may use the same deterministic rule mechanism so player actions such as power, ventilation, doors and atmosphere naturally change outcomes.
- Minds may see observable affordances/context and remembered outcomes, but never hidden rule tables or outcome probabilities.
- New hazards must remain authoritative world state with deterministic decay/spread and real interaction with doors, atmosphere, ventilation, power and existing damage systems. Catastrophic/station-loss outcomes must require escalation and compound preconditions rather than a single healthy-station action.
- Failed/rejected intentions should become bounded feedback/memories rather than silently collapsing to idle. `IntentExecutionSystem.FailIntent` now records the failure as a `Memory` and a small stress bump (see "Known issues" below), but nothing yet feeds that memory back into cognition prompts/options to actually stop a mind repeating the same rejected choice — that feedback loop is still deferred. Browser/server fallback cognition should converge on one shared utility model, while Ollama remains free to choose a different valid action.
- Tests for this program must cover deterministic replay/state hashing, catalog/rule integrity, representative cascade chains, ambient reactions to player verbs and the existing authority invariant. Never weaken existing simulation tests to make new content pass.

---

## V0.13 — Complete Hazardous Transport Assignments (shipped)

`containment-transfer` is now a complete, player-reachable, deterministic win/lose assignment, not just a roster/geometry foundation.

- **Deliberately standalone, not inserted into the 5-mission campaign order.** `ScenarioCatalog.Campaign` (the ordered arc with continuity/reveal/ending state) is unchanged at 5 entries. `ScenarioCatalog.StandaloneAssignments = [ContainmentTransfer]` is a new, separate list for complete assignments that run on their own fresh station/roster and never touch campaign continuity, reveal stage or the endgame gate. `ScenarioCatalog.Find` resolves both lists. Inserting it into `Campaign` was considered and rejected: that scenario's `FreshGenerated` roster policy and dedicated `AsymmetricIndustrial`/Security station constraints are incompatible with the continuing-crew/continuity-carry-over model the other five missions and the reveal/ending arc assume.
- **Player entry point:** `StationSession.LoadStandaloneScenarioAsync(scenarioId)` (new abstract member, implemented on both hosts) starts a `StandaloneAssignments` scenario on a fresh station/crew without touching `Campaign`/`CaptureCompletedMission`/carry-over. Wired to a confirm-guarded "SPECIAL ASSIGNMENT: CONTAINMENT TRANSFER" button in the console MENU overlay (`Home.razor`), mirroring the existing "RESET RUN" confirm pattern. Verified end-to-end in a live browser (menu → confirm → fresh containment station with all 4 prisoners on the crew manifest and the directive board showing MAINTAIN CHAIN OF CUSTODY).
- **Escape/recapture/combat is deterministic in `PrisonerContainmentSystem`.** Escape pressure (danger tier + `PrisonerViolenceBias` + stress) only creates an attempt when a real unsecured/passable containment hatch exists. The attempt uses ordinary `ActionResolver`/`NpcMovement`; `LocalMovementSystem` physically approaches the hatch and revalidates its live state at the portal, and only a successful crossing sets `Npc.HasEscapedContainment`. `Npc.IsContainmentBreachInProgress` is transient authority for the committed crossing and prevents browser/server cognition from cancelling it. Locking, welding, barricading or otherwise making the hatch impassable before crossing stops the escape without changing authoritative containment. Once at large, `CrewRoutineSystem` supplies the deterministic farthest-reachable-room fallback. `RecapturePrisoner` remains an ordinary crew affordance chosen by minds, while deterministic C# resolves restraint, injury and lethality. `Attack` is still rewritten to `Argue` and is not a mind-selectable shortcut.
- **Win/lose gate:** a new mandatory `DirectiveKind.ContainmentIntegrity` directive (`CorporateDirectiveSystem`) fails the scenario immediately on any prisoner death, and is graded at the observation-window deadline on whether every prisoner is still secure (recapturing an escapee before the deadline keeps it alive). The pre-existing `KeepCrewAlive` objective/`Apply` target computation and `ScenarioProgressSystem`'s living-crew count were both fixed to exclude prisoners (`!npc.IsPrisoner`), so a prisoner casualty no longer masks a real crew-alive failure or vice versa — this is a small, generally-applicable correctness fix, not containment-specific behaviour, and does not change any of the other five missions (none have prisoners).
- Coverage: `tests/Overseer.Simulation.Tests/PrisonerTransportTests.cs` pins standalone catalog membership, physical hatch crossing, authoritative room state during a breach attempt, cognition non-interruption, mid-attempt sealing counterplay, secured-hatch prevention, recapture/lethal struggles, directive outcomes and at-large alerts.

**Deliberately deferred out of this pass** (real backlog, not silently dropped):

- Richer per-prisoner goals/relationships/backstory. Prisoners still get only the four `PrisonerDefinition` fields; they now receive the same deterministic starting relationship texture as every fresh roster, but there are no prisoner-specific authored bonds, goals or backstory. Escape/flee/recapture behaviour is fully deterministic C#, not mind-authored motive.
- `CrewContinuitySnapshot`/`CampaignStateSerializer` still do not carry `IsPrisoner`/`PrisonerDangerLevel`/`PrisonerViolenceBias`. Not exercised today (the assignment is always `FreshGenerated`), but would need fixing before any future scenario reuses `CampaignContinuing` roster policy with prisoners.
- Dedicated containment activity presentation now projects existing deterministic state in the shared console: prisoner map tokens/Inspector cards distinguish `BREACH IN PROGRESS`, `AT LARGE` and secure custody, while crew pursuing or restraining an escapee show `RECAPTURE`; this is presentation-only and does not alter containment authority.

---

## Deferred after V0.13

- **Diagnostics expansion:** cognition telemetry should eventually cover every model-backed interaction type (crew generation, message interpretation and future planners), while remaining bounded/transient by default.

## Known issues and audit follow-ups (audit of 2026-09-21, after PR #62)

A code/behaviour/UI audit was run and its fixes merged in PRs #52–#62. These items were found but **not** addressed. They are backlog, not the next milestone: pick them up when they block a milestone or when explicitly asked. Each was re-checked against `d70647b`.

**Deliberate decisions (do not "fix"):**

- The server tick awaits the Ollama decision (`StationSession.AdvanceCoreAsync` → `GameSession.ThinkAsync`), so the station pauses while a mind thinks. The owner wants this: the model gets time to take in the situation and the station waits for its decision. Do not make cognition non-blocking unless asked.
- Closing an ordinary door no longer counts as "denying access" for corporate directives; the player must lock, weld or barricade it, or cut power (PR #52).

**Server runtime:**

- ~~On first load the server generates the crew twice~~ fixed: `Components/App.razor`'s `PageRenderMode` now disables prerender (`new InteractiveServerRenderMode(prerender: false)`), so only the real interactive circuit runs `GameSession.InitializeAsync`. Guarded by `StationSessionTests.ServerHostDoesNotPrerenderTheInteractiveRoute`. Note the tradeoff: the server now sends no prerendered HTML, so first paint is blank until the SignalR circuit connects and finishes crew generation.
- `GameSession` is scoped per circuit, so opening `/debug` in a **new tab** shows a fresh session, not the player's game. In-app navigation keeps the same circuit.
- The Ollama decision call never sets `num_ctx`. The prompt is about 3.9k tokens (every one of the 49 actions plus every room's atmosphere), so a 4B model's default context may silently cut off the rules at the top. Check the raw prompts in `/debug`.
- Invalid model output quietly becomes `ActionKind.Idle` (`OllamaAiDecisionService`). One retry with the validation error would recover most of these.
- `dotnet run` in Production mode serves no static assets (no static-web-assets manifest). Use Development locally, or `dotnet publish` for Production.

**Emergent behaviour:**

- Crew are omniscient about each other's location: social/check-on goals walk to the target's true room (`IntentExecutionSystem` compares against `target.CurrentRoomId`). Using last-seen positions plus searching would let the player hide or misdirect people.
- There are two separate rule-based decision ladders with different thresholds: `BrowserMindSystem` (Pages) and `RuleBasedAiDecisionService` (server fallback). Fixes have already failed to reach both (hunger ordering, PR #52). A single utility scorer (need × personality × relationship × evidence, plus seeded noise) should drive both, and the options offered to the LLM.
- ~~Failures leave no mark~~ fixed: `IntentExecutionSystem.FailIntent` now writes a low-importance `Memory` (the same first-person reason shown in `CurrentAction`) and raises `Npc.Stress` on every failed intent. Still open: a door Overseer controls being in the way should specifically become "Overseer sealed Medical" and feed suspicion, rather than the current generic failure reason/memory.
- The first assignment can be won passively (in an 8-hour run with no player input, it was won while 2 of 4 directives were logged failed). Worth checking the win gate against directive outcomes.

**UI/UX:**

- Missions start running immediately. Consider starting paused on the briefing and auto-pausing on a death or an attack.
- The crew inspector gives equal weight to about 10 bars. Lead with mood, their read on Overseer, the current goal and key relationships.
- The station seed (`GEN // …`) is developer information in the player toolbar and is oversized.

**Aesthetics:**

- The JWST backdrop competes with the small crew tokens. Dim or desaturate it, or vignette it. Door frames are brighter than the crew; give each crew member a colour used everywhere and larger tokens.
- Station state is shown as text rather than atmosphere. Power loss could show as darkness with emergency strips, low O₂ as haze, decompression as particles, and rooms could have ambient audio.
- There is no design system. `Home.razor.css` is 6.9k lines with 489 distinct hex colours, made of stacked per-version override layers. Next step: colour/spacing tokens, then fold the override layers into single rules per component. Only PR #62's provably neutral pruning has been done, so any merge of layers needs visual review.

**Architecture:**

- `Overseer.Web.UI/Pages/Home.razor` is one 2.9k-line component, so every tick re-renders the whole page. Split it into header, map, crew token, inspector, overlays and comms components; this helps performance and review.
- The UI tests are source-string assertions plus ad-hoc Playwright runs. A committed Playwright (or bUnit) smoke suite in CI would catch layout regressions like the header overlap fixed in #62.

## Handoff prompt rule

Future chat prompts should be short: tell the next agent to read this file, inspect current main/recent PR/CI/Pages state, implement the documented next milestone, follow the mandatory workflow, and update this file.

Put technical detail here, not in the chat handoff prompt.
