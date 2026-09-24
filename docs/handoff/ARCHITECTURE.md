# Architecture and invariants

Part of the authoritative handoff set; see `PROJECT_HANDOFF.md` for the index. This file owns: the authority model, invariants, project layout, station generation/presentation architecture, persistence boundary and cross-cutting implementation contracts. It changes rarely; edit a section in place when a contract actually changes.

---

## Product and authority model

**All Systems Normal** is an emergent station simulation where the player is the station AI. Humans are autonomous; the player manipulates systems, information and circumstances rather than issuing unit orders.

Non-negotiable rule:

> **The LLM decides what an NPC WANTS to do. Deterministic C# decides what the NPC CAN do and what actually happens.**

Build deterministic world mechanics, affordances, constraints and consequences, and let the mind compose actions from them. Do not replace emergent behaviour with large hard-coded decision trees.

Deterministic systems own entity validity, knowledge/perception, containment, routing, physical crossings, skills, time, environment, combat, damage and death.

Core invariants:

- `Npc.CurrentRoomId` is authoritative containment.
- `PositionX` / `PositionY` / `Movement` are local physical/presentation state.
- `Door.IsPassable` is crossing truth; crossings revalidate live door state. Crew route planning (`FindPathForCrew`) also plans through closed, unlocked, powered hatches via `CrewDoorInteractionSystem.CanTraverseWhenReached`, because crew open those at the portal. Robots plan with `IsPassable` only.
- Strategic routing is deterministic A* over real room/corridor/door topology. Local movement follows collision-safe fixture/wall/door waypoints; actors must never visually shortcut a later waypoint through geometry. Local PositionX/Y are room percentages only; movement budgets are measured in station-map distance so physical walking speed is independent of room/corridor dimensions.
- Timed physical work is authoritative `CrewTaskState`: deterministic C# starts, progresses, interrupts/fails and completes it. Once hands-on work starts it is committed; routines, chores and arbitrary urgency cannot cancel it. Only a deterministic immediate survival threat may pre-empt it, and success is recorded only after the world mutation actually occurs. **Every path that invalidates an in-progress task (NPC moved, target gone, resource lost) must resolve it via `CrewTaskSystem.Interrupt`/`Fail`** — `FailIntent` alone leaves the task `InProgress` and permanently blocks the NPC, because availability gates on `!CrewTaskSystem.IsWorking(npc)`.
- Visible map connections must be real navigable geometry; never draw fake corridor edges.
- Knowledge/evidence is observer-specific and provenance-aware.
- Stable Blazor `@key` identity is required for map entities.
- Pages remains model/credential-free.
- Campaign persistence stores deliberate continuity, not arbitrary live GameState.

---

## Stack and projects

.NET 10 / C#, Blazor Server + ASP.NET Core, Blazor WebAssembly, xUnit, Microsoft.Extensions.AI, OllamaSharp / local Ollama.

- `Overseer.Domain` — authoritative contracts/state
- `Overseer.Simulation` — deterministic mechanics/generation/validation
- `Overseer.AI` — Ollama cognition and structured adapters
- `Overseer.Persistence` — versioned campaign persistence
- `Overseer.Web.UI` — the one station console (Razor Class Library): `Pages/Home.razor(.css)`, `Pages/Debug.razor(.css)`, `wwwroot/layout.js`, `wwwroot/audio.js`
- `Overseer.Web` — server/Ollama host
- `Overseer.Web.Client` — deterministic static Pages host
- `Overseer.Simulation.Tests` — regression contract

Shared mechanics belong in Domain/Simulation and must not be independently reimplemented in each UI.

**One UI, two hosts.** `StationSession` (Overseer.Simulation) owns the station, the tick pipeline and every operator verb; each host's `GameSession` subclass supplies only crew creation and the think step (Ollama on the server, `BrowserMindSystem` on Pages). Pages inject `StationSession` and carry no `@rendermode` — the server applies `InteractiveServer` globally on `<Routes>` (with `/Error` excluded) and registers the library with `AddAdditionalAssemblies` on both `MapRazorComponents` and its router; the client router does the same. Scripts load from `_content/Overseer.Web.UI/`. Never add a host-local copy of a console page or script (`StationSessionTests` guards this).

