# All Systems Normal — Project Handoff

Repository: https://github.com/LimtlessLtd/All-Systems-Normal  
Playable Pages build: https://limtlessltd.github.io/All-Systems-Normal/

**Current milestone:** V0.9A — Autonomous Robots & Human Countermeasures  
**Next milestone:** V0.9B — Fixed Security Turret & Human Counterplay

This file is the authoritative handoff for the current architecture, invariants, completed capabilities, workflow and next milestone. Do not append historical milestone diaries. Update the relevant current-state sections in place.

---

## 1. Product direction

**All Systems Normal** is an emergent space-station simulation where the player is the station AI.

The player does not directly control humans. They influence autonomous crew through station systems, information and circumstances: doors, locks, power, cameras, lighting, climate, atmosphere, communications and later robots/security infrastructure.

Design priorities:

- emergence over scripted story events
- autonomous humans with persistent goals, relationships and knowledge
- manipulation through the environment and information, not direct unit orders
- physically grounded counterplay: humans must move, investigate, repair, override and coordinate
- systems should interact to create stories rather than act as isolated mechanics

---

## 2. Non-negotiable architecture rules

### AI authority boundary

> **The LLM decides what an NPC WANTS to do. Deterministic C# decides what the NPC CAN do and what actually happens.**

LLMs may produce high-level intentions, reasoning, dialogue, beliefs and interpretations. They must never directly mutate authoritative world state.

Deterministic systems must validate:

- entity/room existence
- NPC knowledge/perception
- reachability and physical location
- door/path state
- action legality
- skill/tool/time requirements
- damage, death and all other physical outcomes

No teleportation, invented rooms/NPCs or direct LLM-triggered kills.

### World-state invariants

- `Npc.CurrentRoomId` is authoritative containment.
- Local `PositionX/PositionY` and `Movement` are presentation/physical movement state inside that containment model.
- `Door.IsPassable` is the navigation truth.
- Strategic routing uses deterministic A* across rooms, connector hallways and doors.
- Door state is revalidated when crossing.
- NPC knowledge is perception-limited. Never feed a global event log or hidden truth to an NPC.
- Stable Blazor `@key` identity on map entities must remain intact; removing it reintroduces false visual movement when filtered entities disappear.
- GitHub Pages must remain model/credential-free.

### Persistence boundary

Campaign saves are explicit snapshots, not arbitrary object-graph serialisation.

Persist only deliberate campaign continuity. Never persist live `GameState`, active movement, intents, jobs, investigations or other transient simulation state unless a future milestone deliberately redesigns the save contract.

---

## 3. Technology and solution layout

Core stack:

- .NET 10 / C#
- Blazor Server + ASP.NET Core
- Blazor WebAssembly
- xUnit
- `Microsoft.Extensions.AI`
- OllamaSharp
- local Ollama for the full AI build

Solution:

```text
src/
  Overseer.Domain/        Authoritative contracts/state
  Overseer.Simulation/    Deterministic simulation and validation
  Overseer.AI/            Ollama cognition, prompt construction, structured AI adapters
  Overseer.Persistence/   Versioned long-term persistence
  Overseer.Web/           Blazor Server / Ollama build
  Overseer.Web.Client/    Static deterministic Pages build
tests/
  Overseer.Simulation.Tests/
```

Runtime split:

- **Overseer.Web** uses Ollama through `IChatClient` for NPC cognition, crew generation and message interpretation, with deterministic fallback services.
- **Overseer.Web.Client** is the GitHub Pages build and uses deterministic/browser-safe cognition only.
- Shared deterministic mechanics belong in Domain/Simulation, not duplicated independently in each UI.

Default local Ollama configuration is currently `qwen3:4b` at `http://localhost:11434`, overridable by configuration/environment.

---

## 4. Current implemented simulation

### Station and movement

- top-down station with functional rooms, dedicated connector hallways and independently controlled doors
- authoritative local NPC coordinates and physical threshold crossing
- A* strategic navigation through actual station topology
- contextual movement to fixtures/equipment rather than room-centre teleportation
- selectable rooms/crew, surveillance visibility and observable speech/thought/alert bubbles
- resizable IDE-style UI panels and map zoom
- soft automatic turns at selectable speeds
- event-driven audio cues plus optional ambient music

### Crew and cognition

Crew model includes:

- health, hunger, fatigue, fear, stress
- hygiene, bladder, recreation, social and intimacy needs
- role, skills and generated personality traits with mechanical modifiers
- pairwise affinity, trust, resentment and attraction
- memories and beliefs
- persistent high-level intents
- routines and physical jobs
- credibility/suspicion toward Overseer
- observer-specific knowledge, sightings, investigation leads and discoveries

