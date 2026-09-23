# Backlog

Part of the authoritative handoff set; see `PROJECT_HANDOFF.md` for the index. This file owns: ingested owner ideas, open known issues, prioritised structural work, deferred items and deliberate "do not fix" decisions. The ordered *next up* list lives in `PROJECT_HANDOFF.md`; detail lives here.

Rules:

- **Remove an item when it is fixed** (in the PR that fixes it). Put any lasting contract it created into `ARCHITECTURE.md` or `SYSTEMS.md`; do not leave struck-through history here.
- A real finding you do not fix immediately — including a non-blocking review finding on another agent's PR — goes here in the same PR/run that raises it, not only in Slack.
- Give each item evidence (file/symbol) and, where known, the fix direction.

---

## Owner ideas

Ingested from `#new-ideas-and-functionality` per `WORKFLOW.md` → Owner ideas. Oldest first. Remove an entry in the PR that ships it, after replying `Shipped in #<PR>` in its Slack thread.

Entry format:

```
### <short title>
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p<ts without the dot> (<date>)
- Idea: "<the owner's words>"
- Outcome: <concrete, testable result that respects the core rule>
- Size: small (one PR) | large (slices: …)
- Status: ready | needs owner input: <question> | in progress (#PR, agent)
```

One batch, 20 entries, from the owner's 2026-09-22 22:17 BST message in `#new-ideas-and-functionality` — a coherent emergent-narrative programme. Several items are natural building blocks for others: **#12 (generic tamper interactions)** is the "generic tag interaction engine" already named next in `ARCHITECTURE.md` → Emergent-agency direction and several later items (contraband, territory, moral disagreements, sabotage-adjacent behaviour) will want to compose on it once it exists; **#1 (pacts)** is the "structured claims/pacts" step in the same programme and #14 (secrets/blackmail) and #6/#19 (suggestion/panic claims) reuse its claim plumbing. Sequence accordingly rather than starting arbitrarily.

### 3. Personal possessions and stealing
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Give crew a tiny number of personally meaningful things: food stash, photograph, medication, tool, keepsake. NPCs can borrow, steal, hide, return or destroy them."
- Outcome: crew get a small fixed set of personally meaningful items with an owner and optionally a hiding spot; borrow/steal/hide/return/destroy are generic interactions; discovery is witness/search-based, never omniscient.
- Size: large (slices below)
- Status: in progress (slices 1-2 shipped, slices 3-5 remain). The two closest existing precedents to build on:
  - **World-state list + observer-specific discovery**, exactly like `BloodEvidence`: `state.BloodEvidence` is a flat list (owner-independent, world-state), and `MedicalEvidenceSystem.NoticeBlood` grants each observer their own `Npc.ObservedBloodEvidenceIds` entry only once `PerceptionSystem.CanSeeBlood` says they can actually perceive it — never omniscient, never global. `state.Possessions: List<PersonalPossession>` (Id, OwnerId, Name, Kind, CurrentHolderId — starts as the owner, HiddenAtRoomId/HiddenAtFixtureLabel when not carried, IsDestroyed) plus a per-`Npc` `HashSet<string> KnownPossessionIds` populated the same way (witnessed a hide/borrow/steal act via `PerceptionSystem.CanMakeOut`, or a successful search) is a direct structural fit.
  - **Absence-triggers-concern polling**, like `MissingPersonSystem`: an owner not finding their own item where expected, on a periodic scan, is the same shape as `NoticeMissedExpectations`/`StartConcern`, and the eventual gossip/accusation consequence needs no new pipeline — `ConversationTopicSystem`'s existing News-retelling path already carries any sufficiently important memory not about the listener, exactly as proven for pact settlements (idea #1) and would apply here too.
  - Slice order: (1) **done** — `FacilitySeeder.SeedPersonalPossessions` gives each crew member 1-2 items (deterministic on name, like `InitialBond`) at roster generation, held by the owner (`CurrentHolderId = OwnerId`, not hidden, not destroyed); the owner's own `KnownPossessionIds` is seeded too but nobody else's is. Covered by `PersonalPossessionSeedingTests.cs`. (2) **done** — `ActionKind.HideItem`/`ReturnItem`, LLM-only (like the pact affordances), let an owner hide their currently-held possession in their current room (`ActionResolver.TryHidePossession`, auto-attributing a Locker/Cabinet/Crate/StorageRack fixture in that room when one exists) or retrieve one they previously hid, but only while physically standing in the room where it is hidden (`ActionResolver.TryReturnPossession`); `CrewAffordanceSystem.TryNormalizeTarget` gates both by ownership/holder-state, `NpcPromptBuilder` exposes a YOUR PERSONAL POSSESSIONS block. Resolves instantly like `FulfillPact`/door ops — no travel/co-location system needed since the owner already knows exactly where their own item is. Covered by `PersonalPossessionInteractionTests.cs`. Remaining: (3) `BorrowItem`/`StealItem` by another NPC plus the witness/search discovery tracking; (4) owner noticing an expected item missing → concern, mirroring `MissingPersonSystem`'s shape; (5) `DestroyItem` and its relationship/memory consequences. Each of (3)-(5) is roughly comparable in size to one of the pact slices already shipped for idea #1.