**Server runtime facts:**

- The server tick awaits the Ollama decision (`StationSession.AdvanceCoreAsync` → `GameSession.ThinkAsync`), so the station pauses while a mind thinks. This is deliberate (see `BACKLOG.md` → Deliberate decisions).
- `Components/App.razor` disables prerender (`new InteractiveServerRenderMode(prerender: false)`) so crew is generated once, by the real circuit. Tradeoff: first paint is blank until the SignalR circuit connects and generation finishes. Guarded by `StationSessionTests.ServerHostDoesNotPrerenderTheInteractiveRoute`.
- `GameSession` is scoped per circuit: opening `/debug` in a new tab shows a fresh session; in-app navigation keeps the same circuit.
- `OllamaAiDecisionService` sets `num_ctx` 16384 (the decision prompt is ~3.9k tokens; Ollama's 2048 default truncates silently) and retries once with a corrective instruction on unparseable output before falling back to `RuleBasedAiDecisionService`. Covered by `AiDecisionServiceTests`.
- `dotnet run` in Production mode serves no static assets (no static-web-assets manifest). Use Development locally, or `dotnet publish` for Production.

---

## Procedural station architecture

Primary files: `src/Overseer.Domain/StationGenerationModels.cs`, `src/Overseer.Simulation/StationGenerator.cs`, `src/Overseer.Simulation/FacilitySeeder.cs`, `tests/Overseer.Simulation.Tests/ProceduralStationGenerationTests.cs`, `tests/Overseer.Simulation.Tests/StationGeometryTests.cs`.

Generation is deterministic for the same station seed + campaign constraints.

Pipeline: **campaign constraints → station identity → archetype → room requirements → topology → spatial packing → physical connector passages → doors → fixtures/details → systems/security → validation → presentation**

Archetypes: Linear, CentralHub, Branching, Ring, AsymmetricIndustrial, Compact, Sprawling, MultiSpine, Retrofit.

`StationIdentity` includes purpose, age, budget, size, crew capacity, industrial intensity, security level, maintenance condition and expansion history. Identity affects topology selection, corridor widths, room scale/grouping, industrial/habitat character, door resistance and deterministic interior detailing.

### Constraint/override layer

`StationGenerationConstraints` is the hard campaign layer. Procedural choices are soft preferences and never override it. Hooks include: required/forbidden rooms and room-count bounds; required adjacency/separation; forced archetype/purpose/budget/size/expansion/security; airlock count and named airlock placement; turret/robot counts and placement; redundant routes required/forbidden; required chokepoints; reactor isolation / medical-near-habitat; mandatory shutdown room; initially accessible/inaccessible rooms; environment/system overrides; planned crew count, hydroponics-capacity multiplier and allowed crop/seed contracts; procedurally packed authored/set-piece rooms; fully authored room geometry + connections.

This supports fully procedural, constrained procedural, partially authored and fully authored missions through the same Facility/simulation model. Do not add scenario-specific conditionals inside the generator when a constraint can express the requirement.

### Geometry invariants

Generated stations must preserve: real non-overlapping rooms; real physical corridor rooms and connector passages; doors only on actual shared boundaries; usable passage cross-sections (each functional-room access tunnel matches the connected spine/corridor short-axis width exactly); all geometry inside the station canvas; functional-room area greater than circulation area; structurally reachable required rooms unless explicitly authored otherwise; canonical functional IDs (control, engineering, reactor, isolation, …) where simulation systems require them; fixtures inside their owning room; valid airlock/security placement; deterministic human and robot navigation.

Packing retries are deterministic and may reject a candidate station; validation must never be weakened merely to make a seed pass. Crowded-seed retry budget may increase, and normal generation must retain its deterministic late-attempt packing fallback.

### Extension points

For new campaigns, configure `ScenarioDefinition.StationConstraints`. Use `AuthoredRooms` for set pieces whose location may be packed procedurally; use `FullyAuthoredGeometry` only for missions requiring exact geometry. If a new simulation system needs a room role, preserve an existing canonical ID or introduce an explicit semantic constraint/model contract; do not infer meaning from screen position.

Both UIs expose a compact `GEN // seed` inspector with same-seed/new-seed regeneration and generation diagnostics. Keep developer information behind this disclosure rather than adding permanent player clutter.

---

## Station presentation architecture

Primary helper: `src/Overseer.Simulation/StationPresentationSystem.cs`. Both runtimes render the same `src/Overseer.Web.UI` console; change it once. Station identity classes are derived from authoritative `Facility` + `StationGenerationMetadata`.

- The map is a large pannable virtual deck, **not** auto-fitted: `.station-map-camera` is 3200×2800px at 100% zoom; authoritative geometry lives in `.station-authority-layer` at the central 80% (2560×2240px); functional rooms are clamped to at least 8% × 9% (~205×202px).
- `.station-world` keeps rooms, corridors, doors, fixtures, crew, robots and turrets in one coordinate system. Camera pan/zoom is presentation-only and must never alter simulation coordinates. Drag/WASD/arrow panning plus wheel/± zoom must reach the full deck; wheel over the station zooms without a modifier.
- Map camera panning never pointer-captures on pointerdown; capture begins only once a press moves past the pan threshold, so plain clicks on `[data-station-interactive]` entities still reach them while drags on rooms/corridors pan (and do not select). Regression-tested.
- Room status plates: `Room.StatusPlateSide` is chosen during generation; `StationGenerator` reserves and validates their external envelopes against all geometry and other plates. `StationRoomCalloutSystem` attaches each plate to its room's top/bottom edge, outside the floor, exactly as wide as the room (`LabelWidth`, capped ~260px, inline `box-sizing: border-box`), with a collision pass moving a label to the other edge if two meet. Plates show full room name + power/temperature/O₂.
- Hull mass is drawn only from real room/corridor footprints. No decorative rails/links implying nonexistent navigation.
- `FacilitySeeder.ApplyIdentityDrivenDetails` adds deterministic room-aware fixtures: wall equipment bulkhead-aligned, floor equipment in collision-aware work bays, all inside their owning room. Maintainable machinery binds to physical fixtures via `RoomFixture.DeviceId`.
- Art direction: bright white/grey aerospace hulls, walls and machinery around a dark black tiled deck floor in every functional room, restrained green/orange/red status colour; darker astronomical backdrop outside the hull. Hover/selection must never hide the tiled floor. Avoid neon cyan/blue, brown grime filters or labels painted on the floor. Interiors should be visibly active (consoles, vents, irrigation, pipes, medical, cameras, airlocks, machinery); repeating animations must be seamless closed cycles. Reactor air-handler visuals stay inside their machinery footprint.
- Sliding doors animate from authoritative `Door.IsOpen`; leaves retract fully clear of the walking line, mobile entities render above the hatch plane, and doors carry visible local control pads.
- Robots are selectable, top-down machine silhouettes consistent with the crew camera angle — never generic dots/cards. Human movement uses continuous local-motion state and contextual hand/arm animation only during hands-on actions. The selected unit's route is a thick green dotted path.
- **Workspace:** `Home.razor` renders the Station Overview + Inspector as the only gameplay surface; mission clock, alerts, pause (button + Space), speed, zoom/FIT and CREW / LOG / MESSAGES / OBJECTIVES / MENU live in the station toolbar/overlays. Do not restore duplicated mission/directive/comms panels, a CONSOLE/focus toggle or a permanent Facility Systems panel. RESET RUN always confirms. The header lets controls wrap onto further rows; right-aligned controls must never sit on a non-wrapping line. The player LOG uses `StationLogPresentation` to omit routine movement.
- **Inspector** is the universal contextual surface for anything clickable (crew, rooms, doors, robots, turrets, grow bays, machinery, key status readouts) via one `StationSelection` / `StationInspectionSystem` contract. The crew Inspector leads with MOOD (presentation-only, derived from `Stress`/`Fear`/`Health` using the `.disposition` styling), DISPOSITION TOWARD OVERSEER, CURRENT GOAL and KEY RELATIONSHIPS (top ally by `Trust`+`Affinity`, top rival by `Resentment`, thresholded so a neutral roster shows none) before vitals/traits/metric bars. Grow-bay selection exposes only deterministic enable/crop-request controls and lifecycle state.
- **`/debug`** is a separate full-screen route with no station map and no player-critical information (release builds may hide it). It holds numbered cognition traces (exact Ollama prompt/options + provider-native `ChatResponse.RawRepresentation`), bounded to 64k prompt / 32k response per entry.
- Alerts keep recent actionable history; hover shows the last five, click selects/navigates to the related entity.
- CSS: when restyling, edit the existing rule in `Home.razor.css` instead of appending another override layer at the end of the file.

Visual invariants: never invent hull/corridor/door geometry, never offset one physical entity separately for aesthetics, and never let decorative fixtures become simulation-authoritative unless the domain contract is explicitly extended. Interactive station buttons must never receive generic `:active` transforms, because rooms/fixtures/crew/robots use transforms for authoritative positioning.

---

## Persistence boundary

`src/Overseer.Persistence/CampaignStateSerializer.cs`, format version 1, browser key `all-systems-normal.campaign.v1`.

Persist deliberate campaign continuity: mission history, corporate directive state, continuing crew identity/traits/skills (`CrewContinuitySnapshot`, including `IsPrisoner`/`PrisonerDangerLevel`/`PrisonerViolenceBias`, so a future `CampaignContinuing` scenario with prisoners keeps them), relationships, bounded important memories, credibility/suspicion, health/presence consequences, equipment condition and provisions. Do not persist live movement, intents, jobs, investigations or cognition telemetry unless the save contract is explicitly redesigned.

Auto-restore must not launch a `CampaignContinuing` assignment when the persisted continuity snapshot has zero living, present crew. Until the physical Corporation crew-resupply flow exists, both runtimes reject that dead-end continuation and start a fresh *Secure Continuity* assignment instead of briefly rendering a fresh roster and then replacing it with an all-dead saved roster.

---

## Cross-cutting implementation contracts

- Pages remains model/credential-free; server/Ollama and browser surfaces must preserve simulation parity by running the same `StationSession` pipeline and `Overseer.Web.UI` console.
- **Fixture collision** is authoritative for mobile entities, and procedural packing uses the same collision predicate. Free-standing machinery gets wider circulation separation; compact bulkhead service banks may sit closer because their access floor is in front; real door approaches are reserved; explicit standing interaction points reserve a small clear floor target; local routing falls back to a deterministic occupancy-grid detour if the visibility graph dead-ends. Bulkhead-integrated decoration may be non-blocking. Visibility-graph detour nodes and grid routing share the safe local `4..96` navigation domain. The visibility-graph safety margin stays `0.35` (clearance is enforced separately); fallbacks must never reselect the actor's current waypoint, and survival intents must never stall on a zero-length waypoint.
- The seed-1 Storage regression (`Marcus Reed` at the formerly stuck door approach) protects the 24-hour provisioning/maintenance lifecycle from fixture-routing starvation. Do not weaken its lifecycle assertions.
- **Perception:** human LOS is directional (forward cone, open-door aware) and light-dependent — if either end of the sightline is unpowered or unlit, human range drops to 35% (sightings, blood evidence and violence attribution all follow). Same-compartment witnesses identify an attacker by distance and light rather than facing; otherwise they only hear a struggle. Robot/turret sensors are omnidirectional, light-independent and cannot acquire through walls; previously acquired hostile targets may continue to be pursued under existing rules.
- **Medical:** care, resurrection and blood evidence are deterministic. Resurrection needs a powered medbay, resources/charge and a present recoverable body. The procedure in progress lives in `Npc.MedicalActionKind` (never read back from `CurrentAction`, which other systems rewrite). A doctor mid-procedure and a patient waiting in a capable medbay are protected from routine errands; leaving abandons the procedure; a waiting patient calls the doctor in as medical duty. Injured crew are routed to the medbay only when a doctor, supplies and a safe medbay exist; an existing trip is kept rather than recreated; plans with urgency ≥ 97 are never overridden. Witnesses rethink once per injury (`Npc.NoticedInjuredCrewIds`).
- **Doors:** `Door.LockedByOverseer` is true only while the current lock was set by the Overseer verb (`StationSession.ToggleLock`); every other path that changes `IsLocked` (crew `LockDoor`/`UnlockDoor`, manual override, counterplay bypass, scenario setup) resets it. A crew route blocked only by such a door makes `IntentExecutionSystem.ReportSealedRoute` report "Overseer sealed {Room}" and records `SuspicionSystem` evidence (`EvidenceClaim.AccessRestricted`) as personally witnessed denial — distinct from `ObservePlayerDoorChange`, which fires only for a bystander beside the door as it locks. Closing an ordinary door does not count as denying access for directives (lock, weld, barricade or cut power instead).
- Where regression coverage lives: movement/perception/medical in `MovementPerceptionMedicalPolishTests.cs`; routines/hazards/containment in `EmergentWorldSystemsTests.cs`; containment transfer in `PrisonerTransportTests.cs`; plus lifecycle, robot, UI and procedural-generation suites.

---

## Emergent-agency direction (deferred programme)

Deferred behind current work unless the owner reprioritises it; the concrete next steps are tracked in `BACKLOG.md`.

Goal: widen NPC/LLM freedom through composable world affordances, not a decision tree. The mind proposes intent; deterministic C# validates capability, applies physics/resources/access/skills and resolves outcomes.

- Preserve both **rule determinism** (model output never mutates state directly) and **replay determinism** (station seed + recorded mind decisions reproduce a run).
- Planned sequence: shared deterministic roll + decision journal/state hash → rejection feedback → generic tag interaction engine → interaction/hazard content → bounded plans/triggers/goal predicates → structured claims/pacts → shared utility scoring and last-seen/search cognition → headless replay/metrics.
- A future generic interaction verb may compose methods (strike/pry/cut/heat/cool/rewire/overload/drain/spill/tinker/salvage) against deterministic fixture/device/context tags; C# owns target validity, matching, skill/resource checks and weighted deterministic outcomes. Consequences flow through one closed effect layer, not bespoke per-rule mutation. Ambient world reactions may use the same mechanism so player verbs (power, ventilation, doors, atmosphere) change outcomes.
- Minds see observable affordances/context and remembered outcomes, never hidden rule tables or probabilities.
- New hazards are authoritative world state with deterministic decay/spread and real interaction with doors, atmosphere, ventilation, power and damage. Catastrophic/station-loss outcomes require escalation and compound preconditions.
- Failed/rejected intentions become bounded feedback/memories rather than silently collapsing to idle. Browser/server fallback cognition should converge on one shared utility model; Ollama remains free to choose a different valid action.
- **Fallback-vs-LLM behavioural default (owner decision, 2026-09-23, `#agentic-problems`).** The deterministic fallback ladders (`BrowserMindSystem`/`RuleBasedAiDecisionService`) are the "model citizen" baseline: sensible, procedure-following behaviour (e.g. fighting a survivable fire rather than fleeing it, gated only on existing courage/skill/stress thresholds) is their *default* position, not a cowardly or passive one. The LLM path is where genuine human unpredictability — hesitation, panic, poor judgement, self-interest — is expected to diverge from that baseline; it is not the fallback's job to be less capable or less brave than the LLM. Apply this when deciding a fallback ladder's default behaviour for any new affordance, the same way `StationHazardSystem.ShouldFightFire` now does for fire.
- **Reflex first, model decides (owner rule, 2026-09-24, `BACKLOG.md` #106).** The model owns long-term goals. Immediate reactions are deterministic and needs-driven, in Maslow order: safety first, then bodily needs, then social. A person reacts at once through the shared fallback ladder, the model is asked as well, and its decision takes priority once it arrives. The reflex must be something the minds could already choose, and the same event must not overrule the model's choice again. This refines the core rule rather than replacing it: C# may fill the latency gap, but the person's considered intent is still the model's.
- Tests must cover replay/state hashing, catalog/rule integrity, representative cascades, ambient reactions to player verbs and the authority invariant. Never weaken existing simulation tests to make new content pass.