The server can generate crew through the model; the browser build uses deterministic fallback generation. Scenario content binds to live crew rather than assuming hard-coded names.

### Environment and station operations

Implemented deterministic systems include:

- room temperature and climate control
- oxygen, CO2 and pressure
- ventilation and life support
- powered room/device behaviour
- pressure-cycled airlock operation and safety interlocks
- deliberate unsafe airlock operation with observable consequences
- equipment wear/failure and physical repair
- power generation/demand and load shedding
- hydroponic crop growth
- watering/feeding/harvesting
- galley cooking and meal inventory
- crew provisioning/eating
- physical environmental harm and evacuation behaviour

### Autonomous robot

V0.9A adds one authoritative MR-1 maintenance/security robot.

- `StationRobot` uses the same room containment, local coordinates, movement order and door-threshold revalidation model as crew.
- Overseer can set only high-level `Friendly` / `Neutral` / `Hostile` policy and issue remote power commands while the control link is available.
- Friendly repairs real station faults; Neutral patrols/charges; Hostile selects reachable humans, physically navigates to them and only attacks inside deterministic range/cooldown rules.
- Robot policy changes, remote shutdowns and attacks generate observer-local evidence through the existing suspicion/provenance system.
- Crew cognition in both browser fallback and Ollama can choose grounded countermeasures: local shutdown, Engineering network isolation, charging denial, physical damage, and local reprogram/reboot.
- Network isolation blocks later Overseer remote policy/power commands. Charging denial matters through deterministic battery depletion.
- Both UIs show MR-1 position/state/task and the same link-gated high-level controls.

Implementation lives primarily in `RobotSystem.cs` and `RobotCountermeasureSystem.cs`; regression coverage is in `RobotSystemTests.cs`.

### Door and human counterplay

Doors support mechanically distinct states including:

- powered locking/opening
- manual override
- technical bypass
- structural damage
- repair
- welding
- barricading

Crew can investigate, discover shutdown hardware, force/override routes, repair station systems and coordinate shutdown attempts. Overseer cannot magically undo physically secured/damaged states that are outside its authority.

### Suspicion, evidence and investigation

The information model is structured and observer-specific.

Evidence supports:

- direct observation
- physical discovery
- testimony
- inference
- stable provenance/root IDs
- reliability
- falsifiable claims
- decay/contradiction/discrediting

NPCs can form physical investigation leads, travel to a location, inspect it and discover what is actually present. Remote/omniscient investigation is not allowed.

Crew can compare accounts, share evidence, spread rumours and coordinate against Overseer. Suspicion is grounded in current evidence rather than being a permanently increasing hidden score.

### Overseer communications and social manipulation

The player can send:

- station-wide broadcasts
- private messages

Messages are interpreted into structured claims. Invented rooms/people are rejected or reduced to harmless social noise. Truthfulness is evaluated deterministically against world state and can be discovered later by crew observation/account comparison.

Overseer credibility is distinct from hostility/suspicion.

### Corporate scenarios and campaign

There are five ordered campaign assignments in `ScenarioCatalog.Campaign`.

The campaign includes:

- operational scenario objectives
- corporate directives and sponsor compliance
- mandatory and supplementary directives
- public justifications vs hidden `TruePurpose`
- multiple shutdown-access variants
- escalating experimental intent
- explicit mission success/failure
- persistent campaign consequences

Mission progression is locked to the next incomplete campaign assignment; arbitrary package selection is no longer the campaign flow.

Campaign continuity currently carries deliberate long-term state including:

- mission history
- cumulative sponsor compliance
- continuing crew identity/traits/skills
- pairwise relationships
- Overseer credibility and damped suspicion
- bounded important memories
- crew health/presence consequences
- equipment condition
- provisions
- staged sponsor reveal
- final campaign ending

Transient movement, active actions/intents, maintenance/provisioning jobs, investigation leads and room-local activity do not carry between assignments.

### Reveal and endings

Sponsor-purpose material progresses through:

`Classified → Uneasy → Compromised → Exposed`

The final exposed campaign unlocks four explicit endings:

- obey sponsor / continue programme
- expose the experiment
- sever sponsor control / preserve Overseer
- accept crew shutdown

The chosen ending is immutable for that campaign.

---

## 5. Campaign persistence

Persistence implementation:

- `src/Overseer.Persistence/CampaignStateSerializer.cs`
- versioned format, currently version 1
- browser storage key: `all-systems-normal.campaign.v1`

