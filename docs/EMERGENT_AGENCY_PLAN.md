# Emergent Agency Plan

Design contract for widening crew (LLM) freedom while every outcome stays deterministic.
The authority rule in `PROJECT_HANDOFF.md` is unchanged: **the mind proposes what an NPC
wants; deterministic C# decides what can happen and what does.**

"Deterministic" here means both:

1. **Rule determinism** — no model output mutates state directly; everything passes
   validation and resolves through C# systems.
2. **Replay determinism** — the same station seed + the same recorded mind decisions
   reproduce the same run, tick for tick.

## Baseline (after PR #64)

- A mind returns one `NpcMindDecision` (`Action`, `TargetId`, `Goal`, `Reason`, `Urgency`).
  `OllamaAiDecisionService.Validate` silently turns anything invalid into `Idle`.
- `Goal` is display text only.
- `StationSession.AdvanceCoreAsync` awaits `ThinkAsync` inline before
  `IntentExecutionSystem.Tick`, so decisions already apply on the tick they were requested.
- Ollama runs at temperature 0.7 with no seed, so live runs are not reproducible.
- `StationHazardSystem` owns fire/smoke (`Room.FireIntensity`, `Room.SmokePercent`):
  degraded devices ignite, fire consumes O2 and spreads through passable doors, crew
  respond via `FightFire`, `EvacuateHazard`, `SealHazardRoom`, `VentHazardRoom`.
  Rolls use a private FNV `StableRoll(seed, minute, a, b, salt)`.
- `Npc.NeedsMindReconsideration` lets systems request an early rethink.
- There is no inventory; crew interact with fixtures (`RoomFixture`, `FixtureType`) and
  maintainable devices (`StationDevice`, `StationSystemKind`) in their room.

## Status

Each PR updates its own row. Large steps may be split into slices (D1, D2, …); add a row per slice.
Status values: `todo`, `in-review` (PR open), `done` (merged).

| Step | Scope | Status | PR |
|------|-------|--------|----|
| A | Determinism foundation | todo | |
| B | Rejection feedback | todo | |
| C | Tag interaction engine | todo | |
| D | Tag catalog content (slice by domain) | todo | |
| E | Plans, triggers, goal predicates | todo | |
| F | Claims and pacts | todo | |
| G | Cognition upgrades | todo | |
| H | Measurement | todo | |

## PR sequence

One PR per step, in order, each green on `dotnet build` + `dotnet test`. Step C/D is the
priority and should get the most care.

### A. Determinism foundation
- Extract `StableRoll` into one shared `DeterministicRoll` utility in Overseer.Simulation
  and use it everywhere a roll is needed. Never `Random.Shared`, unseeded `Random`,
  `string.GetHashCode()` or `HashCode` (randomised per process in .NET).
- Decision journal at the `IAiDecisionService` / message-interpreter boundary:
  `(minute, npcId, promptHash, decision, source)`. A replay decision service reads the
  journal instead of calling a model.
- `GameStateHash` helper (stable, order-independent over rooms, doors, crew, hazards).
- Pass a seed in Ollama `ChatOptions` (best effort; the journal is the real guarantee).
- Test that locks "think completes before intent execution in the same tick".

### B. Rejection feedback
- `Validate` and executors return a short reason instead of silently idling.
- Keep the last 3 on the NPC; the prompt shows them ("You tried X: failed because Y").
- Repeated failures raise stress and leave a memory. Telemetry counts rejections per action.

### C. Tag interaction engine (no bulk content yet)
See **Tag interaction spec** below. Ships the model, matcher, effect applier, new hazard
kinds and ~20 representative rules with tests.

### D. Tag catalog content
The big table: the content targets, cascades and ambient reactions below.

### E. Plans, triggers and goal predicates
- Closed predicate vocabulary evaluated by C#: `InRoom`, `DoorOpen`, `HazardInRoom`,
  `CrewSeenIn`, `ClaimContradicted`, `TimeReached`, `OverseerMessaged`, `DeviceOffline`.
