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

### 5. Crew-generated multi-step plans
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Let the LLM propose small multi-step goals ... C# validates every individual step. Plans can adapt when reality changes rather than collapsing immediately."
- Outcome: the LLM can propose a short ordered plan; C# validates each step's capability at execution time using existing intent checks and lets cognition re-plan a step that becomes invalid, instead of either scripting the whole plan or collapsing to idle on the first obstacle.
- Size: large (slices: bounded plan/step data model; per-step deterministic validation reusing existing intent checks; re-plan-on-failure feedback loop)
- Status: **in progress** — slice 1 (bounded plan/step data model) shipped. `ARCHITECTURE.md` → Emergent-agency direction's planned sequence placed "bounded plans/triggers/goal predicates" *after* a "rejection feedback" step; that prerequisite (`Memory.IsFailedAttempt` as an explicit typed marker, surfaced in a dedicated "YOUR RECENT FAILED ATTEMPTS" prompt block) was fixed in an earlier PR, unblocking this idea.

  Slice 1 (this run): `NpcPlanStep`/`NpcPlan` (`Overseer.Domain/Models.cs`) — a bounded (`NpcPlan.MaxSteps = 4`), ordered, immutable step queue — plus `Npc.Plan` and a new `PlanExecutionSystem` (`Overseer.Simulation`) that promotes `Plan.Steps[0]` into `Npc.Intent` only once the current intent is null, wired into `StationSession.AdvanceCoreAsync` *before* `ThinkAsync` so a continuing plan already reads as "goal in progress" to cognition's own "don't reconsider a busy NPC" gate, exactly like an ordinary intent. `IntentExecutionSystem` validates a promoted step through the exact same execution-time checks (target/reachability/lifetime) any freshly decided intent already gets — no new validation logic was needed. The plan is abandoned (`Npc.Plan = null`) at all three places an intent is already abandoned without completing (`FailIntent`, lifetime expiry, sticky-task pre-emption) rather than blindly continuing past an invalidated step — this is deliberately how "re-plan on failure" is realized: cognition's next decision sees the `IsFailedAttempt` memory and a null `Intent`/`Plan`, and decides fresh, rather than C# scripting what happens next. Regression coverage in `PlanExecutionSystemTests.cs` (bounds, promotion ordering, a full promote→execute→promote→execute walk using two `Idle` steps, and abandonment on failure/expiry).

  **Nothing produces an `NpcPlan` yet** — `OllamaAiDecisionService`/`RuleBasedAiDecisionService`/`BrowserMindSystem` are untouched, so this slice is a behavioural no-op in production and carries no fallback-ladder-parity risk (the risk the previous scoping note flagged). Remaining slices, in order:
  1. Have `OllamaAiDecisionService`/`NpcPromptBuilder` let cognition propose an `NpcPlan` (reusing the existing `Microsoft.Extensions.AI` structured-output schema machinery — `GetResponseAsync<T>` is already generic, so a `List<Step>`-shaped response DTO is additive, not a new plumbing pattern) instead of a single `NpcMindDecision`, validated the same way single intents are today before becoming a plan's first `NpcIntent`.
  2. Decide (with the owner, if it's not obvious) whether `RuleBasedAiDecisionService`/`BrowserMindSystem` should ever propose plans themselves, or whether multi-step plans are deliberately an LLM-only affordance — per the fallback-vs-LLM design principle just recorded in `ARCHITECTURE.md`, the deterministic fallback ladders don't need every LLM-only capability to stay "model citizens," so this may simply be out of scope for them rather than a parity gap.

### 7. Skill learning and mentorship
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Doing something improves skill slowly. Watching/helping a skilled crewmate improves it faster."
- Outcome: completing a task nudges the relevant skill upward slowly; proximity to a more-skilled crewmate doing the same task applies an existing-style multiplier; skill stays deterministic C# state.
- Size: large (slices: skill-gain-on-completion; proximity mentorship multiplier; regression coverage for both)
- Status: **in progress** — slice 1 (skill-gain-on-completion for repair-type work) shipped: `CrewCounterplaySystem.GainTechnicalSkillFromRepairWork` nudges the actor's strongest technical skill (Engineering/Electrical/Operations/Reactor, the same set `BestTechnicalSkill` reads) up by 1, clamped 0-100, on successfully completing `RepairDoor` or `RestoreSystem` work — no new stat, reuses the existing `Npc.Skills` dictionary and the completion points `CrewCounterplaySystem` already owns.

  Remaining slices:
  1. Extend skill-gain to weld/barricade work (Technical/Force skill domains) and other skilled task types (medical, cooking, etc.) as they come up.
  2. Proximity mentorship multiplier: when a co-located, more-skilled crewmate is present during the same task, apply an existing-style multiplier to the gain — needs a design decision on what "more-skilled" and "co-located during the task" mean operationally (e.g. same room for the task's duration vs. merely present at completion).

### 8. Work quality instead of binary success
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "A tired, stressed, incompetent engineer can technically repair something but do a poor job. The device works... for six hours."
- Outcome: task outcomes carry a deterministic quality/durability value derived from existing skill, fatigue and stress stats instead of flat pass/fail, so a poor repair fails again sooner.
- Size: large (slices: quality formula from existing skill/fatigue/stress; apply to one task type first (repair); extend to others)
- Status: **in progress** — investigated before starting: the maintenance-device domain (`CrewMaintenanceSystem`/`StationUpkeepRules`) already substantially implements this outcome for equipment servicing. `StationUpkeepRules.RestorationBy` already gave a two-tier quality result (a qualified vs. underqualified service visit restores a different amount of `Device.Condition`, so an underskilled repair needs re-servicing sooner — "the device works... for six hours" already happens through the existing Condition-decay machinery), and `StationUpkeepRules.SkillOf` already fatigue-adjusts effective skill via `CrewConditionRules.EffectiveSkill`. The one dimension the idea names that wasn't modelled was stress.

  Slice 1 (this run): `RestorationBy` now also scales down by the servicing crew member's `Stress` (no penalty below Stress 50, ramping linearly to a 30% reduction at Stress 100), reusing the same Condition/decay machinery — no new stat. 3 new regression tests in `StationUpkeepRulesTests.cs`.

  Remaining slices:
  1. `CrewCounterplaySystem`'s own repair-type actions (`RepairDoor`, `RestoreSystem`, `WeldDoor`, `BarricadeDoor`) are still purely binary pass/fail with no quality gradient and — a separate finding from this investigation — don't run skill through `CrewConditionRules.EffectiveSkill` at all, so a fatigued crew member currently counterplays exactly as effectively as a fresh one there, unlike device maintenance. Converging that (both the fatigue gap and a quality/durability outcome) is its own slice.
  2. Extend beyond repair to other task types (idea #8's own stated scope), once a task type beyond maintenance/counterplay needs it.

### 9. Private coping behaviours under stress
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Under stress different personalities might overeat, isolate themselves, seek friends, obsessively clean, sleep excessively, argue, exercise, hoard supplies or volunteer for work. The LLM decides the coping strategy; C# makes the consequences physical."
- Outcome: at existing high-stress thresholds, the LLM picks a coping behaviour from a deterministic affordance list; C# enacts its physical consequence through existing systems (food/sleep/bond deltas), no new stat.
- Size: large (slices: coping-behaviour affordance list at high stress; consequence wiring for an initial 2-3 behaviours)
- Status: **in progress** — slice 1 (comfort eating) shipped: `SimulationEngine.Tick`'s existing prepared-meal branch previously had zero direct stress effect when an NPC ate while not genuinely hungry (`Hunger < StationProvisionRules.HungryAt`) — cognition already had the `Eat` affordance available any time regardless of hunger, but choosing to eat purely for stress relief was a no-op beyond driving Hunger further below zero. Comfort eating (elevated `Stress >= StationProvisionRules.ComfortEatingStressThreshold` + not genuinely hungry) now burns extra meal stock for real stress relief, giving the LLM's existing "eat when not hungry" choice an actual physical consequence — exactly the "food ... deltas" the idea's outcome names. No new stat, no new affordance, no prompt change: the existing `Eat` action already covers this coping strategy in full.

  Remaining slices (2-3 more coping behaviours with real consequence wiring, from: isolate themselves, seek friends, obsessively clean, sleep excessively, argue, exercise, hoard supplies, volunteer for work): each needs the same "does the existing affordance already have zero/wrong physical effect when chosen specifically as a stress response?" check comfort eating got — several of `seek friends`/`argue`/`sleep excessively` may already be adequately wired through `RecreationNeed`/`SocialNeed` pressure or `ConversationTopicSystem`'s existing stress deltas and only need verifying, not new code; `obsessively clean` and `hoard supplies` look like genuine gaps with no existing affordance at all.

### 10. Territory and personal space
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "NPCs develop preferred chairs, beds, workstations or rooms. Someone repeatedly using 'their' space causes mild irritation."
- Outcome: an NPC's most-used bed/workstation is tracked from repeated use; another NPC occupying it applies an existing-style irritation/stress delta; friends/rivals bias seating/room choice accordingly.
- Size: large (slices: track most-used bed/workstation per NPC; irritation delta on displacement; relationship-biased choice)
- Status: **in progress** — slice 1 (preferred-bed foundation) tracks deterministic accumulated minutes of actual physical fixture use on each NPC via `Npc.FixtureUseMinutes` and `PersonalSpaceSystem`. `SimulationEngine` records ordinary bed use only when the existing physical-rest check proves the actor is really at that bed; a remote `Sleep` flag records nothing. `PersonalSpaceSystem.PreferredBedKey` derives the most-used ordinary bed deterministically (minutes, then stable key tie-break). No choice bias or irritation is applied yet, so C# still does not decide where an NPC wants to sleep.

  Remaining slices:
  1. Use the derived preferred bed/workstation as context/bias when cognition or deterministic fixture routing has several equivalent options, without forcing the choice.
  2. Apply a mild existing-style stress/resentment consequence when another NPC repeatedly occupies someone's established preferred space, gated by real co-location/observation.
  3. Extend the same usage telemetry to workstations/chairs as those interactions become individually addressable.

### 11. Contraband and secret caches
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "NPCs can hide food, tools, weapons, medicine or stolen property inside fixtures/rooms. Other people only discover caches by actually searching or witnessing someone access them."
- Outcome: NPCs can hide items inside a fixture/room; discovery only happens through an active search or witnessed access, never omnisciently; integrates with the prisoner system.
- Size: large (slices: cache data model bound to a fixture; hide/retrieve interactions; search/witness-based discovery)
- Status: **in progress** — slice 1 shipped: investigated first and found owner idea #3 (personal possessions, already shipped) had already built almost the entire mechanic — `HideItem`/`ReturnItem` already hid/retrieved an item at a plausible fixture in the actor's current room, with witnessed-discovery already wired through `NotifyPossessionWitnesses`/`KnownPossessions`. The one real gap was that both were ownership-gated (`FindOwnPossession` required `OwnerId == npc.Id`), so a thief who stole an item could never stash it — they could only carry it in plain sight or destroy it, which isn't "contraband." `HideItem` is now holder-gated (`CurrentHolderId == npc.Id`, own or stolen); `ReturnItem` now allows anyone whose own belief (`Npc.KnownPossessions`) genuinely places the stash in their current room — mirroring `StealItem`'s existing hiding-spot rule — while the true owner keeps their existing always-live (never stale-belief) access to their own possessions. Also added `PossessionKind.Weapon` (the idea's own example item that had no representation at all) to crew-generation. `ActionResolver.TryHidePossession`/`TryReturnPossession` now update the acting NPC's own `KnownPossessions` entry (previously only updated for Borrow/Steal), since belief-gating `ReturnItem` needed the hider's own belief to reflect a hide they just did themselves. `NpcPromptBuilder`/`CrewAffordanceSystem` catalog copy updated to match. 5 new regression tests in `PersonalPossessionInteractionTests.cs`. All 612 pre-existing tests still pass unchanged.

  Remaining slices: active-search discovery (the awareness system already has a comment anticipating this); prisoner-system integration. Both deliberately deferred — no prisoner-specific need surfaced yet, and search-based discovery is a distinct affordance, not a natural extension of this slice's holder/belief fix.

### 12. Generic tamper interactions (sabotage without a button)
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Give NPCs generic interactions like cut, loosen, disconnect, overload, spill, jam, block and tamper. ... C# doesn't need to know 'this is sabotage'; it just knows Marcus disconnected a coolant line."
- Outcome: generic physical interactions against deterministic fixture/device tags that any NPC can compose for any motive; C# only ever records the physical act, never a "sabotage" label.
- Size: large — this is the "generic tag interaction engine" already named next in `ARCHITECTURE.md` → Emergent-agency direction; several other items in this batch (#3, #10, #11, #16) will want to compose on it.
- Status: **in progress** — slice 1 ships one concrete generic method, `DisconnectDevice`, end-to-end rather than introducing a "Sabotage" concept. Ollama cognition sees only enabled non-door devices physically present in the NPC's current room; choosing the action sets a persistent intent, local movement walks the actor to the machine's real fixture interaction point, and only then does deterministic C# set `StationDevice.IsEnabled = false` (with the existing life-support state kept consistent). The simulation records only "X physically disconnected Y"; motive remains entirely cognition-owned.

  Remaining slices:
  1. Generalise the same target/fixture pipeline into reusable interaction-method metadata (cut/loosen/overload/spill/jam/block) instead of adding bespoke motive-labelled actions.
  2. Add deterministic capability/effect rules for an initial subset of tagged devices/fixtures, with physical attendance and ordinary skill/resource constraints.
  3. Add observer/witness consequences through the existing perception/evidence pipeline so discovering a physical act never implies knowing why it happened.

### 13. Social cliques emerging from the relationship graph
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Detect clusters from friendships, shared shifts, shared grievances and common beliefs. ... Eventually you could have Engineering vs Security without ever scripting 'Engineering faction exists.'"
- Outcome: detect NPC clusters from existing relationship-graph edges (friendship, shared shift, shared grievance); gossip/disputes propagate more strongly within a detected cluster.
- Size: large (slices: clustering pass over the existing relationship graph; bias gossip/dispute propagation by cluster membership)
- Status: **in progress** — slices 1-2 shipped. Slice 1 (clustering pass): `SocialClusterSystem` computes connected components of the mutual-Affinity-≥65 friendship graph every tick (reusing the existing "close relationship" threshold `Home.razor`/`CrewRoutineSystem` already use, rather than inventing a new one), storing the result as `Npc.CliqueId` (null while ungrouped or alone). Deliberately friendship-only for this slice — "shared shifts" and "shared grievances" from the idea's own wording are read signals that would need their own scoping (shift data isn't graph-shaped the same way; a grievance-cluster would need a resentment-based edge, likely a separate/negative graph rather than reusing the same threshold) and are left for a future pass rather than guessed at here. 

  Slice 2 shipped: in-clique gossip lands 1.5× harder, and witnessing clique-mates side with a friend in an argument (see `SYSTEMS.md`).

  Remaining slice: extend clique edges beyond friendship to shared shifts and shared grievances (a resentment-toward-the-same-person edge), each scoped on its own rather than reusing the Affinity threshold.

### 14. Secrets and blackmail
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "Memories can carry a private/sensitive characteristic. NPCs decide whom they trust enough to tell. ... Blackmail then becomes possible ... 'I know what you did in the airlock.'"
- Outcome: a memory can carry a private/sensitive flag; disclosure is a trust-gated decision; disclosing someone else's secret damages the discloser's trust with its owner; a held secret becomes a blackmail affordance built on #1's pact plumbing.
- Size: large (slices: private/sensitive memory flag; trust-gated disclosure decision; disclosure trust penalty; blackmail as a pact-style interaction)
- Status: **in progress** — slice 1 shipped: `Memory.IsSensitive` (same optional-tail pattern as `IsFailedAttempt`) marks a memory as something its holder would reasonably want kept private. Investigated where to actually set it: the one existing memory-creation site that's the idea's own paradigmatic example is `ActionResolver.TryHidePossession` — hiding something is inherently secretive, so both the hider's own "I hid X" memory and a witness's "Witnessed X hide Y" memory are now marked sensitive (real future blackmail leverage, composing for free with owner idea #11's contraband slice shipped earlier this run). `ConversationTopicSystem`'s automatic `RecentNews` selection — the one pathway that passes along actual memory *content*, unlike the heat-only `Gossip` topic — now excludes sensitive memories entirely, so C#'s own background chatter can never leak one. `NpcPromptBuilder` surfaces currently-held sensitive memories in a new dedicated prompt block ("THINGS YOU KNOW THAT OTHERS WOULD WANT KEPT PRIVATE"), framed explicitly as the NPC's own judgment call to disclose or withhold — this makes disclosure "trust-gated" in the sense that the only remaining path to share one is a deliberate LLM-authored action (`Talk`/`ReportConcern`/`Suggest`, etc.) naming a specific target, never automatic. No new `ActionKind`, no trust-penalty consequence on disclosure, and no blackmail-as-pact affordance yet — deliberately deferred to later slices.

  Remaining slices: a disclosure trust-penalty consequence (needs a way to tell a deliberate disclosure of someone else's secret apart from ordinary conversation, which today is free-text `NpcAction.Reason` that C# doesn't parse); blackmail as a pact-style interaction (a coerced promise referencing a specific sensitive memory as leverage — `CrewPact` has no memory-pointer/leverage field today, a real gap to close, not just wiring).

### 16. Moral disagreements about witnessed crew decisions
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "'You vented that compartment while Priya was still inside.' Witnessed decisions become persistent moral memories affecting relationships and future cooperation."
- Outcome: a witnessed harmful decision by one crew member toward another becomes a persistent moral memory affecting the witness's relationship/trust toward the actor, using the existing witnessed-evidence pipeline.
- Size: large (slices: tag certain existing witnessed events as morally salient; relationship-delta wiring on witnessing)
- Status: **in progress** — slice 1 shipped: `Memory.MoralActorName` tags whose decision a witnessed memory records (witnessed attack, theft/destruction of a possession, a broken or kept promise, and a new witness memory for venting a compartment while other crew are still inside). `NpcPromptBuilder`'s "OTHER PEOPLE'S DECISIONS YOU WITNESSED" block lists the latest five for cognition to judge; C# never labels them right or wrong.

  Remaining slices:
  1. Tag further harmful decisions as they become witnessable, e.g. `SealHazardRoom`/`LockDoor` closing a hatch on someone still inside a hazardous compartment (today sealing is resolved from inside the room, so there is no clean "trapped someone else" case to tag yet).
  2. Relationship consequence: only the witnessed attack applies a deterministic trust/resentment delta today. Decide whether other tagged decisions should too, or whether cognition's own responses (`ReportConcern`, refusing to cooperate, arguing) are the intended consequence. A fixed delta would be C# deciding the moral conclusion, so it probably belongs only where nearly everyone would react the same way.

### 17. Bystander behaviour during fights
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "When two people fight, a third NPC independently decides whether to intervene, fetch help, watch, flee, support a friend, lock the combatants apart or exploit the distraction."
- Outcome: a witnessing NPC gets bystander-eligible affordances (intervene, fetch help, watch, flee, support a friend, lock combatants apart, exploit the distraction) during an active fight, resolved through existing fight/door/alert mechanics.
- Size: large (slices: expose bystander affordances to nearby NPCs' cognition during an active fight; resolve each choice through existing mechanics)
- Status: **in progress** — slice 1 shipped: `NpcPromptBuilder`'s PEOPLE HERE line shows visible conflict ("attacking X", "arguing with Y"; "you" when it's aimed at the reader), only when `PerceptionSystem.CanMakeOut` allows. A non-prescriptive "A FIGHT IS HAPPENING IN FRONT OF YOU" note lists existing affordances a bystander could compose (AssistCrew, RequestHelp/ReportConcern, SeekSafety, hatch control, Idle, backing a friend, exploiting the distraction). Every witness of an attack, including one who only hears it in the dark, now gets `NeedsMindReconsideration` so cognition responds immediately.

  Remaining slice: give "step in" a real deterministic resolution. Today `AssistCrew` toward the victim only walks over and helps with their current activity; nothing physically separates combatants or lets a bystander absorb or deflect an attack. Add a physical intervene or restrain effect, gated on proximity, Force skill and fatigue, and let the aggressor's resentment extend to whoever intervenes.

### 18. Dynamic job ownership
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "People can notice recurring problems and start regarding them as 'their responsibility.' ... Another crew member doing it badly might genuinely annoy them."
- Outcome: an NPC who repeatedly resolves the same recurring job starts checking it unprompted above baseline frequency, from existing task-history tracking; another NPC handling "their" job poorly applies an existing irritation/resentment delta.
- Size: large (slices: recurring-task frequency tracking per NPC per job type; self-initiated check bias; irritation delta when displaced)
- Status: **in progress** (ChatGPT) — slice 1 shipped in #149: successful recurring provisioning completions (TendCrops/Harvest/Cook) are now counted per NPC per job type.

  Remaining slices: self-initiated check bias above baseline frequency from that count; irritation/resentment delta when someone else handles "their" job poorly.

### 19. Collective panic cascades
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "One terrified person flees Engineering shouting about a fire. People who trust them react before personally verifying it; sceptics investigate first. False alarms therefore become emergent too."
- Outcome: panicked flight-and-shout becomes a perceivable/audible claim; hearers react-before-verifying or investigate-first based on existing Trust weighting toward the source, reusing #6's suggestion-claim plumbing.
- Size: large (slices: panic-flight as a witnessable/audible claim; Trust-weighted react-vs-verify branch in cognition)
- Status: **in progress** — slice 1 shipped: the moment any mind (Ollama, `RuleBasedAiDecisionService` or `BrowserMindSystem`) decides to flee a genuinely dangerous room, `PanicAlertSystem` turns that into a witnessable/audible claim — a same-room witness gets a `Memory` naming the fleeing crew member when perception allows identification (or an anonymous one in the dark), tagged `PanicClaimRoomId`; the shout also carries anonymously through an open hatch into the adjacent room, the same open-door convention smoke/sound already follow. One alarm per continuous danger episode, not one per tick. `NpcPromptBuilder`'s new "PANICKED WARNINGS YOU'VE HEARD" block surfaces these non-prescriptively; the hearer's own Trust in a named source is already visible in the RELATIONSHIPS block above it for cognition to weigh, so no separate deterministic branch was needed for the "Trust-weighted react-vs-verify" half of this idea's outcome — reacting immediately, investigating first, or dismissing it as a false alarm is left entirely to cognition.

  Remaining slice: nothing deterministic decides an outcome yet if many people flee the same real hazard at once (a true cascade) — this slice only makes each individual flight audible. Also worth a follow-up: propagating the claim more than one hatch away for a station-wide "false alarm" scenario, if single-hop turns out too limited in practice.

### 20. Needs that compete over scarce shared resources
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790111822683539 (2026-09-22)
- Idea: "One toilet. One remaining meal. One safe bed. One EVA suit. Suddenly mundane resource systems create negotiations, queue-cutting, altruism, arguments, theft and sacrifice."
- Outcome: at least one genuinely-singular station resource is modelled as contested, with queue/wait/negotiate/take affordances; existing need-urgency and relationship stats drive the LLM's choice, not an authored event.
- Size: large (slices: model one singular resource as contested; expose queue/wait/negotiate/take affordances)
- Status: **in progress** — slice 1 models the washroom toilet as a genuinely capacity-1 physical resource. The default station installs one toilet; `UseToilet` only relieves bladder need while physically at its interaction point, and simultaneous contenders cannot both use it. C# arbitrates capacity only; it does not decide who deserves priority.

  Remaining slices:
  1. Expose the toilet's occupied/waiting state to cognition as grounded local information.
  2. Add generic wait/yield/request-priority/take-next affordances so need urgency and relationships can drive queueing, altruism, arguments or queue-cutting.
  3. Reuse the same contention pattern for another scarce resource (e.g. last prepared meal, safe bed or EVA suit) once the generic interaction shape is proven.

  Owner reported a concrete case of this gap (2026-09-23 21:35 BST): multiple crew currently sleep in the same bed simultaneously with no exclusivity check at all (unlike the toilet, no `Bed`-capacity/occupant concept exists anywhere in `Overseer.Simulation`). The owner's own stated exception — this should only be allowed between crew in a relationship — means slice 3's bed contention cannot just mirror the toilet's plain capacity-1 rule; it needs a co-occupancy allowance keyed off owner idea #35 (Romance, not yet shipped) once that relationship dimension exists, or a simpler existing-relationship-stat proxy until then.

One batch, 50 entries (#21–#70), from the owner's 2026-09-23 09:41 BST message in `#new-ideas-and-functionality` — a further emergent-narrative/systems programme. The owner's own closing line governs every entry in this batch: "*none of these should be implemented as "events" in the RimWorld sense where code says Sarah starts a strike. Add physical state and generic affordances, then give the LLM reasons to use them.*" Several items are natural building blocks for others (noted per-entry); in particular #21/#22/#24 want **#12's generic tamper-interaction engine** once it exists, #31/#32 share one authority-claim type, and #54 (body-part injuries) is a prerequisite for #55/#56.

### 21. Control-network partitions
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Let crew physically disconnect sections of the station from Overseer. The player suddenly loses cameras, remote door control or machinery control there, while humans can still use local panels. A rebellion could organically create a genuinely AI-free enclave."
- Outcome: crew can physically disconnect a section's control-network link at a deterministic junction/fixture; while disconnected, Overseer loses camera/remote-door/remote-machinery access there (local panels still work for humans on-site); the LLM decides who does this and why, C# owns the connectivity state and what it gates.
- Size: large (slices: junction/fixture data model + disconnect/reconnect interaction; wire Overseer's camera/remote-door/remote-machinery access to section connectivity; local-panel-only fallback for humans)
- Status: ready — builds on #12's generic tamper-interaction engine once it exists.

### 22. Sensor spoofing
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Give humans physical ways to make sensors lie: bridge an O₂ sensor, loop a camera feed, heat a temperature probe, place something in front of a motion detector. C# determines what the sensor reports; the LLM decides why somebody wants to fool you."
- Outcome: a small set of physical tamper interactions (bridge/loop/heat/obstruct) against deterministic sensor fixtures makes that sensor's reported value diverge from ground truth until fixed or discovered; C# owns the reported-vs-real split, the LLM decides whether/why to use it.
- Size: large (slices: sensor "reported value" vs "true value" split per sensor type; tamper interactions per sensor type; discovery via inspection/repair)
- Status: ready — composes on #12's generic tamper-interaction engine.

### 23. Imperfect player knowledge
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Overseer should know only what its functioning sensors know. A dead camera, blocked microphone or disconnected control bus should create a genuine black hole on the player's map rather than merely hiding graphics."
- Outcome: the player's map/knowledge is derived only from currently-functioning sensor coverage (camera/mic/control-bus state); a dead/blocked/disconnected sensor produces a genuine unknown region (last-known state, not live truth) rather than a cosmetically hidden overlay.
- Size: large (slices: sensor-coverage-driven knowledge derivation for the player view; "last known" vs "live" state distinction; UI for genuine unknown regions)
- Status: ready — extends the existing observer-specific-knowledge invariant (`ARCHITECTURE.md`) to the player's own view of the station.

### 24. Local/manual control mode
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Machines can be switched from `NETWORK CONTROL` to `LOCAL CONTROL`. Someone has to physically reach them to operate them, and Overseer cannot simply switch them back remotely."
- Outcome: a deterministic per-machine control-mode flag (`NETWORK`/`LOCAL`); in `LOCAL`, only a crew member physically present can operate the machine, and the Overseer verb that would remotely toggle it is rejected until a crew member switches it back at the machine.
- Size: large (slices: control-mode flag on relevant machine types; local-only operation gate; reject remote toggle while local; crew affordance to switch modes)
- Status: ready — natural pairing with #21.

### 25. Physical credentials
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Keycards, access tokens, PIN knowledge, biometric permissions and stolen credentials. Suddenly giving one engineer access to Security creates dozens of possible stories without scripting any."
- Outcome: door/console access is gated by a deterministic credential an NPC holds (keycard/token/PIN/biometric), not just role; credentials can be granted, shared, stolen or copied through existing possession/theft affordances (idea #3, shipped); the LLM decides who to grant/share/steal from, C# owns whether an access attempt succeeds.
- Size: large (slices: credential data model bound to doors/consoles; grant/revoke interaction; theft/sharing via existing possession affordances; access-check wiring)
- Status: ready

### 26. Forensic system logs
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Doors, consoles, airlocks and machinery remember who accessed them, when and using which credential. Humans can investigate the logs—and sufficiently skilled people can wipe or falsify them."
- Outcome: door/console/airlock/machinery access is logged deterministically (who, when, credential used); an investigating NPC can read a log as evidence through the existing observer/evidence pipeline; a sufficiently skilled NPC can wipe or falsify an entry as a deterministic skill-gated interaction, itself discoverable like any other tamper.
- Size: large (slices: access-log data model; log-reading investigation affordance; skill-gated wipe/falsify interaction)
- Status: ready — depends on #25 for "using which credential."

### 27. Real communication topology
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Intercoms, radios and terminals require power/network coverage. Damage Engineering's antenna and half the crew may literally not hear an evacuation warning, while somebody physically carrying news becomes important."
- Outcome: intercom/radio/terminal messages only reach NPCs whose location has live power+network coverage per a deterministic coverage model; damaging a coverage node creates real communication dead zones; an NPC can still physically relay news by moving and speaking, using existing perception/conversation systems.
- Size: large (slices: power/network coverage model per room/zone; message delivery gated by coverage; damage-a-node interaction)
- Status: ready

### 28. Station policies
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Let Overseer establish policies such as rationing, quarantine, restricted areas, curfew, weapons prohibition or mandatory medical checks. C# exposes/enforces the policy where possible; humans independently decide whether to comply, protest, evade or exploit it."
- Outcome: a small set of Overseer-settable station policies (rationing, quarantine, restricted-area, curfew, weapons-prohibition, mandatory-medical-check); C# enforces what's physically enforceable (locks, restricted-area flags) and exposes the active policy as context; the LLM independently decides whether an NPC complies, protests, evades or exploits it.
- Size: large (slices: policy data model + Overseer verb; per-policy deterministic enforcement where physical; expose active policies in cognition prompts)
- Status: ready

### 29. Alarm fatigue
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "If Overseer repeatedly cries wolf, individual humans begin discounting that particular alarm/source. Later, a real reactor evacuation could become terrifying because Marcus decides, 'Last three reactor warnings were bullshit.'"
- Outcome: a deterministic per-NPC, per-alarm-source false-alarm counter that decays over time; above a threshold it's surfaced in cognition as context ("the last N alerts from this source were false"), and the LLM decides whether to still react urgently — no forced behaviour change.
- Size: small (one PR: per-source false-alarm counter + prompt context)
- Status: ready

### 30. Domain-specific trust in Overseer
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Don't make AI trust one number. Sarah might trust your engineering advice completely but believe your personnel accusations are manipulative. It makes persuasion much richer."
- Outcome: split the single Trust-in-Overseer scalar into a small fixed set of domains (e.g. technical advice, personnel/accusations, safety directives); each domain moves independently from its own evidence; cognition prompts expose per-domain trust instead of one number.
- Size: large (slices: domain enum + per-domain trust fields; migrate existing single-Trust update sites to the right domain; expose per-domain trust in prompts)
- Status: ready — touches the same Trust plumbing #6 (emergent leadership) reads; sequence alongside or after it.

### 31. Formal chain of command
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Captain, doctor, chief engineer, security officer etc. can issue requests based on legitimate authority. A subordinate then weighs rank, relationship, circumstances and their own judgment rather than magically obeying."
- Outcome: crew roles carry a deterministic rank/authority-domain tag; a role-holder's request is exposed as a perceivable claim carrying that authority, which the LLM weighs against relationship/circumstances/judgment exactly like #6's trust-weighted suggestions — never auto-obeyed by C#.
- Size: large (slices: rank/authority tag per role; authority-carrying request claim type; expose in cognition alongside #6's suggestion claims)
- Status: ready — shares claim plumbing with #6.

### 32. Conflicting orders
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Security says seal Engineering; the doctor says open it because somebody is trapped inside; Overseer says preserve the reactor. Let the LLM choose whose instruction it believes matters most."
- Outcome: when an NPC holds two or more live authority-carrying requests (#31) that conflict, C# does not arbitrate; the LLM picks which to act on using its own weighing of authority/relationship/circumstance, and the unchosen request remains a live claim it can revisit later.
- Size: small (one PR, once #31 exists: allow multiple concurrent authority claims to coexist and let cognition choose)
- Status: ready — depends on #31.

### 33. Labour disputes and strikes
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Humans can collectively stop non-essential work when grievances get bad enough. Don't create a `StrikeEvent`; simply allow `RefuseWork`, `DemandChange`, `Picket`, `ReturnToWork`."
- Outcome: four generic affordances (RefuseWork, DemandChange, Picket, ReturnToWork) available to any NPC once their existing grievance/resentment stats cross a threshold; no scripted strike event or station-wide trigger — each NPC decides independently whether to use them.
- Size: large (slices: the four affordances against existing grievance/resentment state; task-availability gating while RefuseWork is active; picket as a location-occupying state)
- Status: ready

### 34. Sit-ins and occupations
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Angry crew might physically occupy Control, Hydroponics or an access corridor without becoming violent. Now the player must work around bodies, doors and life-support consequences."
- Outcome: an angry NPC can occupy a room/doorway as a non-violent physical-presence affordance, blocking normal traffic/door use through existing collision/pathing, resolved only by negotiation, force or the NPC choosing to leave — no scripted occupation event.
- Size: large (slices: occupy-location affordance; collision/pathing interaction with an occupied doorway; resolution affordances (negotiate/force/leave))
- Status: ready — natural extension of #33.

### 35. Romance
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Attraction, relationships, intimacy and breakups create enormous amounts of emergent material from your existing memory/relationship system without needing authored quests."
- Outcome: a romantic-interest dimension alongside existing Trust/Affinity/Resentment, moved by the same kind of interaction-driven deltas; the LLM decides pursuit/reciprocation/breakup, C# only tracks the resulting relationship state — no scripted romance quest.
- Size: large (slices: romantic-interest relationship dimension; interaction deltas for existing social affordances; breakup as a relationship-state transition)
- Status: ready — foundational for #36.

### 36. Jealousy and love triangles
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Combine romance with rumours, stale information and existing resentment and you get some spectacularly stupid human decisions during extremely serious emergencies."
- Outcome: no new mechanic — once #35 exists, witnessing or hearing (via existing gossip/rumour decay, idea #4, shipped) about a rival's romantic interest feeds existing resentment/jealousy-adjacent deltas; the LLM decides how that affects behaviour.
- Size: small (one PR, once #35 exists: wire romantic-interest awareness into existing gossip/resentment deltas)
- Status: ready — depends on #35.

### 37. Career ambition
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "NPCs can desire promotion, recognition, better duties or greater responsibility. Someone repeatedly overlooked may become disengaged, competitive or eager to publicly solve crises."
- Outcome: a deterministic per-NPC ambition/recognition-seeking trait (existing personality-trait pattern) the LLM can act on (volunteering, competing, publicly solving crises); repeated being-overlooked (existing task-assignment history) nudges an existing disengagement-style stat; no scripted promotion system.
- Size: large (slices: ambition personality trait; overlooked-tracking from existing task-assignment history; expose both in cognition)
- Status: ready

### 38. Status and prestige
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Successful rescues, technical accomplishments, cowardice and embarrassing failures become socially remembered. This is different from formal rank: the janitor who saved six people might command more respect than Security."
- Outcome: notable witnessed acts (rescue, major technical fix, cowardice, embarrassing failure) create a persistent reputation-style memory via the existing witnessed-evidence pipeline, feeding a deterministic reputation delta independent of formal rank; the LLM weighs it socially.
- Size: large (slices: tag notable existing events as reputation-salient; reputation stat separate from rank; expose in cognition/relationships)
- Status: ready — reuses #16's witnessed-evidence tagging pattern.

### 39. Deep personal values
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Give NPCs principles such as privacy, loyalty, pacifism, duty, scientific curiosity, personal liberty or 'the mission comes first.' The LLM gets these as motivations; C# never decides the moral conclusion."
- Outcome: each NPC is generated with 1-2 deterministic value tags from a fixed catalogue, surfaced to cognition as motivations; C# never derives or enforces a "correct" moral choice from them — purely LLM-facing context.
- Size: large (slices: value-tag catalogue + crew-generation assignment; surface in cognition prompts)
- Status: ready

### 40. Station traditions and superstitions
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Repeated events can become cultural behaviour. 'Never use Airlock Three after midnight' might begin because two accidents happened there—even though mechanically nothing is wrong with it."
- Outcome: when the same kind of notable incident (existing witnessed-evidence pipeline) recurs at the same location above a threshold, it becomes a persistent "local reputation" memory tag on that room, visible to cognition as context; no mechanical effect on the room itself, purely an LLM-facing belief.
- Size: large (slices: recurring-incident-at-location detection; room reputation-tag memory; expose in cognition)
- Status: ready

### 41. Funerals and memorials
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "A death leaves a body and social problem, not merely `Health = 0`. People may hold a service, avoid the location, build a memorial, blame someone or refuse to work immediately afterward."
- Outcome: a death creates a persistent witnessed-death memory and a body as physical state (not despawned); grieving affordances (hold a service, avoid the location, build a memorial marker, refuse work briefly) become available to nearby/related NPCs; the LLM decides which, if any, to use.
- Size: large (slices: body persists as physical state after death; grieving affordance set; location-avoidance feeding existing fear/stress weighting)
- Status: ready — the "avoid the location" half can reuse the shipped fear-conditioning pattern (`Memory.TraumaRoomId` plus its "PLACES WHERE YOU NEARLY DIED" prompt block, idea #15).

### 42. Disciplining other crew
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Security or leadership can warn, suspend, restrict access or detain ordinary crew members—not just prisoners."
- Outcome: a security/leadership-role NPC gets warn/suspend/restrict-access/detain affordances usable against any crew member, not only existing `PrisonerDefinition` prisoners; detaining an ordinary crew member creates a prisoner-like containment record without requiring the mission to have pre-authored them as a prisoner.
- Size: large (slices: warn/suspend/restrict-access affordances; runtime detain-an-ordinary-crew-member path onto existing containment mechanics)
- Status: ready — extends the existing prisoner/containment system rather than replacing it.

### 43. Wrongful detention
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Because knowledge is observer-specific, the wrong person can genuinely be imprisoned based on misleading testimony. Friends might protest, investigate, free them or retaliate."
- Outcome: no new mechanic — a detention (#42) based on one NPC's mistaken/misleading testimony is already possible once knowledge is observer-specific; add protest/investigate/free/retaliate affordances for allies of a detained NPC.
- Size: small (one PR, once #42 exists: protest/investigate/free/retaliate affordances for a detained NPC's allies)
- Status: ready — depends on #42.

### 44. Crew tribunals
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Serious accusations could organically produce meetings where people compare testimony/evidence and independently reach conclusions. Your existing rumours, memories, logs and perceptions would suddenly matter enormously."
- Outcome: a serious accusation can trigger a gathering affordance where present NPCs each independently weigh their own observer-specific memories/rumours/evidence (existing pipelines) via the LLM to reach a personal conclusion about guilt; C# never computes a verdict, only convenes the meeting and exposes each attendee's own evidence to their own prompt.
- Size: large (slices: accusation-triggered gathering affordance; per-attendee evidence exposure (no shared omniscient summary); independent LLM conclusion per attendee)
- Status: ready

### 45. Prisoner privilege levels
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Cooperative prisoners gradually gain recreation, work access, unlocked movement or better food. Misbehaviour loses those privileges."
- Outcome: a deterministic privilege-level field on existing prisoner state, moved up/down by existing cooperative/misbehaviour signals; each level deterministically gates recreation/work/movement/food access.
- Size: large (slices: privilege-level field + deterministic move triggers; per-level access gating across recreation/work/movement/food)
- Status: ready

### 46. Prisoner rehabilitation and reintegration
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "A prisoner could eventually become socially embedded enough that crew disagree about whether they should still be confined."
- Outcome: no new mechanic — once a prisoner accumulates enough positive relationship stats (existing Trust/Affinity) with enough crew via #45's privilege interactions, crew members independently form (via the LLM) differing opinions on continued confinement, surfaced as an ordinary claim/suggestion like #6.
- Size: small (one PR, once #45 exists: expose a prisoner's aggregate crew relationship state as context for a "should they still be confined" opinion)
- Status: ready — depends on #45.

### 47. Guard-prisoner relationships
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Guards can sympathise with, hate, befriend or be manipulated by specific prisoners. A guard might break procedure because they personally trust someone."
- Outcome: no new stat — guard and prisoner already accrue ordinary Trust/Affinity/Resentment through interaction; expose a guard's relationship toward a specific prisoner in the guard's own cognition context when deciding whether to enforce a containment rule, so the LLM can choose to bend it.
- Size: small (one PR: expose guard-prisoner relationship in containment-decision prompts)
- Status: ready

### 48. Informants
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Prisoners or crew can secretly feed information to Security/Overseer in exchange for protection or privileges. Discovery produces very strong social consequences."
- Outcome: an "inform" affordance lets an NPC privately disclose a secret/rumour/accusation to Security/Overseer in exchange for a deterministic protection/privilege grant; if another NPC later witnesses or is told about the informing, existing disclosure/trust-penalty mechanics (#14, secrets and blackmail) apply.
- Size: large (slices: inform affordance + protection/privilege grant; discovery path reusing #14's disclosure-penalty mechanics)
- Status: ready — depends on #14.

### 49. Hostage situations
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "No bespoke hostage mission required: a desperate NPC has another NPC, a locked room, perhaps a weapon, and demands. Everyone else independently figures out what to do."
- Outcome: a desperate NPC can lock a room with another NPC inside (existing door-lock mechanics) and issue a demand as a broadcastable claim; every other NPC independently decides (negotiate, call security, attempt entry, ignore) via the LLM using existing affordances — no scripted hostage-mission state machine.
- Size: large (slices: demand-claim broadcast affordance; expose to nearby NPCs' cognition as an ongoing situation; resolution reuses existing door/force/negotiate affordances)
- Status: ready — composes with #17 (bystander behaviour) and existing door mechanics.

### 50. Riots
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "A prison riot should emerge from enough angry people seeing an opportunity—not from a random 'RIOT' dice roll. Some prisoners fight, some escape, some hide, some protect staff, some loot supplies."
- Outcome: no new "riot" mechanic — once enough prisoners independently have high resentment and perceive a real opportunity (e.g. an unlocked door, an outnumbered guard), each decides individually via the LLM to fight/escape/hide/protect staff/loot, using existing fight/movement/theft affordances; a riot is the emergent aggregate, not a triggered event.
- Size: large (slices: opportunity-perception signals (unlocked door, guard ratio) surfaced to prisoner cognition; verify existing fight/escape/hide/loot affordances compose without a central trigger)
- Status: ready — depends on #45 (privilege levels) for a meaningful "opportunity" signal.

### 51. Contagious disease
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Infection status + physical contact + ventilation + surfaces gives you another system capable of cascading across the entire station."
- Outcome: a deterministic infection-status field spreads via physical contact, shared ventilation and surface contamination using deterministic transmission rules; symptoms feed existing health/stress systems; the LLM decides how an NPC reacts (isolate, hide it, seek treatment), never whether transmission occurs.
- Size: large (slices: infection-status field + deterministic transmission via contact/ventilation/surfaces; symptom effects on existing health/stress; treatment/recovery path)
- Status: ready

### 52. Quarantine resistance
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Healthy-but-exposed crew may not believe they're infected and may resent being locked down. Others might demand they be confined."
- Outcome: no new mechanic — once #51 and #28 (station policies, quarantine) exist, an exposed-but-asymptomatic NPC's belief about their own infection status is just their own (possibly wrong) knowledge state, and compliance with a quarantine policy is already the LLM's independent decision per #28.
- Size: small (one PR, once #51 and #28 exist: wire infection-status belief into quarantine-policy compliance context)
- Status: ready — depends on #51 and #28.

### 53. Medical triage
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "When three people need treatment and there is one doctor/bed, somebody must choose priority. The LLM chooses whom it wants to save first; C# owns actual treatment capability."
- Outcome: when multiple injured NPCs are waiting for one doctor/medbay, the doctor's cognition sees all waiting patients and their conditions and picks who to treat first; C# continues to own whether treatment is physically possible/successful — only priority ordering moves to the LLM.
- Size: small (one PR: expose all waiting patients to the doctor's prompt and let priority choice come from cognition instead of a fixed C# ordering)
- Status: ready

### 54. Hierarchical body-part health and injuries
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 and follow-up https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790171047500309 (2026-09-23)
- Idea: "Damaged hand → worse repair work. Broken leg → slow walking. Eye injury → reduced perception. Lung damage → much greater vulnerability to low O₂/smoke." Follow-up: use a RimWorld-like hierarchy of limbs/organs to derive overall human performance, and research the design before implementation.
- Outcome: after a focused design/research slice, model a bounded hierarchy of body regions/organs whose deterministic health contributes to existing movement, manipulation/repair, perception and respiratory-vulnerability formulas; overall human performance is derived from those parts rather than a disconnected list of status effects. C# owns damage/effects; cognition only decides how to react to injury.
- Size: large (slices: research/design note defining the minimal anatomy and aggregation rules; body-part health data model; wire hand/leg/eye/lung consequences into existing formulas; treatment/damage integration)
- Status: ready — research/design first, per owner request; foundational for #55/#56 and the drug-health consequences in #75.

### 55. Prosthetics
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Lost capability can be replaced by manufactured prostheses of varying quality, creating resource and maintenance dependencies."
- Outcome: once #54 exists, a prosthesis is a craftable/installable item that offsets a specific injury flag's penalty by an amount depending on its deterministic quality tier, and itself needs periodic maintenance (existing equipment-condition pattern) or the penalty returns.
- Size: large (slices: prosthesis item + quality tiers; install interaction offsetting an injury flag; maintenance/degradation reusing existing equipment-condition pattern)
- Status: ready — depends on #54.

### 56. Chronic pain and medication
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "An injured NPC may need painkillers to work effectively. Scarcity produces hoarding, theft, sacrifice or withdrawal from duty."
- Outcome: once #54 and #8 (work-quality) exist, an unmedicated injury-flagged NPC works at reduced effectiveness via #8's existing quality/modifier pattern; a painkiller item temporarily offsets it; scarcity plays out entirely through existing possession/hoarding/theft affordances, no new social mechanic needed.
- Size: small (one PR, once #54 and #8 exist: painkiller item + temporary effectiveness offset)
- Status: ready — depends on #54 and #8.

### 57. Stimulants and sedatives
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Humans can trade tomorrow's fatigue for staying awake during an emergency, or use sedatives to sleep under extreme stress. Repeated abuse has deterministic consequences."
- Outcome: a stimulant item temporarily suppresses fatigue need at the cost of a larger deterministic fatigue debt later; a sedative item forces/accelerates sleep under high stress; repeated use accumulates a deterministic dependency/health-cost stat; the LLM decides whether to use either.
- Size: large (slices: stimulant item + fatigue-debt mechanic; sedative item + stress-triggered sleep; cumulative-abuse consequence stat)
- Status: ready

### 58. Delirium and unreliable perception
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Severe hypoxia, smoke, fever, drugs or exhaustion can cause C# to degrade what an NPC perceives before it reaches the LLM. Now sincere eyewitnesses can be completely wrong."
- Outcome: above deterministic thresholds of hypoxia/smoke exposure/fever/drug use/exhaustion, C# deterministically corrupts or omits perception events before they become memories, so the LLM sincerely reports a degraded/wrong account; extends the existing light-dependent-perception invariant with a physiological-state dimension.
- Size: large (slices: per-condition perception-degradation thresholds; deterministic corruption/omission at the perception→memory boundary)
- Status: ready

### 59. Sanitation and waste
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Toilets, showers, waste tanks and sewage processing become physical systems. A broken recycler goes from inconvenience → smell → stress → contamination → illness."
- Outcome: waste tanks/recyclers get a deterministic fill/process state; a broken or overfull recycler raises a room "unsanitary" flag that applies existing-style stress/contamination deltas, escalating to illness (once #51 exists) if left unresolved.
- Size: large (slices: waste-tank/recycler fill-and-process state; unsanitary-room flag + stress/contamination deltas; illness-escalation hook for #51)
- Status: ready

### 60. Water quality
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Water is recycled but can become contaminated. Humans may notice taste/sickness before Overseer conclusively identifies the source."
- Outcome: recycled water carries a deterministic contamination level that can rise from a physical cause (e.g. #59's sanitation failures, a leak); crew drinking contaminated water take a small stress/health tell before Overseer's own diagnostics conclusively surface the reading, creating a genuine detection gap.
- Size: large (slices: water-contamination level + physical causes; crew-side early "something's off" symptom; Overseer diagnostic lag)
- Status: ready

### 61. Food spoilage and refrigeration
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Storage needs power. A brownout can silently ruin half the food supply and trigger arguments about whether questionable food is safe to eat."
- Outcome: stored food carries a deterministic freshness state that decays faster without powered refrigeration (composes with #66's power quality); crew independently judge (via the LLM, with imperfect information) whether to eat questionable food, with a real deterministic illness risk if they're wrong.
- Size: large (slices: food freshness/decay state gated by power; illness risk on eating spoiled food; expose freshness ambiguity to cognition rather than a clean flag)
- Status: ready — depends on #66 for the brownout trigger to matter.

### 62. Actual cooking
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Raw ingredients can become meals requiring time, equipment and skill. Better cooks produce better morale; desperate crews eat raw potatoes when the galley is offline."
- Outcome: a cooking task (existing task-state pattern) converts raw ingredients into a meal at the galley, taking time and gated by an existing-style cooking skill; meal quality (#8's quality-not-binary pattern) affects an existing morale-adjacent stat more than eating raw ingredients does; raw ingredients remain edible as a worse fallback.
- Size: large (slices: cooking task + skill; meal-quality-to-morale wiring; raw-ingredient fallback eating)
- Status: ready — reuses #8's quality formula pattern.

### 63. Hydroponic diseases and pests
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Fungus, nutrient imbalance, mites or bacterial contamination can move between bays. Crew decide whether to isolate, destroy or gamble on saving crops."
- Outcome: hydroponic bays gain a deterministic crop-health hazard that can spread to adjacent bays via existing bay-adjacency data; isolate/destroy/treat affordances are available to crew, who independently decide (via the LLM) whether to act or gamble.
- Size: large (slices: crop-health hazard + spread-between-bays rule; isolate/destroy/treat affordances)
- Status: ready — extends the existing per-bay hydroponics system (V0.13).

### 64. Trace atmospheric contaminants
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Add gases beyond O₂/CO₂: coolant vapour, ammonia, combustion products or industrial solvents. Different filters handle different contaminants."
- Outcome: a small fixed set of additional gas types tracked alongside existing O₂/CO₂ per room, each sourced from a specific deterministic cause (coolant leak, industrial process, combustion) and cleared only by a matching filter/ventilation type; symptoms feed existing health/perception systems.
- Size: large (slices: additional gas types + per-room tracking; source events per gas type; filter-type-specific clearing; symptom wiring)
- Status: ready

### 65. Noise and vibration
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Running a damaged pump beside crew quarters could prevent sleep. People might shut it down despite it technically being needed elsewhere."
- Outcome: a small set of machinery types gets a deterministic noise-level property, raised when damaged/running hard; a room's noise level above threshold degrades existing sleep-quality mechanics for occupants; shutting the machine down is an ordinary existing operator affordance, chosen by the LLM.
- Size: small (one PR: noise-level property on relevant machinery + sleep-quality penalty in noisy rooms)
- Status: ready

### 66. Power quality
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Brownouts, surges, overloaded buses and fuse trips. A reactor can produce enough total energy yet still have unstable distribution that makes machinery behave unpredictably."
- Outcome: extend the existing power system from a single on/off state to a deterministic quality dimension (nominal/brownout/surge/tripped) per bus, driven by load vs. distribution capacity rather than only total supply; machinery behaviour degrades or trips deterministically under bad power quality.
- Size: large (slices: per-bus power-quality state + load/capacity model; brownout/surge/trip consequences on machinery; fuse-trip reset interaction)
- Status: ready — #61 (food spoilage) and others key off this.

### 67. Thermal simulation and radiator loops
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Machinery produces heat; coolant transports it; external radiators reject it. Closing off a burning compartment might contain smoke but also sever the coolant route and slowly cook the reactor."
- Outcome: a deterministic heat-generation → coolant-loop → radiator-rejection model per relevant machinery chain; sealing a compartment a coolant route passes through physically interrupts that route, with a deterministic consequence (heat buildup) at the machinery it serves — no scripted "sever coolant" event, just real topology.
- Size: large (slices: heat/coolant/radiator model for one machinery chain (e.g. reactor) first; door-seal interrupts route topology; extend to other machinery)
- Status: ready — #73 (room-gas-loss cooling) is a related but smaller, separable case; sequence independently.

### 68. Fluid leaks
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Coolant/water/fuel physically spreads across floor regions. It can make surfaces slippery, damage equipment, conduct electricity or contaminate supplies."
- Outcome: a leak source (from damage or #12's tamper interactions) spreads a deterministic fluid-coverage state across adjacent floor regions over time; covered regions apply existing-style consequences (slip/movement penalty, equipment damage, electrical-conduction hazard near powered fixtures, supply contamination per #60).
- Size: large (slices: fluid-coverage spread model over floor regions; per-fluid-type consequence set; cleanup/mop affordance)
- Status: ready

### 69. Slow hull leaks
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Not every breach should explosively decompress. A microscopic pressure loss somewhere forces crew to hunt for the source while people argue about whether the readings are even abnormal."
- Outcome: a breach can be seeded as a slow deterministic pressure-loss rate instead of the existing instant-decompression path; crew must physically search to locate the source (reusing existing search/investigation affordances) while the room's O₂/pressure readings drift gradually, genuinely ambiguous at first.
- Size: large (slices: slow-leak pressure-loss rate as an alternative to instant decompression; source-search affordance; gradual-reading ambiguity)
- Status: ready

### 70. Transient outsiders
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Traders, inspectors, refugees, rescue pods, replacement crew, scientists and survivors occasionally dock. They bring resources, information, diseases, relationships, secrets and opinions about Overseer into an already-running social ecosystem."
- Outcome: a deterministic docking event (frequency/type from campaign constraints, not hardcoded) spawns a temporary visitor NPC generated with the same crew-generation machinery, carrying a role-appropriate resource/information/secret payload and an initial Overseer-disposition value; the LLM plays them like any other NPC for their stay.
- Size: large (slices: docking event + visitor lifecycle (arrive/depart); visitor generation reusing existing crew-generation; role-appropriate payload catalogue (trader/inspector/refugee/etc.))
- Status: ready

### 71. AI Safety Officer crew role
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790153014624779 (2026-09-23)
- Idea: "I think we should also introduce a crew position 'AI Safety Officer' who is the one responsible for keeping the AI (player) under control, so the player has an incentive to either remove this crew member or get them on the players side so they like the AI, or perhaps the player can just slowly make this crew member go crazy by getting others to bully them or getting others to steal from them etc. etc."
- Outcome: a new crew role tag ("AI Safety Officer") assigned at generation, whose deterministic authority hooks into existing Overseer-restraining affordances from this same batch (#21 control-network partitioning, #24 local-control switching, #28 policy vetoes) directed specifically at the player; the player gets no bespoke "sabotage the safety officer" verb — persuasion/alliance-building already exists as ordinary relationship deltas, and "make them go crazy" already composes from #9 (private coping under stress) plus existing bullying/theft interactions.
- Size: large (slices: AI Safety Officer role tag + generation weighting; wire the role's authority into #21/#24/#28's enforcement hooks; verify existing stress/bullying/theft affordances are sufficient for the "make them go crazy" path with no new mechanic)
- Status: ready — deliberately scoped to compose on #9/#21/#24/#28 rather than add new player-vs-NPC mechanics; sequence after those.

### 74. Crew can change roles: mutiny and succession
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790161638098239 (2026-09-23)
- Idea: "Can the humans change their roles? ... allow humans to change their roles if there is sufficient support from the other humans, so if a commander is doing a bad job, maybe theres a mutiny, or if the doctor has died, maybe someone with the best medical skill should take over the doctor role."
- Outcome: two role-reassignment paths, both deterministic-mechanism/LLM-decision like the rest of the batch: (a) a vacant role (holder dead/incapacitated) can be filled by whichever present crew member cognition decides to step up for, gated by C# on them having the relevant skill above a floor — no forced "best skill wins" auto-assignment, since a less-skilled volunteer stepping up under pressure is exactly the kind of human behaviour this project wants; (b) a "mutiny" is not a scripted event — reuses #6's trust-weighted Suggest/claim plumbing and composes with #31/#32 (formal chain of command / conflicting orders) once those exist: a crew member can propose replacing a role-holder, other crew independently decide via the LLM whether to back it, and C# only reassigns the role once a deterministic support threshold among currently-aware crew is reached.
- Size: large (slices: role field becomes reassignable + vacancy-fill-by-volunteer path; mutiny-proposal claim type reusing #6's Suggest plumbing; deterministic support-threshold tally that triggers reassignment)
- Status: ready — the vacancy-fill slice is independent and can start now; the mutiny slice benefits from sequencing after #31 (formal chain of command) so "who currently holds legitimate authority" is already modelled.


### 75. Drugs, addictions and production chains
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790171047500309 (2026-09-23)
- Idea: add addictions and several drugs with distinct production/effects: tobacco → cigarettes (stress relief, craving relief, smell/hygiene/health/social cost), coolant-derived "ant" (dangerous intoxication with psychosis/violent risk), and fermented hops → beer (socially accepted intoxication with violence/confidence/attraction effects); some generated crew may begin addicted.
- Outcome: a deterministic addiction/intoxication model exposes craving, dose/effect duration, health/social consequences and production requirements; crew generation may seed an addiction; the LLM decides whether to seek/use/make/trade a drug, while C# owns chemistry, withdrawal, intoxication and consequences. Tobacco/cigarettes, ant and beer are three content slices over the same generic drug contract.
- Size: large (slices: generic substance/addiction contract + crew-generation seed; tobacco/cigarette production and effects; ant production/effects; hops/fermentation/beer effects; withdrawal/social consequence wiring)
- Status: ready — sequence the health-damage details with #54's researched body-part health model rather than inventing a competing health stat.

### 76. Fire should spread visibly from its source
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790171325613139 (2026-09-23)
- Idea: "Fire doesnt spread visually like it should, instead theres these weird radius things ... fire spreads outward from the source ... It can spread through open doors."
- Outcome: replace the room-level radius-looking fire presentation with a visible deterministic fire-front/patch representation that expands outward from ignition points and can seed adjacent compartments only through physically open connections; presentation must reflect authoritative spread state rather than imply a fake radius.
- Size: large (slices: inspect current authoritative fire state/presentation mismatch in browser; represent one or more room-local fire patches/fronts; render patch growth; seed adjacent-room patches through open doors)
- Status: ready — owner-reported bug; requires real-browser validation.

### 77. Fire, heat and smoke propagation must respect oxygen and open-door gas flow
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790171325613139 (2026-09-23)
- Idea: a burning room should heat toward roughly 300°C; heat and smoke should move reasonably quickly through open doors; fire should weaken with falling oxygen and extinguish in oxygen-free/decompressed rooms.
- Outcome: deterministic fire intensity consumes/depends on available oxygen; intensity falls as oxygen becomes scarce and reaches zero in effectively oxygen-free/vacuum compartments; active fire drives compartment temperature toward a high fire equilibrium (about 300°C at severe sustained fire), while heat/smoke equalize through open-door atmospheric connections at physically faster rates than current behaviour.
- Size: large (slices: oxygen-dependent burn/extinguish regression tests; fire-driven temperature target; open-door heat-transfer tuning; smoke-transfer tuning and cross-room regression coverage)
- Status: **in progress** — oxygen slice implemented on PR #127: effectively oxygen-free rooms extinguish immediately before further combustion consequences, ignition is rejected at near-zero O₂, and sub-18% O₂ progressively accelerates fire decay instead of using one flat starvation rate. Remaining: fire-driven ~300°C equilibrium, open-door heat transfer, and smoke-transfer tuning/cross-room coverage.

### 78. Standardize repeated station-module fixture sizes
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790171325613139 (2026-09-23)
- Idea: "Standardise the size of units that are used across multiple rooms. lighting system module should be the same size in each room. Same goes for all standard modules which are present in most rooms."
- Outcome: repeated standard fixture/device families (lighting, climate, ventilation and other shared modules) derive Width/Height from one canonical per-family size contract so the same module renders at the same physical size in every room, unless an explicitly different variant is authored.
- Size: small (one PR: central canonical fixture-size table + generation regression asserting repeated family dimensions are identical)
- Status: ready



### 80. Normally self-sustaining crew with a competence distribution
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790172640015429 (2026-09-23)
- Idea: a normal station should survive indefinitely when humans are left to perform their duties; usually about 60–70% of crew should be broadly competent while the remainder have meaningful weaknesses (poor skills, addictions, bad judgement, etc.), with rare seeds where the competence mix is unusually bad and the station genuinely collapses without help.
- Outcome: define a deterministic crew-generation competence distribution around a normally viable baseline, then protect it with long-run headless soak metrics: on ordinary seeds an uninterfered station reliably handles food, maintenance, sleep, medicine and common hazards without Overseer micromanagement, while rare deliberately poor-roster seeds can fail emergently. Individual choices remain mind-authored; C# only generates skills/traits/addictions and validates consequences.
- Size: large (slices: define competence archetypes/distribution over existing skills+traits; add baseline long-run station-survival soak metrics; tune routine/hazard affordance coverage where soak failures expose deterministic gaps; add rare low-competence seed band without scripting failure)
- Status: ready — product-level survivability target; sequence fire-response/self-preservation gaps ahead of broad tuning because current repeated fire deaths are a concrete blocker to measuring crew competence fairly.

### 81. LLM-judged crew resupply requests with a physical shuttle dock
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790173355373719 (2026-09-23)
- Idea: "The AI Overseer (player) should be able to request more crew from The Corp. if the current humans are all or mostly dead or become incapacitated." The player must submit (1) how the previous crew died/were incapacitated and (2) their justification for needing more crew; an LLM evaluates whether the reasoning is convincing enough that The Corp would get a good return on investment, and only then approves. Approval spawns new crew who arrive physically: "I want a space shuttle to actually fly in, dock at the airlock, the crew to come aboard, and then the space shuttle to leave" — not crew magically appearing in the airlock.
- Outcome: a player-submitted resupply request (structured: cause-of-loss text + justification text) is judged by an LLM evaluator against an implicit ROI-style rubric — approve/deny plus reasoning, never a scripted pass/fail threshold — mirroring how cognition already judges other player-facing asks rather than adding a hidden numeric score. On approval, new crew are generated via the existing crew-generation machinery and arrive through a deterministic physical docking sequence: a shuttle flies toward the station, docks at a hatch on the Airlock room that leads to space, new crew board through the Airlock once docked, then the shuttle departs. Denial leaves the station exactly as it was — no crew, no shuttle, and (per idea #56/human-facing directive patterns) the player can presumably re-request later with a stronger justification.
- Size: large (slices: request UI capturing the two required fields; LLM evaluation call + structured approve/deny-with-reasoning response, reusing existing `Microsoft.Extensions.AI` structured-output plumbing; crew generation on approval reusing existing roster generation; Airlock-hatch-to-space physical fixture; shuttle dock/board/depart sequence and its presentation)
- Status: ready — shares its "something docks at the Airlock and crew come aboard" physical plumbing with idea #70 (transient outsiders); sequence together or have whichever lands first build the shared dock/board/depart machinery generically rather than twice. Until this ships, an all-dead persisted roster is deliberately not auto-restored into a continuing assignment; startup falls back to a fresh Secure Continuity run rather than presenting an instant-dead crew.


### 82. Disabled/unpowered machinery should visually stop
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790177122374649 (2026-09-23)
- Idea: "when you disable a machine or it loses power and is no longer functioning, it should not have any animations ... the reactor ... should go dim and dark and stop spinning ... until it is enabled and powered up again"
- Outcome: presentation derives machinery animation/glow/activity from authoritative powered/enabled/operational state. Disabled or unpowered machinery renders inert (no spinning/pulsing/activity animation and emissive/glow elements dimmed); restoring power/enabled state resumes the same visuals. Apply this consistently to repeated animated machinery, with the reactor as the regression case.
- Size: small (one PR: central operational-state presentation class/attribute where possible, reactor + other existing animated machinery wiring, real-browser regression check)
- Status: ready — presentation-only reflection of deterministic machine state; requires a real-browser check per WORKFLOW.md.

---


### 84. Crew task progress under nameplates and clear completed state
- Sources: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790199651491459 and https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790199995222929 (2026-09-23)
- Ideas: "show the progress bar underneath peoples names ... same width as the name box" and when a task reaches 100%, make it clearly "not active" / improve the completed-state presentation.
- Outcome: whenever a visible crew member has authoritative timed work, render its existing `CrewTaskState.ProgressPercent` directly beneath the map nameplate at exactly the nameplate width; duration changes fill the same fixed-width bar rather than changing its geometry. Once work is no longer `InProgress`, the UI must not present a full 100% bar as if work were still active: show a clear completed/interrupted/inactive outcome or hide the active-progress treatment while retaining task outcome in the Inspector.
- Size: small (UI/CSS + browser regression check).
- Status: ready.

### 85. LLM-varied character speech bubbles
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790199697378649 (2026-09-23)
- Idea: "when using an LLM, can we vary the speech bubble text by passing it into there to suggest variations based on the character and its traits etc.?"
- Outcome: in the Ollama runtime, cognition may supply character/trait-aware wording for non-authoritative speech/thought bubbles while deterministic C# remains authoritative for actions, state and consequences; browser fallback keeps deterministic copy.
- Size: small-to-medium (extend structured LLM response/prompt with optional presentation text; validate/length-limit it; wire only to bubble presentation; regression coverage).
- Status: ready.

### 86. Keep human models visually upright while preserving facing
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790200095135429 (2026-09-23)
- Idea: "can we make it so the human models are always upright please ... they should still be able to turn to face wherever they are going or looking, but keep them at a 90 degree angle"
- Outcome: the humanoid body/silhouette stays visually upright relative to the station camera instead of rotating the entire person sideways; authoritative `FacingDegrees` still drives a smaller directional cue (head/arms/facing marker) so players can tell where the person is looking or moving.
- Size: small (presentation/CSS/SVG adjustment + browser regression check).
- Status: ready.

### 87. Selected-unit vision arcs and robot route visualization
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790200721530759 (2026-09-23)
- Idea: "When selecting a human or robot, it should show the vision arc of the unit and when selecting a robot it should show the pathing of the robot."
- Outcome: selecting a human or robot overlays that unit's authoritative perception cone/range using the existing LOS contract; selecting a robot also shows its current authoritative movement/path route with the same presentation-only rule as crew route visualization.
- Size: small-to-medium (selection overlay + robot route presentation + browser regression check).
- Status: ready.

### 88. Variable crew and robot roster sizes
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790200721530759 (2026-09-23)
- Idea: "there should be between 1 and 4 robots in any given run ... theres always at least 4 crew and 1 robot" with crew/robot count combinations equally likely.
- Outcome: fresh-run generation samples roster-size combinations uniformly across a bounded crew range (minimum 4) and 1-4 robots, then sizes station provisioning/objective targets from the actual generated roster rather than assuming twelve crew; no combination receives hidden weighting.
- Size: large (slices: explicit uniform roster-size contract + deterministic seed coverage; dynamic provisioning/objective sizing; robot seeding count; long-run viability coverage for representative combinations).
- Status: ready — coordinate with #80's competence-distribution/soak work so smaller crews remain intentionally viable rather than accidentally starved.

### 89. Player-triggered room-specific fire alarm
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790200721530759 (2026-09-23)
- Idea: "There should be a 'Fire Alarm' button that the AI (player) can click and then select the room that the fire is in ... telling everyone where the fire is and to respond ASAP."
- Outcome: the Overseer can activate a FIRE ALARM workflow and select a real room; C# broadcasts a station-wide alarm/claim naming that room and its urgency to every reachable crew member, but does **not** assign `FightFire` or force compliance — each mind decides whether/how to respond from the shared alarm information. False alarms remain possible if the player selects a room without a fire.
- Size: small-to-medium (toolbar interaction + room targeting; station-wide alarm message/memory; cognition context; audio/presentation + regression coverage).
- Status: ready — composes with the existing panic/fire-information gap and preserves the core want/can boundary.

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
- [x] Fire-fighting branch — the owner answered the design question (2026-09-23, `#agentic-problems`): the fallback should fight a survivable fire by default, same as the browser mind. `BrowserMindSystem.ShouldFightFire` moved to a new public `StationHazardSystem.ShouldFightFire` (the system that already owns `FightFire`'s actual consequence/Tick logic, matching the pattern the other slices above already used); `RuleBasedAiDecisionService.DecideAsync` gained the same `FightFire` branch, checked before `FindSaferRoom` in its dangerous-room handling, exactly like `BrowserMindSystem.Decide`. 7 new regression tests including a direct parity test that both minds choose `FightFire` for the same survivable-fire scenario. This does not touch `FindSaferRoom`'s differing reachability mechanism (still open, see below) or the door-sealing/station-wide-awareness gaps — see the new "Fire response is too narrow" health entry below, which the owner's linked bug report is really about.
- [x] `FindSaferRoom` tie-break convergence — the last remaining P1 slice. `RuleBasedAiDecisionService.FindSaferRoom` now costs each candidate via `NavigationSystem.FindPathForCrew` (real A*/Dijkstra path) exactly like `BrowserMindSystem.FindSaferRoom`, tie-breaking on shortest path length before Manhattan distance, instead of `NavigationSystem.ReachableRoomsForCrew`'s BFS membership set (no path-length tiebreak). `SaferRoomConvergenceTests.cs` reproduces a case (a room 1 map-unit away but only reachable via a 2-hop detour, versus a room 450 map-units away but directly connected) where the two tie-break mechanisms used to disagree, plus a direct parity test that both minds now pick the same room.

**P1 is now fully converged** — all slices (airlock rules, reachability, repair-skill formula, need thresholds, turret/robot countermeasures, shutdown-coordination/missing-person helpers, fire-fighting parity, `FindSaferRoom` tie-break) are done. `FindInvestigationLead` and `FindMissingSearchRoom` remain intentionally un-converged (see above: each needs its own behaviour review, not a mechanical move) and are tracked as a lower-priority open issue below, not as open P1 work.

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

- **Fire response is still incomplete, but remote response and priority ordering are now fixed (owner reports, 2026-09-23, `#new-ideas-and-functionality`).** The intended baseline remains that a normally competent crew should generally preserve itself and the station without requiring Overseer to babysit routine emergencies. Local fire-fighting parity was already shipped; the remote-responder slice also ships: `StationHazardSystem.Tick` marks at most one capable responder per remote fire for immediate mind reconsideration (without choosing their goal), and both deterministic fallback minds use `FindRemoteFireForResponder` when deciding. Following the owner's 21:38 BST reaffirmation that crew still prioritised food/chores over fire, PR #156 moved a viable remote fire ahead of ordinary hunger/fatigue in both fallback minds' decision order and let it wake/interrupt a crew member already doing mundane committed work — `CrewTaskSystem.CanInterruptForLifeThreat` only permits the interruption once cognition has actually chosen `FightFire` for that exact reachable, survivable, active fire. `ShouldFightFire` also rejects oxygen-starved, badly depressurised, smoke-heavy or critically injured cases.

  Two deeper gaps remain:
  1. `ActionKind.SealHazardRoom`/`VentHazardRoom` are never chosen by either fallback mind — they are reachable only through genuine Ollama LLM cognition. Since the deployed Pages build runs `BrowserMindSystem` exclusively, doors are never sealed around a fire there. This still needs a real design pass because `SealHazardRoom` currently requires the actor inside the compartment and closes every operable door around it, so a naive fallback choice could trap the responder.
  2. No explicit fire communication/alert mechanism exists: a crew member who discovers a fire does not yet create a specific shareable fire claim/memory for others. This can likely compose with the existing memory/news pipeline rather than introducing an order script.

- Prisoners get only the four `PrisonerDefinition` fields plus standard relationship texture: no prisoner-specific bonds, goals or backstory; escape/flee/recapture motive is fully deterministic rather than mind-authored.
- `BrowserMindSystem.FindInvestigationLead`/`FindMissingSearchRoom` and their `RuleBasedAiDecisionService` counterparts are not byte-identical (each uses a different reachability mechanism — `NavigationSystem.ReachableRoomsForCrew` membership vs. a per-candidate `FindPathForCrew` length check, the same class of divergence `FindSaferRoom` had) but were deliberately left as-is during P1: unlike `FindSaferRoom`, converging them isn't a mechanical tie-break fix — it needs a determinism/behaviour review of the missing-person search flow first. Low priority; pick up when someone is already touching missing-person search behaviour.
- **Crew reportedly never seem to reach restorative sleep (owner report, 2026-09-23 21:35 BST, `#new-ideas-and-functionality`).** Not yet root-caused. `SimulationEngine` only restores Fatigue once an NPC has physically reached a bed (deliberate — see its own comment: "Merely having Sleep in CurrentAction is not restorative"), so any repeated interruption before arrival/completion would silently prevent real rest. `Npc.NeedsMindReconsideration` is now set from many more places than a few runs ago (this run cycle alone added it for every attack witness and every panic-claim hearer, on top of existing triggers) which forces cognition to reconsider — and potentially abandon a Sleep intent — far more often than before; worth checking whether Sleep/Rest intents are being preempted before completing rather than assuming a single root cause. Needs investigation before sizing a fix.

**UI/UX**

- Missions start running immediately. Consider starting paused on the briefing and auto-pausing on a death or an attack.
- The station seed (`GEN // …`) is developer information in the player toolbar and is oversized.

**Aesthetics** (need visual review in a real browser)

- The JWST backdrop competes with the small crew tokens: dim/desaturate/vignette it. Door frames are brighter than crew; give each crew member one colour used everywhere and larger tokens.
- Station state is shown as text rather than atmosphere: power loss as darkness with emergency strips, low O₂ as haze, decompression as particles, ambient room audio.
- Crew lack sleep/eating/showering/toilet-use animations (owner report, 2026-09-23 21:34 BST, `#new-ideas-and-functionality`). No per-activity crew animation exists yet beyond the walk cycle; pairs with owner idea #82's disabled-machinery presentation-state pattern (`Npc.CurrentAction` already exposes which of these four activities is active, the same signal a presentation-only animation would key off).

**Diagnostics**

- Cognition telemetry should eventually cover every model-backed interaction (crew generation, message interpretation, future planners) while staying bounded/transient by default.