Both Pages and server UIs persist the explicit campaign snapshot in browser local storage.

Intentional behaviour:

- completed campaign progress survives browser reload/restart
- the server build can restore the same campaign after a circuit/server restart through browser-held campaign data
- mid-assignment live simulation state is **not** saved
- reload resumes campaign continuity by rebuilding a fresh station for the next assignment
- RESET clears campaign storage and starts a new campaign
- malformed/unsupported persistence payloads fail closed

---

## 6. Important implementation anchors

Before changing a subsystem, inspect the current code rather than relying only on this summary.

Key areas:

- `Overseer.Domain/Models.cs` — core simulation/NPC contracts
- `Overseer.Domain/CorporateDirectives.cs` — corporate directive contracts
- `Overseer.Domain/CampaignProgression.cs` — campaign domain state/endings
- `Overseer.Simulation/ScenarioSystems.cs` — scenario catalog/application and related rules
- `Overseer.Simulation/RobotSystem.cs` — deterministic MR-1 policy execution, navigation, repair, power and attacks
- `Overseer.Simulation/RobotCountermeasureSystem.cs` — deterministic physical crew counterplay
- `Overseer.Simulation/CampaignProgressionSystem.cs` — campaign capture, carry-over, reveal and transitions
- `Overseer.Persistence/CampaignStateSerializer.cs` — persistence boundary
- `Overseer.Web/Services/GameSession.cs` — Ollama/server session integration
- `Overseer.Web.Client/Services/GameSession.cs` — Pages/browser session integration
- both `Home.razor` files — mirrored player-facing controls/presentation
- `tests/Overseer.Simulation.Tests/` — regression contract

Do not assume old test counts or historical PR descriptions are current.

---

## 7. Validation and Git workflow

Mandatory workflow for every completed milestone/change:

```text
main
→ new feature branch
→ implementation
→ tests/build/publish
→ PR
→ green CI
→ merge into main
→ verify post-merge GitHub Pages deployment
```

Do not leave completed green work sitting in an open PR unless explicitly instructed.

Standard validation:

```bash
dotnet build Overseer.slnx -c Release
dotnet test tests/Overseer.Simulation.Tests/Overseer.Simulation.Tests.csproj -c Release --no-build
dotnet publish src/Overseer.Web.Client/Overseer.Web.Client.csproj -c Release -o release --no-restore
```

The repository Pages workflow additionally rewrites the base path for `/All-Systems-Normal/`, prepares the static output and deploys from `main`.

Before handoff:

- ensure PR CI is green on the final PR head
- merge completed work
- verify the post-merge `main` workflow and Pages deploy succeeded
- update this file in place
- leave no stale “next task” sections elsewhere in this file

---

## 8. Next milestone — V0.9B Fixed Security Turret & Human Counterplay

Build one fixed security-turret vertical slice using the authority model proven by MR-1. Do not add a fleet or general combat framework yet.

Required scope:

1. Add one fixed turret with authoritative room, power, network/control-link, integrity and armed state.
2. Overseer controls only high-level security policy and arming where the control link permits it; no direct click-to-damage command.
3. Deterministic C# must own target eligibility, line/range checks, firing cadence, hit/damage outcomes and ammunition/heat/power limits.
4. Turret behaviour must be physically local to its installed compartment/coverage; it cannot observe or attack through sealed geometry.
5. Arming, tracking and firing must create perception-limited evidence through the existing provenance/suspicion system.
6. Add grounded human counterplay: local disarm, network isolation, power denial, physical sabotage/damage and skilled local reprogramming.
7. Reuse existing Engineering/control-link and station power concepts where possible rather than inventing a parallel authority model.
8. Surface turret state and available high-level controls in both Pages and Ollama/server UIs.
9. Add regression coverage for authority boundaries, visibility/range, damage, control-link isolation, power denial, human counterplay and evidence.

Keep deferred:

- multiple turret classes or broad weapons framework
- multiple robot classes
- robot/turret self-destruct
- autonomous lethal decisions delegated directly to an LLM
- virus/malware mechanics

V0.9B should stay a narrow second proof that fixed security infrastructure obeys the same physical, observable and counterable rules as MR-1.
---

## 9. Handoff rule

Future chat handoff prompts should stay short:

- tell the next agent to read this file in full
- inspect current `main`, recent PR/CI and Pages deployment
- implement the single current next milestone
- follow the mandatory branch/PR/CI/merge/Pages workflow

Do not duplicate architecture, old implementation history, test counts or detailed roadmap prose in the chat prompt. Keep those details here, and keep this file current rather than appending historical changelogs.