### 4. Rumours that mutate on retelling
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Instead of copying a memory verbatim, each retelling can deterministically degrade its certainty/details. ... people can compare accounts against physical evidence."
- Outcome: each gossip retelling deterministically degrades certainty/specificity along a fixed decay table rather than copying the memory verbatim; listeners can compare a rumour's stated details against physical evidence via the existing evidence pipeline.
- Size: large (slices: decay table over existing gossip/evidence claim types; apply decay on retelling; surface degraded text to cognition/UI)
- Status: ready — owner notes the existing gossip/evidence architecture already supports this.

### 5. Crew-generated multi-step plans
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Let the LLM propose small multi-step goals ... C# validates every individual step. Plans can adapt when reality changes rather than collapsing immediately."
- Outcome: the LLM can propose a short ordered plan; C# validates each step's capability at execution time using existing intent checks and lets cognition re-plan a step that becomes invalid, instead of either scripting the whole plan or collapsing to idle on the first obstacle.
- Size: large (slices: bounded plan/step data model; per-step deterministic validation reusing existing intent checks; re-plan-on-failure feedback loop)
- Status: ready — matches the "bounded plans/triggers/goal predicates" step already named in `ARCHITECTURE.md` → Emergent-agency direction.

### 6. Emergent leadership via trust
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Someone repeatedly fixing problems gets listened to ... a high-trust NPC might say 'everyone get to Medical' and other NPCs independently decide whether to comply. No magical command mechanic needed."
- Outcome: a high-`Trust` NPC's suggestion is exposed as a perceivable claim other NPCs weigh via existing Trust/credibility stats when the LLM independently decides whether to comply; no new leader role.
- Size: large (slices: expose a "suggestion" claim type; weight compliance by existing Trust/credibility in cognition prompts)
- Status: ready

### 7. Skill learning and mentorship
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Doing something improves skill slowly. Watching/helping a skilled crewmate improves it faster."
- Outcome: completing a task nudges the relevant skill upward slowly; proximity to a more-skilled crewmate doing the same task applies an existing-style multiplier; skill stays deterministic C# state.
- Size: large (slices: skill-gain-on-completion; proximity mentorship multiplier; regression coverage for both)
- Status: ready

### 8. Work quality instead of binary success
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "A tired, stressed, incompetent engineer can technically repair something but do a poor job. The device works... for six hours."
- Outcome: task outcomes carry a deterministic quality/durability value derived from existing skill, fatigue and stress stats instead of flat pass/fail, so a poor repair fails again sooner.
- Size: large (slices: quality formula from existing skill/fatigue/stress; apply to one task type first (repair); extend to others)
- Status: ready

### 9. Private coping behaviours under stress
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Under stress different personalities might overeat, isolate themselves, seek friends, obsessively clean, sleep excessively, argue, exercise, hoard supplies or volunteer for work. The LLM decides the coping strategy; C# makes the consequences physical."
- Outcome: at existing high-stress thresholds, the LLM picks a coping behaviour from a deterministic affordance list; C# enacts its physical consequence through existing systems (food/sleep/bond deltas), no new stat.
- Size: large (slices: coping-behaviour affordance list at high stress; consequence wiring for an initial 2-3 behaviours)
- Status: ready