- Decisions may carry an optional plan (≤4 steps with abort predicates) and ≤3 triggers
  ("if predicate then action", with expiry). The executor runs them and asks for a new
  decision only on failure, abort or a salient event.
- Optional structured goal predicate beside the free-text `Goal`; success/failure becomes a memory.

### F. Claims and pacts
- `Talk`/`ReportConcern`/`MisleadCrew` may carry a structured claim (subject, predicate,
  object, confidence) that spreads through the existing provenance-aware knowledge system.
- Pacts: terms written in the predicate vocabulary; keeping or breaking them is detected
  by C# and feeds relationships and suspicion.

### G. Cognition upgrades
- One shared utility scorer for browser and rule-based minds (per the handoff direction).
  The Ollama prompt shows the scorer's top 5 candidates; choosing off-list is allowed.
- Salience-driven rethinks reuse `NeedsMindReconsideration`.
- Periodic reflection condenses memories into structured `Belief`s.
- Last-seen/search knowledge instead of omniscient target locations.

### H. Measurement
- Headless batch runner (N seeds × 24–72h, rule-based or replayed minds) reporting:
  action entropy, rejection rate per action, tag-rule fires by severity, cascade chains,
  station-loss rate.

## Tag interaction spec

### Taggables
- **Fixtures**: default tags per `FixtureType`, plus optional per-instance extra tags set
  deterministically by generation/seeding.
- **Devices**: default tags per `StationSystemKind`.
- **Derived tags** (computed on evaluation, never stored): room (`Unpowered`, `Dark`,
  `Burning`, `Smoky`, `LowOxygen`, `OxygenRich`, `LowPressure`, `Vacuum`, `Hot`,
  `Freezing`, `Wet`, `Crowded`), device (`Degraded`, `Offline`), door (`Open`, `Welded`,
  `Damaged`), npc (`Injured`, `Exhausted`, `Panicked`), robot/turret (`Hostile`, `Armed`),
  plus any new hazard present.
- **Actor tool kit**: virtual source tags from role and skill, since there is no
  inventory yet: `BareHands`, `Multitool`, `Welder`, `Medkit`, `SecurityKit`.

Tag vocabulary: at least 45 tags across material, energy, thermal, fluid/chemical,
structural, computational, biological, radiological and social/odd domains.

### Verb
- One new `ActionKind.Interact` (target: fixture or device in the current room) with a
  `Method`: `Strike`, `Pry`, `Cut`, `Heat`, `Cool`, `Rewire`, `Overload`, `Drain`,
  `Spill`, `Tinker`, `Salvage`.
- `NpcMindDecision` gains optional `Method` and `SourceId`.
- The existing hazard actions stay; their outcomes may route through rules where that
  removes duplication.

### Rules
```
TagRule(Id, Methods, SourceAll, TargetAll, ContextAll, Forbidden, Skill?, Branches)
Branch(Weight, Severity, Effects[], LogTemplate)
```
- Matching: every rule whose tag sets are satisfied; choose the most specific (most
  required tags), tie-broken by ordinal `Id`. No match → a generic "nothing happens"
  outcome, which is logged and fed back to the NPC.
- Roll: `DeterministicRoll(stationSeed, minute, ruleId, actorId, sourceId, targetId)`.
  Effective skill (after fatigue/sleep-debt penalties) shifts weight toward lower severity
  with a documented formula; exhaustion and panic shift it higher.
- **Ambient rules**: no actor. Evaluated on a fixed cadence in deterministic room/fixture
  order for hazard × fixture pairs, e.g. `Burning`+`Flammable`, `LiveCurrent`+`Wet`,
  `Vacuum`+`Fragile`, `OxygenRich`+`Sparking`. Player verbs (power, lights, ventilation,
  hatches, temperature, airlocks) matter automatically because they change derived tags.

