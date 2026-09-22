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

_None yet._

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
- [ ] Skill formula (one repair-skill score)
- [ ] Need thresholds (one table)
- [ ] Ladder convergence onto one utility scorer

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

- Crew are omniscient about each other's location: social/check-on goals walk to the target's true room (`IntentExecutionSystem` compares against `target.CurrentRoomId`). Last-seen positions plus searching would let the player hide or misdirect people.
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