### 10. Territory and personal space
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "NPCs develop preferred chairs, beds, workstations or rooms. Someone repeatedly using 'their' space causes mild irritation."
- Outcome: an NPC's most-used bed/workstation is tracked from repeated use; another NPC occupying it applies an existing-style irritation/stress delta; friends/rivals bias seating/room choice accordingly.
- Size: large (slices: track most-used bed/workstation per NPC; irritation delta on displacement; relationship-biased choice)
- Status: ready

### 11. Contraband and secret caches
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "NPCs can hide food, tools, weapons, medicine or stolen property inside fixtures/rooms. Other people only discover caches by actually searching or witnessing someone access them."
- Outcome: NPCs can hide items inside a fixture/room; discovery only happens through an active search or witnessed access, never omnisciently; integrates with the prisoner system.
- Size: large (slices: cache data model bound to a fixture; hide/retrieve interactions; search/witness-based discovery)
- Status: ready

### 12. Generic tamper interactions (sabotage without a button)
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Give NPCs generic interactions like cut, loosen, disconnect, overload, spill, jam, block and tamper. ... C# doesn't need to know 'this is sabotage'; it just knows Marcus disconnected a coolant line."
- Outcome: generic physical interactions against deterministic fixture/device tags that any NPC can compose for any motive; C# only ever records the physical act, never a "sabotage" label.
- Size: large — this is the "generic tag interaction engine" already named next in `ARCHITECTURE.md` → Emergent-agency direction; several other items in this batch (#3, #10, #11, #16) will want to compose on it.
- Status: ready — foundational; consider sequencing before items that assume it exists.

### 13. Social cliques emerging from the relationship graph
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Detect clusters from friendships, shared shifts, shared grievances and common beliefs. ... Eventually you could have Engineering vs Security without ever scripting 'Engineering faction exists.'"
- Outcome: detect NPC clusters from existing relationship-graph edges (friendship, shared shift, shared grievance); gossip/disputes propagate more strongly within a detected cluster.
- Size: large (slices: clustering pass over the existing relationship graph; bias gossip/dispute propagation by cluster membership)
- Status: ready

### 14. Secrets and blackmail
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Memories can carry a private/sensitive characteristic. NPCs decide whom they trust enough to tell. ... Blackmail then becomes possible ... 'I know what you did in the airlock.'"
- Outcome: a memory can carry a private/sensitive flag; disclosure is a trust-gated decision; disclosing someone else's secret damages the discloser's trust with its owner; a held secret becomes a blackmail affordance built on #1's pact plumbing.
- Size: large (slices: private/sensitive memory flag; trust-gated disclosure decision; disclosure trust penalty; blackmail as a pact-style interaction)
- Status: ready

### 15. Fear conditioning tied to locations
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "If someone nearly dies in Reactor, Reactor itself becomes associated with that memory. They may hesitate to return there, ask someone to accompany them, or refuse unless the emergency is serious."
- Outcome: surviving a near-death event creates a room-tagged traumatic memory that feeds into existing fear/stress weighting for that room, reduced by decay or overridden by a stronger emergency signal; no new stat.
- Size: large (slices: room-tagged traumatic memory; feed into existing room-entry reluctance/urgency weighting)
- Status: ready

### 16. Moral disagreements about witnessed crew decisions
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "'You vented that compartment while Priya was still inside.' Witnessed decisions become persistent moral memories affecting relationships and future cooperation."
- Outcome: a witnessed harmful decision by one crew member toward another becomes a persistent moral memory affecting the witness's relationship/trust toward the actor, using the existing witnessed-evidence pipeline.
- Size: large (slices: tag certain existing witnessed events as morally salient; relationship-delta wiring on witnessing)
- Status: ready

### 17. Bystander behaviour during fights
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "When two people fight, a third NPC independently decides whether to intervene, fetch help, watch, flee, support a friend, lock the combatants apart or exploit the distraction."
- Outcome: a witnessing NPC gets bystander-eligible affordances (intervene, fetch help, watch, flee, support a friend, lock combatants apart, exploit the distraction) during an active fight, resolved through existing fight/door/alert mechanics.
- Size: large (slices: expose bystander affordances to nearby NPCs' cognition during an active fight; resolve each choice through existing mechanics)
- Status: ready

### 18. Dynamic job ownership
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "People can notice recurring problems and start regarding them as 'their responsibility.' ... Another crew member doing it badly might genuinely annoy them."
- Outcome: an NPC who repeatedly resolves the same recurring job starts checking it unprompted above baseline frequency, from existing task-history tracking; another NPC handling "their" job poorly applies an existing irritation/resentment delta.
- Size: large (slices: recurring-task frequency tracking per NPC per job type; self-initiated check bias; irritation delta when displaced)
- Status: ready

### 19. Collective panic cascades
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "One terrified person flees Engineering shouting about a fire. People who trust them react before personally verifying it; sceptics investigate first. False alarms therefore become emergent too."
- Outcome: panicked flight-and-shout becomes a perceivable/audible claim; hearers react-before-verifying or investigate-first based on existing Trust weighting toward the source, reusing #6's suggestion-claim plumbing.
- Size: large (slices: panic-flight as a witnessable/audible claim; Trust-weighted react-vs-verify branch in cognition)
- Status: ready

### 20. Needs that compete over scarce shared resources
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "One toilet. One remaining meal. One safe bed. One EVA suit. Suddenly mundane resource systems create negotiations, queue-cutting, altruism, arguments, theft and sacrifice."
- Outcome: at least one genuinely-singular station resource is modelled as contested, with queue/wait/negotiate/take affordances; existing need-urgency and relationship stats drive the LLM's choice, not an authored event.
- Size: large (slices: model one singular resource as contested; expose queue/wait/negotiate/take affordances)
- Status: ready

---

## Deliberate decisions (do not "fix")

- The server tick awaits the Ollama decision, so the station pauses while a mind thinks. The owner wants the model to have time to take in the situation. Do not make cognition non-blocking unless asked.
- Closing an ordinary door does not count as "denying access" for corporate directives; the player must lock, weld, barricade or cut power.
- Mission 1 can be won by a passive player; its supplementary directives are deliberately non-mandatory.
- Persistence/save versioning is out of scope for the foreseeable future: `CampaignStateSerializer.Deserialize` silently discards a save on version mismatch with no migration path. Do not start save/load work unless the owner asks.

---

## Structural priorities (architecture review, 2026-09-22)

Pick up in order. These are structural risk/maintainability items, not user-facing bugs. Each is large enough that it should be split into several independently green PRs.

### P1 — Converge the two fallback decision ladders

`BrowserMindSystem` (Pages) and `Overseer.AI/RuleBasedAiDecisionService` (server fallback) independently reimplement NPC decision logic with different structure and drifting values:

- ≥7 duplicated need thresholds (hunger, fatigue, bladder, hygiene, recreation, resentment/argue, socialize gate). Example: hunger-critical is a two-tier `≥72`/`≥58` escalation in `BrowserMindSystem` vs a single `≥62` in `RuleBasedAiDecisionService`.
- Two skill formulas: `CrewCounterplaySystem.BestRepairSkill` vs `RuleBasedAiDecisionService.BestRepairScore` (different clamp order; diverge whenever a modifier is negative).
- BFS reachability hand-rolled three times: `NavigationSystem.FindPathForCrew`, `RuleBasedAiDecisionService.ReachableRooms`, `NpcPromptBuilder.ReachableRooms`.

This has already shipped a bug (a hunger-ordering fix reached only one file). Project references allow a shared home in `Overseer.Simulation`: `Overseer.AI.csproj` references both `Overseer.Domain` and `Overseer.Simulation`, and `RuleBasedAiDecisionService` already has `using Overseer.Simulation;`. Fix: one shared need-scorer/rules library (thresholds, skill formulas, reachability) consumed by both, converging on the single utility model in `ARCHITECTURE.md` → Emergent-agency direction. Slices, each with parity tests:

- [x] Airlock rules — PR #88: `AirlockSafetySystem` now delegates to `Overseer.Domain/AirlockSafetyRules`.
- [x] Reachability — PR #91: `RuleBasedAiDecisionService.ReachableRooms` and `NpcPromptBuilder.ReachableRooms` were byte-for-byte duplicate BFS implementations; both now delegate to a new public `NavigationSystem.ReachableRoomsForCrew`. `NavigationSystem.FindPathForCrew` (Dijkstra/A*, returns a costed path) stays separate on purpose: some call sites use its `.Count > 0`/`== 0` as a reachability proxy, but it computes something genuinely different from a reachable-set BFS, and folding it in would change return semantics at those sites. A future slice could still give it a shared `ReachableRoomsForCrew`-backed fast path for the boolean call sites if it turns out to matter.
- [x] Skill formula — `RuleBasedAiDecisionService.BestRepairScore` (single-clamp `baseSkill + Technical + Repair`) is gone; its 3 call sites now delegate to `CrewCounterplaySystem.BestRepairSkill` (double-clamp: technical component capped at 120 before the repair modifier, then the total capped at 130), the formula the actual repair/restore mechanics already used. Parity test in `AiDecisionServiceTests.cs` reproduces a case (technical +40, repair −70, `baseSkill` 100) where the two formulas used to disagree on crossing the `>= 55` repair-attempt threshold (50 vs 70).
- [x] Need thresholds — PR #95: new `Overseer.Simulation/CrewNeedThresholds.cs` holds the 10 shared constants (hunger/fatigue critical+elevated tiers, bladder, hygiene, recreation, resentment-argue, social need, sociability gate); `BrowserMindSystem` and `RuleBasedAiDecisionService` both consume them instead of independent literals. `RuleBasedAiDecisionService` gained the critical hunger/fatigue tier `BrowserMindSystem` already had (placed before malware/turret/robot/airlock checks, matching); other single-tier needs converged onto `BrowserMindSystem`'s existing values (the pattern followed by the two prior slices, #91 and #93) with matching urgency numbers. 3 new parity regression tests.
- [x] Turret/robot countermeasure decisions — `RuleBasedAiDecisionService.FindTurretCountermeasure`/`FindRobotCountermeasure` and `BrowserMindSystem`'s identically-named methods were byte-for-byte duplicate decision logic (differing only in how each wrapped the result via its own private `Create`). Moved into `TurretCountermeasureSystem.FindCountermeasure`/`RobotCountermeasureSystem.FindCountermeasure` (Overseer.Simulation, which already own the corresponding Tick/consequence logic), returning a shared `CountermeasureDecision` record; both call sites are now thin wrappers. 8 new regression tests on the shared decision logic.
- [x] Shutdown-coordination and missing-person decision helpers — `ShouldJoinShutdownTeam`, `FindKnownShutdownMechanism`, `FindShutdownTeam`, `FindShutdownRecruit`, `MostPressingMissingConcern` and `MissingConcernReason` were byte-for-byte duplicated between the two files (verified by diff). Moved into `ShutdownCoordinationSystem` (`ShouldJoinTeam`/`FindKnownMechanism`/`FindTeamFor`/`FindRecruit`) and `MissingPersonSystem` (`MostPressingConcern`/`ReasonFor`), the systems that already own the corresponding Tick logic for each; both decision files are now thin wrappers. 6 new regression tests. `FindInvestigationLead`, `FindMissingSearchRoom` and `FindSaferRoom` were checked and are **not** byte-identical — each uses a different reachability mechanism (`NavigationSystem.ReachableRoomsForCrew` membership vs. a per-candidate `FindPathForCrew` length check) and would need a determinism/behaviour review before merging, not a mechanical extraction; left as-is pending the full ladder-convergence slice below, which has to make that call anyway. `FindPerceivedUnsafeAirlock` also differs only in referencing `AirlockSafetySystem` (Simulation) vs `AirlockSafetyRules` (Domain) — cosmetic, both already delegate to the same rules per #88, not worth a slice on its own.
- [ ] Ladder convergence onto one utility scorer — the last piece. Remaining duplication is concentrated in the two priority chains themselves (`RuleBasedAiDecisionService.DecideAsync` vs `BrowserMindSystem.Decide`): differing goal/reason flavour text throughout, `BrowserMindSystem` has a `ShouldFightFire`/`FightFire` branch inside its dangerous-room handling that `RuleBasedAiDecisionService` lacks entirely, and the three reachability-dependent helpers noted above disagree on mechanism. Converging this is a genuine design decision (which behaviour wins, e.g. does the fallback gain fire-fighting) and needs careful before/after parity tests, not just a mechanical move.

### P2 — Decompose `Home.razor` / `Home.razor.css`

`src/Overseer.Web.UI/Pages/Home.razor` (~2980 lines) has no child components, `[Parameter]` or `CascadingValue`. One `StateHasChanged()` per ~2.8s tick re-diffs the whole page over the SignalR circuit; cost scales with entity count. `Home.razor.css` (~7300 lines) has stacked override layers (`.crew-token`/`.room-node` redeclared at 4+ places), 59 `!important`, ~640 hex literals, no `:root` tokens. Fix: split into Header/Map/CrewToken/Inspector/Overlay components with scoped styles. Any CSS layer merge needs visual review in a real browser.

Follow-on: UI tests are source-string assertions (`File.ReadAllText(Home.razor)` + `Assert.Contains`, e.g. `StationUiPresentationPolishTests.cs`) that false-pass/false-fail. Add a committed bUnit (or Playwright) smoke suite once components exist.

### P3 — Give the tick loop a contract

No `ISystem` interface; `Tick` is duck-typed with two signatures (`Tick(GameState)` / `Tick(GameState, TimeSpan)`) across ~40 systems. `StationSession.AdvanceCoreAsync` calls them in a hardcoded sequence whose ordering is load-bearing but documented only by inline comments. Fix: minimal `ISystem` with one `Tick` signature, an explicit named ordered pipeline, and rename `SimulationEngine` (e.g. `PhysiologySystem`).

### P4 — Share stateless services instead of `new()`-ing them

`NavigationSystem` is instantiated in ≥8 places (`CrewProvisioningSystem`, `IntentExecutionSystem`, `CorporateDirectiveSystem`, `CrewMaintenanceSystem`, `MedicalSystem`, `BrowserMindSystem`, `CrewRoutineSystem`, `ScenarioSystems`); `CrewDoorInteractionSystem` is owned by `StationSession` and re-created in `LocalMovementSystem`. Harmless while stateless; the first stateful field will silently desync. Fix: a minimal composition root before any of them grows state.

### Lower urgency

`Npc` (`src/Overseer.Domain/Models.cs`) is a 60+ mutable-property bag whose invariants ("never set directly by cognition") are enforced only by comment. `Door` has no guard against contradictory states (welded/open/locked at once). Tighten after P1–P4.

---

## Open issues

**Emergent behaviour**

- Failed intents are remembered (`FailIntent` writes a memory) but nothing feeds that back into cognition prompts/options, so a mind can repeat the same rejected choice.
- Prisoners get only the four `PrisonerDefinition` fields plus standard relationship texture: no prisoner-specific bonds, goals or backstory; escape/flee/recapture motive is fully deterministic rather than mind-authored.

**UI/UX**

- Missions start running immediately. Consider starting paused on the briefing and auto-pausing on a death or an attack.
- The station seed (`GEN // …`) is developer information in the player toolbar and is oversized.

**Aesthetics** (need visual review in a real browser)

- The JWST backdrop competes with the small crew tokens: dim/desaturate/vignette it. Door frames are brighter than crew; give each crew member one colour used everywhere and larger tokens.
- Station state is shown as text rather than atmosphere: power loss as darkness with emergency strips, low O₂ as haze, decompression as particles, ambient room audio.

**Diagnostics**

- Cognition telemetry should eventually cover every model-backed interaction (crew generation, message interpretation, future planners) while staying bounded/transient by default.