### Effects (closed set, one `TagEffectApplier`, no rule-specific code)
`AddHazard(kind, amount, self|adjacentOpen)`, `ReduceHazard`, `AdjustAtmosphere(O2, CO2,
pressure, temp)`, `DamageDevice`, `DisableDevice`, `DamageDoor` / `JamDoor`,
`CutRoomPower`, `InjureNpc(amount, cause)`, `WitnessStressFear`, `WitnessMemory` (via
perception rules and provenance), `Alert` / `Audio` / `Log` / `Bubble`,
`ScheduleFollowUp(ruleId, delayMinutes)`, `AdvanceReactorInstability(n)`,
`StationLoss(cause)` → `ScenarioStatus.Failed` through the scenario progress path.

### Hazards
Fire and smoke stay on their existing `Room` fields. New hazards go in one keyed
collection on `Room` with deterministic tick behaviour (decay, spread through passable
doors, interaction with ventilation and power):
`LiveCurrent`, `CoolantLeak`, `ToxicGas`, `Radiation`, `StructuralStress`, `HullBreach`
(drives the existing pressure/vacuum systems). Also a station-level `ReactorInstability`
counter with thresholds.

### Content targets (step D)
- ≥150 rules, ≥45 tags, ≥6 new hazard kinds, ≥12 cascade chains, each chain documented in
  a table at the end of this file (trigger → steps → worst outcome → how crew/player can break it).
- Severity scale: `Nothing, Flavour, Minor, Moderate, Severe, Critical, Catastrophic, StationLoss`.
- Target weight distribution across all branches: Nothing ~30%, Flavour ~15%, Minor ~25%,
  Moderate ~17%, Severe ~9%, Critical ~3%, Catastrophic <1%.
- `StationLoss` is reachable only through escalation (`ReactorInstability` or `HullBreach`
  thresholds) with ≥2 compounding preconditions. It is never the result of one action on
  a healthy station.
- Variety: the same action should play out very differently in different contexts.
  Include odd or funny low-stakes outcomes and surprisingly helpful ones (e.g. a strike
  that unjams a device).

### Knowledge and prompts
- Prompts list tags for fixtures/devices in the current room (capped) and remembered
  outcomes ("Strike Capacitor Bank → it arced"). Minds never see rules or probabilities.
- Witnesses learn outcomes as memories with provenance, so crew learn, avoid and exploit
  interactions.
- `BrowserMindSystem` and the rule-based mind must sometimes choose `Interact` so Pages
  exercises the system without a model.

### Tests
- Catalog integrity: unique Ids, positive weights, every tag used by ≥1 rule and provided
  by ≥1 source, every rule satisfiable on some generated station (seeds 1–20),
  `StationLoss` branches properly gated.
- Severity distribution stays within ±5 points of the targets.
- Determinism: same seed + journal → identical `GameStateHash` after 24h.
- One test per cascade chain; ambient reactions to player verbs (e.g. a closed hatch
  stops spread, cutting power removes `LiveCurrent` risk).

### Scope limits
- Hazards are live state and are not persisted to the campaign. Carrying station damage
  over between assignments is a separate decision.
- Presentation in these PRs is limited to the event log, alerts, audio cues, bubbles and
  inspector text. Anything needing new art, animation or mechanics goes in the backlog below.

## Presentation and mechanics backlog (future PRs)

Append here as steps C/D identify needs: per-hazard map visuals (flames, smoke, sparks,
coolant frost, gas tint, radiation glow, hull breach), crew animations per `Method`,
portable items and inventory/carrying, extinguishers and suppression systems, and a
damaged-fixture visual state.

## Review checklist (applied to every PR)

- No model output mutates state; every new path is validated.
- No non-deterministic sources; rolls include explicit inputs.
- Server and Pages behave identically apart from the mind.
- Content matches the targets above; no placeholder or duplicate rules.
- New state has tests; existing tests were not weakened.
- `PROJECT_HANDOFF.md` updated in place.

## Cascade chains

(Step D fills this table.)
