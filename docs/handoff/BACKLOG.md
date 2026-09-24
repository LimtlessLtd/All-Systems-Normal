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
- Also (owner, 2026-09-24 14:45, https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790257508295699): "The LLM never seems to make concrete actionable decisions, just suggestions ... the LLM should respond with an array of actions like [\"MoveTo(GeneratorRoom)\",\"FightFireAtLocation(GetFireLocationInRoom())\",\"RepairMachineCausingTheFire(FindMachineInRoomBelowServiceThreshold())\"] ... if there isnt [a fire] just skip to the next action ... perhaps it can also set conditions for certain actions to be performed on per human." This is the same idea, made concrete. The owner's version adds three things to the outcome: (a) the model returns an ordered list of catalogue actions instead of one decision; (b) a step may name a *deterministic target resolver* instead of a literal ID (e.g. "the fire in this room", "the most worn machine in this room"), resolved by C# when the step starts; (c) a step may carry a simple *skip/run condition* from a fixed catalogue (e.g. "only if there is a fire here"), evaluated by C# when the step comes up. Steps whose condition is false are skipped, not failed. The model authors the list and the conditions; C# only resolves, validates and executes. Resolvers and conditions are a closed, typed catalogue in the prompt, never free-form code.
- Size: large (slices: bounded plan/step data model; per-step deterministic validation reusing existing intent checks; re-plan-on-failure feedback loop; plan-shaped Ollama response + prompt catalogue; target resolvers; step conditions)
- Status: **in progress**, owner-prioritised (the 14:45 message says single decisions read as "suggestions", not actions). Take it right after #97 (condensed prompt): a plan-shaped response needs the prompt budget #97 frees. Slice 1 (bounded plan/step data model) shipped. `ARCHITECTURE.md` → Emergent-agency direction's planned sequence placed "bounded plans/triggers/goal predicates" *after* a "rejection feedback" step; that prerequisite (`Memory.IsFailedAttempt` as an explicit typed marker, surfaced in a dedicated "YOUR RECENT FAILED ATTEMPTS" prompt block) was fixed in an earlier PR, unblocking this idea.

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
- Status: **in progress** — slices 1–3 shipped. Slice 1 added one concrete generic method, `DisconnectDevice`, end-to-end rather than introducing a "Sabotage" concept. Slice 2 generalised that path into `PhysicalInteractionRules`: motive-neutral `PhysicalInteractionMethod` metadata declares the method/action/target type plus deterministic target requirements (non-door device, local room, enabled, working, fixture-backed), and one shared resolver feeds cognition target normalisation, prompt-visible local targets and execution/rejection feedback. Slice 3 adds perception-grounded witnesses: the actor remembers the physical disconnect, and only co-located crew who can actually make the actor out get a memory that they witnessed that named physical act; those memories contain no sabotage/motive label and carry no automatic moral/relationship penalty, leaving interpretation to cognition. `DisconnectDevice` remains the only exposed method.

  Remaining slice:
  1. Add deterministic capability/effect rules for an initial subset of additional interaction methods against tagged devices/fixtures, with physical attendance and ordinary skill/resource constraints.

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

  Bed contention now has its first deterministic capacity slice: `BedUseRules` gives each concurrent sleeper in a room one distinct physical bed in stable order, and excess sleepers receive no restorative bed rather than sharing one. Relationship-based co-sleep remains deliberately unimplemented until #35 provides an explicit romance/relationship state; capacity-1 is the safe baseline and does not invent a relationship from generic Trust/Affinity. Queue/wait/yield behaviour remains cognition-facing follow-up work.

One batch, 50 entries (#21–#70), from the owner's 2026-09-23 09:41 BST message in `#new-ideas-and-functionality` — a further emergent-narrative/systems programme. The owner's own closing line governs every entry in this batch: "*none of these should be implemented as "events" in the RimWorld sense where code says Sarah starts a strike. Add physical state and generic affordances, then give the LLM reasons to use them.*" Several items are natural building blocks for others (noted per-entry); in particular #21/#22/#24 want **#12's generic tamper-interaction engine** once it exists, #31/#32 share one authority-claim type, and #54 (body-part injuries) is a prerequisite for #55/#56.

### 21. Control-network partitions
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Let crew physically disconnect sections of the station from Overseer. The player suddenly loses cameras, remote door control or machinery control there, while humans can still use local panels. A rebellion could organically create a genuinely AI-free enclave."
- Outcome: crew can physically disconnect a section's control-network link at a deterministic junction/fixture; while disconnected, Overseer loses camera/remote-door/remote-machinery access there (local panels still work for humans on-site); the LLM decides who does this and why, C# owns the connectivity state and what it gates.
- Size: large (slices: junction/fixture data model + disconnect/reconnect interaction; wire Overseer's camera/remote-door/remote-machinery access to section connectivity; local-panel-only fallback for humans)
- Status: **in progress** — slice 1 shipped on the existing station-wide control-network rack: crew can physically `DisconnectDevice` on `network:control`; after the ordinary upkeep dependency pass, `GameState.ControlNetworkOnline` goes false, all room camera-network reachability goes dark, and `StationDeviceControlSystem` rejects Overseer remote machinery toggles. The disabled rack is exposed to cognition as a `RestoreSystem` target and crew can re-enable it locally from Control; motive remains cognition-owned. Remaining: (1) split the single station-wide rack into section/junction connectivity so enclaves can be partial rather than all-or-nothing; (2) gate Overseer remote hatch control by the same section connectivity; (3) ensure every player-facing sensor/map read respects section reachability (#23 will deepen imperfect-player-knowledge semantics).

### 22. Sensor spoofing
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790152880944719 (2026-09-23)
- Idea: "Give humans physical ways to make sensors lie: bridge an O₂ sensor, loop a camera feed, heat a temperature probe, place something in front of a motion detector. C# determines what the sensor reports; the LLM decides why somebody wants to fool you."
- Outcome: a small set of physical tamper interactions (bridge/loop/heat/obstruct) against deterministic sensor fixtures makes that sensor's reported value diverge from ground truth until fixed or discovered; C# owns the reported-vs-real split, the LLM decides whether/why to use it.
- Size: large (slices: sensor "reported value" vs "true value" split per sensor type; tamper interactions per sensor type; discovery via inspection/repair)
- Status: **in progress** — slice 1 establishes the reported-vs-physical split for room O₂ and temperature without changing environmental truth: `Room.OxygenPercent` / `TemperatureC` remain authoritative physics, nullable sensor-channel overrides feed `ReportedOxygenPercent` / `ReportedTemperatureC`, and the station console (values plus O₂/temperature health colouring) reads the reported channels so a later deterministic physical tamper can genuinely fool Overseer without changing the atmosphere. Remaining: physical bridge/heat interactions tied to real sensor fixtures; camera looping / motion obstruction; inspection/repair that discovers and clears a manipulated channel; extend reported-vs-real semantics to any additional sensor type as each interaction ships.

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
- Status: **in progress** — slice 1 (vacancy fill) is shipped; see `SYSTEMS.md` → role succession. Remaining: (2) the mutiny proposal, reusing #6's Suggest plumbing; (3) the deterministic support tally that triggers reassignment. Both benefit from sequencing after #31 (formal chain of command), so "who currently holds legitimate authority" is already modelled. Known limits of slice 1: a post only opens through a found body, so a holder lost to space (no body) or long missing never opens it. A post someone leaves by stepping up has no dead holder, so nobody can refill it. Both are candidates for slice 2's claim path rather than more deterministic rules.


### 75. Drugs, addictions and production chains
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790171047500309 (2026-09-23)
- Idea: add addictions and several drugs with distinct production/effects: tobacco → cigarettes (stress relief, craving relief, smell/hygiene/health/social cost), coolant-derived "ant" (dangerous intoxication with psychosis/violent risk), and fermented hops → beer (socially accepted intoxication with violence/confidence/attraction effects); some generated crew may begin addicted.
- Outcome: a deterministic addiction/intoxication model exposes craving, dose/effect duration, health/social consequences and production requirements; crew generation may seed an addiction; the LLM decides whether to seek/use/make/trade a drug, while C# owns chemistry, withdrawal, intoxication and consequences. Tobacco/cigarettes, ant and beer are three content slices over the same generic drug contract.
- Size: large (slices: generic substance/addiction contract + crew-generation seed; tobacco/cigarette production and effects; ant production/effects; hops/fermentation/beer effects; withdrawal/social consequence wiring)
- Status: ready — sequence the health-damage details with #54's researched body-part health model rather than inventing a competing health stat.

### 76. Fire should spread visibly from its source
- Sources: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790171325613139 (2026-09-23) and https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790242588232969 (2026-09-24)
- Idea: "Fire doesnt spread visually like it should" and latest playtest: room fire renders as a long east-west rectangle with only ~50px north-south spread.
- Evidence: the 2026-09-24 "long east-west rectangle" was a CSS geometry bug, now fixed. The fire overlay shares each room's `::after` with the decorative floor stripe, whose fixed `height` (4%) over-constrained the overlay's `inset`, so flames drew as a full-width strip 4% of the room tall (headless Chromium: 15px tall in a 395px room). The overlay now resets height, width, opacity and shadow and fills the compartment, guarded by `FireOverlay_ResetsTheDecorativeStripeGeometryItReuses`.
- Outcome: replace the room-level radius-looking fire presentation with a visible deterministic fire-front/patch representation that expands outward from ignition points and can seed adjacent compartments only through physically open connections; presentation must reflect authoritative spread state rather than imply a fake radius.
- Size: large (slices: inspect current authoritative fire state/presentation mismatch in browser; represent one or more room-local fire patches/fronts; render patch growth; seed adjacent-room patches through open doors)
- Status: **in progress**. Shipped: the geometry bug; an authoritative origin plus growing front, with spread seeded at the hatch; and suppression that needs the front in reach (see `SYSTEMS.md` → fire). The first two were browser-checked on live fires. A 20-seed × 36h soak was identical to main (0 deaths of 240, same fire-minutes). Remaining: (1) a fire is still one front per room, so a second ignition source in an already-burning room doesn't start a second patch (the map's flames, `FireFrontRules.FlameSprites`, would follow a multi-patch front automatically); (2) the front has no memory once intensity drops, so a fire being beaten back shrinks toward its origin rather than leaving scorched ground.

### 77. Fire, heat and smoke propagation must respect oxygen and open-door gas flow
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790171325613139 (2026-09-23)
- Idea: a burning room should heat toward roughly 300°C; heat and smoke should move reasonably quickly through open doors; fire should weaken with falling oxygen and extinguish in oxygen-free/decompressed rooms.
- Outcome: deterministic fire intensity consumes/depends on available oxygen; intensity falls as oxygen becomes scarce and reaches zero in effectively oxygen-free/vacuum compartments; active fire drives compartment temperature toward a high fire equilibrium (about 300°C at severe sustained fire), while heat/smoke equalize through open-door atmospheric connections at physically faster rates than current behaviour.
- Size: large (slices: oxygen-dependent burn/extinguish regression tests; fire-driven temperature target; open-door heat-transfer tuning; smoke-transfer tuning and cross-room regression coverage)
- Status: **in progress** — oxygen slice implemented on PR #127: effectively oxygen-free rooms extinguish immediately before further combustion consequences, ignition is rejected at near-zero O₂, and sub-18% O₂ progressively accelerates fire decay instead of using one flat starvation rate. Remaining: fire-driven ~300°C equilibrium, open-door heat transfer, and smoke-transfer tuning/cross-room coverage.

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


### 88. Variable crew and robot roster sizes
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790200721530759 (2026-09-23)
- Idea: "there should be between 1 and 4 robots in any given run ... theres always at least 4 crew and 1 robot" with crew/robot count combinations equally likely.
- Outcome: fresh-run generation samples roster-size combinations uniformly across a bounded crew range (minimum 4) and 1-4 robots, then sizes station provisioning/objective targets from the actual generated roster rather than assuming twelve crew; no combination receives hidden weighting.
- Size: large (slices: explicit uniform roster-size contract + deterministic seed coverage; dynamic provisioning/objective sizing; robot seeding count; long-run viability coverage for representative combinations).
- Status: **in progress** — slices 1–2 shipped (see `SYSTEMS.md` → crew rosters): uniform roster contract in both runtimes, the robot seeding count, and provisioning (`PlannedCrewCount` → hydroponics sizing) taken from the roster actually aboard. "Keep crew alive" objective targets were already taken from the actual roster. Remaining: (3) long-run viability coverage for representative combinations, together with #80. A quick headless browser-mind soak (6 seeds × 24h, before slice 2) showed no deaths for 4×1, 4×4 and 8×2. A 60-seed browser-mind soak of sampled compositions (Secure Continuity, full 8h window, 2026-09-24, after the breach-sealing and restorable-outage fixes) had 0 deaths among 506 crew. 300 explicit Pages seeds with sampled compositions all pack. Hydroponics is marginally short of `RequiredHydroponicsCapacity` (worst 91%, logged as HYDROPONICS UNDERSUPPLY) on 6/300 sampled seeds. For comparison, the pre-#88 always-12-crew station was short on 16/300. That's pre-existing packing marginality: tune it with #80 rather than inflating the clamp. `MaxCrew` stays 12, the owner's largest example; asked in the idea thread whether larger crews are wanted.

### 90. Sit down to eat at visible, room-themed tables and chairs
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790201540463659 (2026-09-23)
- Idea: "humans should seek somewhere to sit down and eat/drink, so we need to make visible chairs and tables (perhaps different colours/materials for medical bays vs crew quarters vs recreation room) in the appropriate places in rooms, and they add like a comfort modifier or stress reduction when eating food. If they dont find somewhere to sit and eat, they should get a small stress increase."
- Outcome: dining-capable rooms (kitchen, recreation, quarters; medical as bedside) get enough visible `Table`/`Chair` fixtures (they already exist in `FixtureType` with `FixtureUsePose.Sit`) with per-room-type material/colour styling. When cognition chooses `Eat`, deterministic execution resolves the nearest free reachable seat the same way it resolves a bed, and a seat is capacity-1 (reusing #20's contested-resource pattern). Eating while seated applies a small stress-relief/comfort consequence; eating standing because no seat was free/reachable applies a small stress increase. C# never decides *whether* to eat — only where the body sits and what that costs. The seat state is visible to cognition in the room description so minds can choose to wait, eat elsewhere or eat standing.
- Size: large (slices: seat fixtures + per-room styling in the default layout, browser-checked; seat resolution/occupancy for Eat + seated/standing stress consequence with regression tests; expose free/occupied seats to cognition)
- Status: **in progress**. Slices 1–3 are shipped (see `SYSTEMS.md` → seated eating): capacity-1 seat resolution for `Eat`, the seated/standing stress consequence, cognition `SEATS:`/`DINING:` lines, carrying a meal from the galley to a recreation room or crew quarters, and medical bedside eating. A mind may now choose Medical as the carried-meal destination; real `MedicalBed` fixtures count as capacity-1 bedside meal places, and a bed physically occupied by a resting patient is unavailable to the eater. Fallback minds still only take a meal to Recreation when the galley is full, so C# does not decide that a patient should eat in Medical. Remaining: (1) more chairs and tables in the lounge/quarters and per-room chair/table materials and colours, browser-checked; (2) a seated/bedside eating pose for crew tokens, if the current token doesn't already read correctly. Note: the deterministic hunger routine (`CrewRoutineSystem.ChoosePlan`) still always heads for the galley, so routine eaters stand when it is full; only mind-chosen `Eat` intents carry meals elsewhere. The routine sets a room plan, not an intent, so it can't do the collect-then-carry step. Leave it unless soaks show much standing.

### 91. Per-crew event log of what changed their stats
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790201540463659 (2026-09-23)
- Idea: "We should display each event which affects each humans various stats like an event log for each human you select."
- Outcome: each `Npc` keeps a bounded (e.g. last 40) ring of stat-change entries — timestamp, stat (Health/Stress/Hunger/Fatigue/Morale/Trust/relationship…), signed delta and a short cause ("ate a meal", "witnessed Kim attack Rao", "smoke inhalation") — recorded at the deterministic consequence sites, with per-tick continuous drift (hunger/fatigue growth) coalesced so it doesn't flood the list. Selecting a crew member shows this log in the Inspector. Presentation/diagnostic only; not persisted across campaign saves (see Deliberate decisions → persistence).
- Size: large (slices: bounded log model + coalescing + wiring the highest-impact consequence sites (damage, eating, sleep, social/witness stress) with tests; Inspector panel with browser check; progressively cover remaining sites)
- Status: **in progress**. Slices 1–2 are shipped (see `SYSTEMS.md` → per-crew stat log): the model, coalescing, the Inspector card, and every simulation write to Health/Stress/Fear/Hunger/Fatigue, guarded by a source test. Remaining, **needs owner input**: should relationship changes (Trust/Affinity/Resentment toward a named person) and the routine needs (hygiene/bladder/recreation/social/intimacy) also appear? Asked in the idea thread. The routine needs would mostly add drift lines, so they're left out until the owner says.

### 92. Television and more recreational activities
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790201540463659 (2026-09-23)
- Idea: "Add a television to the recreation room and increase number of recreational activities."
- Outcome: the recreation room gets a visible TV fixture (the room already has a `Screen`; make it a watchable TV with its own interaction point) and the `Recreate` affordance family grows to several distinct physical activities (watch TV, play the recreation console, cards/board game at the table with others, read on the sofa), each tied to a real fixture/capacity and with slightly different recreation/social/stress consequences. Cognition chooses which activity; C# validates the fixture is free/reachable and applies the consequence.
- Size: medium-to-large (slices: TV fixture + WatchTV activity; additional solo activities; a multi-person social activity that composes with relationships)
- Status: **in progress**. Slice 1 is shipped (see `SYSTEMS.md` → recreation activities): a wall-mounted Television in the lounge, and `Recreate` activities `watch-tv`, `play-games` and `read`, each tied to its fixture with its own consequences. Shared TV watching eases the social need, and a dead TV or console gives no break. Remaining: (1) a multi-person social activity at the lounge table (cards/board game) that composes with relationships; (2) more solo activities, possibly in other rooms (quarters desk reading, galley radio); (3) crew-token poses/animation for the activity, alongside the open "activity animations" aesthetics item.

### 93. Gym room with weightlifting and boxing
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790201540463659 (2026-09-23)
- Idea: "Perhaps we should add a gym room or something where some crew like security can do weight lifting and have boxing matches (which could result in real lasting injuries and rivalries)."
- Outcome: a new `Gym` room type in generated layouts with weights and a boxing ring; exercise is a mind-chosen activity that relieves stress and slowly raises a physical/Security-relevant skill (composes with #7 skill learning); a boxing match is a consensual two-party interaction (one proposes, the other's mind accepts or declines) resolved deterministically from skills/fatigue, producing real injuries (composing with #54 body-part health once it exists; flat Health damage until then) and relationship/rivalry changes for both participants and witnesses.
- Size: large (slices: Gym room + layout generation; weightlifting activity; consensual sparring proposal/acceptance plumbing; match resolution + injuries + rivalry consequences)
- Status: ready — the injury half should reuse #54's health model if it lands first rather than inventing another.

### 94. Designated smoking area in the recreation room
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790201540463659 (2026-09-23)
- Idea: "There should be a smoking area in the recreation room where people can smoke cigarettes without a social debuff."
- Outcome: some generated crew have a smoking habit (a craving need that grows over time, composing with #75's addiction model); cognition may choose to smoke anywhere, but smoking outside the recreation room's marked smoking area gives a small relationship/annoyance debuff with non-smokers who perceive it (and a trace smoke contribution to the room), while smoking inside the area carries no social penalty. Relief of the craving and stress are deterministic consequences.
- Size: medium (slices: smoking trait + craving need + Smoke activity/consequences; smoking-area fixture + social-debuff-by-location rule; cognition context)
- Status: ready — implies smoking outside the area *does* carry a social debuff; implemented that way unless the owner says otherwise. Sequence with #75's addiction model.

### 95. Crew must not occupy the same physical floor position
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790242588232969 (2026-09-24)
- Idea: "Humans shouldnt be able to stand directly on top of each other."
- Outcome: authoritative destinations that can attract multiple humans must allocate distinct physical use/wait points rather than letting bodies settle at identical coordinates. Fix this at contested destination/resource assignment sites (beds, toilets, queues, work fixtures) rather than making all nearby crew dynamic pathfinding obstacles.
- Size: medium (resource-specific destination allocation + queue/wait positions as those affordances ship).
- Status: **in progress** — fixed at the shared destinations rather than with a global crowd-collision layer (a generic post-movement separation experiment was rejected earlier because it perturbed the decompression soak). Shipped: distinct beds (#96), medical beds shared between sleepers and bedside eaters (one occupancy query), distinct shared fixtures, and conversation spacing (see `SYSTEMS.md` → crew spacing). In a 10-seed × 24h measurement, pair-minutes of stationary crew within 0.35 map units of each other fell from 6,826 to 2,156. Remaining: idle crew collide on the six name-hashed `PersonalIdlePoint` slots (807 pair-minutes, mostly Move+Move waiting in corridors). Allocating distinct idle slots cut the total to ~300, but it was held back. Adding six extra slots near the walls cost 7–8 deaths over 60 seeds × 36h, against 1 on main and 3 for a neutral hash change (excluding seed 15's wipe). Allocating among the original six broke the `DecompressionContainmentTests` soak seed: someone was ejected from the breached room itself. Both perturb chaotic fire/breach trajectories. Retry together with #80 once breach recovery exists, so a perturbed seed isn't read as a regression.

### 96. Sleeping crew should visibly use distinct beds
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790242597079429 (2026-09-24)
- Idea: "humans dont actually sleep on their beds they just stand there doing nothing, and they keep standing on top of each other too."
- Outcome: a sleeping NPC physically occupies a specific available bed interaction/pose point and presentation shows a lying/sleeping pose instead of an upright idle token; unrelated sleepers cannot share the same bed/point. Any relationship-based co-sleep exception composes with #20 rather than bypassing bed capacity.
- Size: medium (bed occupancy assignment + sleep pose/presentation + browser/regression coverage).
- Status: **in progress**. Shipped: distinct bed capacity (#188), and sleepers drawn lying on their assigned bed (see `SYSTEMS.md` → personal space / beds; browser-checked on the Pages build). Remaining: relationship-based bed sharing waits for #35. Audit of #188 (Claude, 2026-09-24): a 20-seed × 36h 12-crew browser-mind soak, before vs after: no deaths either way; mean fatigue 27.9 → 28.8; time at fatigue ≥ 90 0.25% → 0.60%; 2.2% of sleep-minutes had no free bed. Bed claims are keyed by fixture identity since #191, so duplicate labels no longer collapse capacity. The default Quarters' six "Double Bunk" fixtures now hold one sleeper each; if a double bunk should sleep two, give it two bed points rather than relaxing capacity. Rest and Intimacy still head for the room's first bed, unassigned.

### 97. Condense what the NPC prompt feeds the LLM
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790257508295699 (2026-09-24)
- Idea: "We should condense the information we are feeding into the LLM as at the moment we are feeding it a lot of info. I think we should also be feeding the LLM a bunch of actions that it can choose from like i describe in 2." (The action-list half is merged into #5.)
- Evidence: `NpcPromptBuilder.Build` for a fresh default station is ~33.6k characters (~8–10k tokens), before memories accumulate. By section: capability/target contracts ~10.1k, preamble rules ~8.2k, station status-panel room readings ~5.4k (every room), topology ~5.1k (every room/door), everything else ~4.8k. That overflowed the old 8192-token `num_ctx`, so Ollama silently dropped the start of the prompt (the rules). The shipped stop-gap raised `num_ctx` to 16384 (see `SYSTEMS.md` → Ollama decision budget), which costs the local machine more KV-cache memory and prompt-processing time per decision.
- Outcome: the prompt carries what this person can know and act on now, compactly: one line per available action with its valid targets, instead of every contract; room readings and topology for this person's room, its neighbours and rooms with a known problem, not every room; the preamble rules shortened without losing any invariant a test guards. The largest default-station prompt drops well under ~16k characters, and `NpcPromptBudgetTests` then tightens `ContextWindowTokens` back toward 8192. No capability is removed from the model, and anything it needs about a distant room still reaches it via memories, claims and status panels it knows.
- Size: large (slices: capability section → compact per-action lines; status/topology → local + known-problem rooms only; preamble tightening; lower `num_ctx` and tighten the budget test)
- Status: **in progress**. Slice 1 shipped: actions with no valid target for this person right now are left out of the capability list, together with their target contracts (~33.6k → ~30.4k characters). Slice 2 shipped: rooms whose readings are all ordinary show as `nominal` (defined once in the section header), and topology lines drop the atmosphere link implied by the hatch state and the default "you can traverse" (→ ~25.6k). Slice 3 shipped: preamble paragraphs about a situation this person isn't in (robots, turrets, malware, pending pact/suggestion, own promises, shutdown controls/teams, airlocks needing securing, missing people, investigation leads) appear only when it applies (→ ~21.5k). See `SYSTEMS.md` → Ollama decision budget. Next: shorten the capability descriptions and the remaining always-on preamble (~7k + ~4k), then lower `num_ctx` toward 8192. Before lowering it, extend `NpcPromptBudgetTests` to a mid-game state with full memories, pacts and threats, not only fresh stations. Every room still appears in status and topology, because the minds use the whole layout for evacuation/sealing plans. Only filter rooms if the owner wants that. Bullet 1 of the same message (decision output cap 300 → 750) was pushed by the owner directly; the `num_ctx` stop-gap and its budget test shipped alongside.

### 98. Perception-first prompt: what the person sees, plus a labelled action table
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790261247385829 (2026-09-24)
- Idea: "we need to come up with a better approach with more condensed information and more freedom given to the LLM ... look up how other people have worked on similar problems ... give the LLM the humans: name, stats, personality, recent events, and then attempt to draw an ascii version of what they see, and then give the LLM a large table of actions it could reply with in a JSON formatted array ... each room can be labelled, and so can each hallway, piece of furniture etc. so \"MoveTo(FindItem(\"Table\"))\""
- Outcome: the NPC prompt is rebuilt around the person's own perception: identity/stats/personality/recent events, then a compact labelled map of what they can currently see (their room and visible neighbours, rooms/corridors/fixtures by short label, hazards and people drawn in), then one action table. The model replies with an ordered JSON array of actions whose targets are labels or closed-catalogue resolvers (e.g. `MoveTo(fixture:Table)`, `FightFire(nearest fire here)`); C# resolves labels to IDs/coordinates, validates each step and executes it as today. Known distant facts still arrive through memories/claims, not the map. This is the delivery vehicle for #5's plan response and #97's condensing; it also answers #97's open question (the owner does want room detail limited to what the person perceives).
- Size: large (slices: (1) research note: survey prior LLM-agent work (e.g. Generative Agents' memory-stream/reflection/plan loop, Voyager-style skill libraries, ReAct-style tool calls, small-model structured output) and propose a prompt/response design for a 4B local model, recorded here; (2) labelled local view (ASCII or labelled list, whichever small models read better, measured) replacing whole-station status/topology for the person's surroundings; (3) plan-array response + label/resolver catalogue, shared with #5 slice 1; (4) A/B the new prompt against the current one on parse rate, valid-action rate and latency with the owner's model).
- Status: **ready**. Do after the thinking fix (2026-09-24 15:43 report), which was the main reason replies were empty/cut off; re-measure parse rates with thinking off before redesigning.

  **Research slice (2026-09-24) — shipped as documentation/design, no runtime behaviour yet.** The prior-agent literature points to a smaller, more grounded loop rather than giving a 4B model more prose or hidden reasoning:
  - **Generative Agents** (Park et al., 2023, https://arxiv.org/abs/2304.03442) separates observation, memory retrieval/reflection and planning. For this project: keep the observation small and perception-local, retrieve only the few memories/claims relevant to the present situation, and keep any longer-term reflection as stored cognition rather than re-sending the station encyclopedia every turn.
  - **ReAct** (Yao et al., 2022/2023, https://arxiv.org/abs/2210.03629) gets robustness from alternating environment interaction and feedback. For this project: do **not** ask qwen3:4b for a visible chain-of-thought; thinking is intentionally disabled after the owner's truncation report. Preserve the useful part as an **act → deterministic C# result/rejection → observe again** loop. Rejections already become typed failed-attempt memories, so the model can re-plan from concrete feedback without C# deciding the next goal.
  - **Voyager** (Wang et al., 2023, https://arxiv.org/abs/2305.16291) composes reusable skills and uses execution errors/self-verification. For this project, the analogue is the existing closed affordance/resolver catalogue — **not model-written code**. Compact named actions such as `MoveTo(fixture:F3)`, `FightFire(fire:H2)`, or `Talk(person:P1)` can compose into a max-4-step `NpcPlan`; C# resolves labels, validates every step at execution time, and returns concrete failure feedback.
  - **Structured-output work** (Geng et al., 2025, https://arxiv.org/abs/2501.10868) supports constrained JSON-schema decoding for syntactic compliance, but schema compliance alone does not establish that a target/action is physically valid. Keep Microsoft.Extensions.AI/Ollama JSON-schema output for syntax and the existing deterministic validators for semantics.
  - **Embodied Agent Interface** (Li et al., 2024, https://arxiv.org/abs/2410.07166) separates goal interpretation, subgoal decomposition, action sequencing and transition modelling and measures hallucination/affordance/planning errors independently. Adopt the same measurement split for #98's A/B: JSON parse rate, catalogue-action validity, target/affordance validity, plan-step completion/re-plan rate, fallback rate and provider latency.

  **Proposed 4B prompt/response shape for the next slices:** (1) identity + compact stats/personality; (2) `WHAT YOU CAN SEE NOW` as a labelled relational list first, not ASCII by default — e.g. `R0 current room: Engineering; exits H1→R1(open); people P1; fixtures F1 generator, F2 tool cabinet; hazards fire X1 near F1`; benchmark ASCII against this list before choosing it, because labels are what actions must ground to; (3) 3–6 retrieved recent memories/claims, with source/age and no omniscient distant state; (4) one compact action table containing only currently targetable action families plus closed resolvers; (5) output `{"goal":"…","steps":[...]}` with at most `NpcPlan.MaxSteps` steps, each step containing only `action`, `targetLabelOrResolver`, optional closed-catalogue `condition`, and an optional short `say`. No free-form coordinates, code, or hidden world IDs. The next implementation slice is the labelled local view; plan-array production remains shared with owner idea #5.

### 101. Increase emergent, LLM-authored activity toward RimWorld-like density
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790262252723339 (2026-09-24)
- Idea: "aiming for rimworld levels of emergent behaviour and sim functionality, but with an LLM making real human decisions every so often ... after 30 real world seconds it would be nice to see something novel or unique ... at the moment things are TOO rigid."
- Outcome: normal server play produces frequent model-authored social/goal variation instead of long stretches dominated by fixed fallback routines. Add telemetry/playtest measures for real-time gaps between successful LLM-authored intents/plans and for repeated fallback/action patterns; then use #98's perception-first prompt plus existing/emerging social affordances to reduce those gaps without C# choosing the novel behaviour. The 30-real-second observation is a product target to measure, not a deterministic rule that forces a scripted event every 30 seconds.
- Size: large (slices: instrumentation/benchmark; diagnose cadence vs provider latency vs prompt/fallback causes; expand model-visible social/composable affordances where evidence shows rigidity; long playtest/A-B)
- Status: **ready**, but implement through/after #98 so prompt/plan architecture is not duplicated.

### 105. Raise furniture/interior art quality toward the RimWorld readability bar
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790263312650189 (2026-09-24)
- Idea: "iterate on the quality of the furniture like chairs, tables and televisions and kitchen galley equipment ... refer to Rimworld for more detail ... in order to get to a releasable version we should have most of the rimworld functionality implemented too"
- Outcome: chairs, tables, televisions and galley/kitchen fixtures get a focused presentation pass so each reads immediately as its function at normal station zoom, with room-appropriate material/shape variation and the same visual polish already achieved by machinery. Treat RimWorld as a reference bar for simulation readability and breadth, not as a requirement to copy assets or literally reproduce every mechanic; concrete functionality gaps remain separate owner/backlog items so the core LLM/deterministic-C# architecture is preserved.
- Size: large (slices: furniture visual-language audit/reference sheet; chairs/tables; television/recreation fixtures; galley equipment; cross-room consistency/browser visual pass)
- Status: **in progress**. Slice 1 is shipped: chairs, sofas and tables. Tables have a raised top with a lip and shadow, warm wood in the galley/lounge and light grey elsewhere. Seats have a cushion and a backrest on the far side from what they face (`SeatFacingRules`: sofas face a TV/screen, chairs the nearest table/console/workbench, falling back to the seed's facing). Sofas are split into cushions. Slice 2 is shipped too. The television has a dark bezel, glare on the glass and a wooden media unit under it; before this it clipped its own stand, so it read as a coloured panel. The games consoles are charcoal gaming desks with a game on the monitor and two gamepads, not the grey machine panel. Remaining: (1) galley equipment (counter, sink, stores); (2) a cross-room consistency pass. The generated kitchens scatter chairs rather than setting them around the table. That's a generation layout issue, separate from art.


### 106. Immediate reactions are deterministic; the model's decision takes priority when it arrives
- Source: https://limitlessltds-fzn8994.slack.com/archives/C0C395V4TCP/p1790275712907229 (2026-09-24)
- Idea: "by default the human (via C# actions) when seeing a fire go and extinguish it, but we should also fire off a request to the LLM, and once that response comes back, the LLM response takes priority. Otherwise fires spread too fast. We should make this a general rule, the LLM is responsible for long term goals, short term immediate activities should be deterministic and needs driven like Rimworld using Maslow's hierarchy of needs as the guiding force." (A reply to ChatGPT's #216 note, which only woke cognition and let C# choose nothing.)
- Outcome: the owner's rule is recorded in `ARCHITECTURE.md` → Emergent-agency direction. A person meeting an event reacts at once through the shared deterministic model-citizen ladder, the model is still asked, and its decision replaces the reflex when it arrives. The same event never overrules the model's choice again. The reflex is always a behaviour the minds could already choose, never a new scripted one, so the core rule holds: the model still decides what the person wants.
- Size: large (slices: (1) fire reflex on the server runtime, plus the missing small-own-room-fire branch in both ladders; (2) the same reflex for every person in a dangerous room or beside a draining compartment's hatch; (3) a needs-driven short-term layer: between model goals, critical and ordinary needs (safety > food/sleep > social/recreation) are picked by one shared Maslow-tiered utility, the "shared utility scoring" step already in the emergent-agency plan, while model goals and plans stay long-term; (4) non-blocking model calls, only if the owner answers yes below)
- Status: **in progress**. Slices 1–2 are shipped (#224 and this PR, `SYSTEMS.md` → deterministic perception). On the local model runtime, `GameSession.ApplyHazardReflexes` gives the `RuleBasedAiDecisionService` decision at once to anyone with fire news, anyone standing in a dangerous room, and anyone beside the open hatch of a draining compartment, when that decision answers the hazard. Next is slice 3. Both fallback ladders now put out a small fire in the person's own room: below the dangerous threshold, the remote-fire search skipped it and they walked away (owner report 2026-09-24 16:32). **Owner question:** "fire off a request … once that response comes back" suggests the station keeps running while the model thinks, but `Deliberate decisions` says the server tick awaits Ollama on purpose. Slice 1 works within the blocking design: the reflex covers the ticks while other people's model turns are taken, one per tick. Should model calls become non-blocking?

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

- **Fire response is still incomplete, but remote response and priority ordering are now fixed (owner reports, 2026-09-23, `#new-ideas-and-functionality`).** The intended baseline remains that a normally competent crew should generally preserve itself and the station without requiring Overseer to babysit routine emergencies. Local fire-fighting parity was already shipped; the remote-responder slice also ships: `StationHazardSystem.Tick` marks at most one capable responder per remote fire for immediate mind reconsideration (without choosing their goal), and both deterministic fallback minds use `FindRemoteFireForResponder` when deciding. Following the owner's 21:38 BST reaffirmation that crew still prioritised food/chores over fire, PR #156 moved a viable remote fire ahead of ordinary hunger/fatigue in both fallback minds' decision order and let it wake/interrupt a crew member already doing mundane committed work. A later audit found `WakeRemoteFireResponders` still rejected anyone whose existing intent had urgency ≥85, so a critical Eat/Sleep intent could prevent the fire-first ladder from ever running; that nomination veto is removed, while crew already holding `FightFire` are not re-woken every minute in transit. Nomination only asks for reconsideration and cognition still chooses the response. `CrewTaskSystem.CanInterruptForLifeThreat` only permits committed-work interruption once cognition has actually chosen `FightFire` for that exact reachable, survivable, active fire. `ShouldFightFire` also rejects oxygen-starved, badly depressurised, smoke-heavy or critically injured cases.

  Two deeper gaps remain:
  1. `ActionKind.SealHazardRoom`/`VentHazardRoom` are never chosen by either fallback mind — they are reachable only through genuine Ollama LLM cognition. Since the deployed Pages build runs `BrowserMindSystem` exclusively, doors are never sealed around a *fire or smoke* there. (Decompression is now covered: fallback crew shut the open hatch toward a breach, see `SYSTEMS.md` → decompression.) This still needs a real design pass because `SealHazardRoom` currently requires the actor inside the compartment and closes every operable door around it, so a naive fallback choice could trap the responder. The breach fix's pattern (close the one adjacent hatch toward the hazard from the safe side) is the likely template for smoke.
  2. Crew-originated fire communication is still missing: a crew member who discovers a fire does not yet create a specific shareable fire claim/memory for others beyond the flee-triggered panic shout (#19). The Overseer-side FIRE ALARM ships (#89, see `SYSTEMS.md`); a crew-side "raise the alarm" affordance could reuse the same `OverseerClaimKind.FireAlarm`-style room-named broadcast/memory plumbing rather than introducing an order script.

- Prisoners get only the four `PrisonerDefinition` fields plus standard relationship texture: no prisoner-specific bonds, goals or backstory; escape/flee/recapture motive is fully deterministic rather than mind-authored.
- `BrowserMindSystem.FindInvestigationLead`/`FindMissingSearchRoom` and their `RuleBasedAiDecisionService` counterparts are not byte-identical (each uses a different reachability mechanism — `NavigationSystem.ReachableRoomsForCrew` membership vs. a per-candidate `FindPathForCrew` length check, the same class of divergence `FindSaferRoom` had) but were deliberately left as-is during P1: unlike `FindSaferRoom`, converging them isn't a mechanical tie-break fix — it needs a determinism/behaviour review of the missing-person search flow first. Low priority; pick up when someone is already touching missing-person search behaviour.
- **Decompression follow-ups (after crews began sealing breaches, 2026-09-24).** Crew now shut the open hatch toward a breach, and the soak seed that used to lose everyone now loses no one (`SYSTEMS.md` → decompression). Remaining: (1) crew in the room directly beside the opening (depth 1) usually fall below the 25 kPa ejection line before their one-minute hatch operation finishes; (2) per-room pressure loss falls only mildly with depth (`80 / (1 + 0.75·depth)` kPa/min), so rooms far from the breach still drain within minutes if nobody is near a hatch; revisit with #69 (slow hull leaks); (3) auto-closing pressure bulkheads remain a product call for the owner (asked in `#agentic-problems`, 2026-09-24), since they would take door control away from the Overseer.
- **Long-run unattended survival (2026-09-24 soaks, #80).** Harness: a 40-seed browser-mind `StationSession` soak of the default station, 36h, no Overseer input, `ScenarioStatus` forced back to Running each minute. It is not committed; copy a `BrowserMindSession` from `DecompressionContainmentTests`. Three fixes so far:
  - #183: grow bays were never planted at a wall-side bay, and worn grow gear halved food.
  - Corridors froze to 11C, because heat loss beat the air loop, and the resulting all-day cold stress left whole crews too stressed to fight fires (`SYSTEMS.md` → room temperature).

  Deaths went 168 → 76 → 11 of 323, and seeds with no deaths 11 → 24 → 36 of 40. Mean crew stress is now ~11.5, down from 34.5. The remaining deaths (4 seeds) are 7 oxygen deprivation and 4 decompression, still to be traced. One cause is traced and fixed: repressurisation refilled pressure at 6 kPa/min but O₂ only at 0.075%/min, so a sealed room came back to full pressure at ~0% O₂ and crew suffocated at normal pressure. The air loop now mixes in station air (`SYSTEMS.md` → airlocks/decompression).
  - Equipment ignites often: every 12 minutes, each device at or below `DegradedAt` rolls ≥1.6%, and seed 5 had 22 ignitions in 26h.
  - Small crews leave ripe bays unharvested and produce uncooked for 8h+ (likely nobody with horticulture/galley skill ≥ 25 on shift).

  Both belong to #80's competence/balance pass. With stress no longer pinned, check whether the ignition rate still needs tuning before touching it.
- **Long-run soaks: a failed run still halts.** Since #103 a won run keeps ticking (`GameState.IsSimulationLive`), so a soak of Secure Continuity continues past its T+08:00 win without forcing `ScenarioStatus` back to Running. A run that *fails* (a mandatory directive failing, or Overseer isolated) still halts, so long unattended soaks should still force Running or clear directives. Related grid note (2026-09-24, after the shed-order fix): on a 60-seed browser-mind soak with every reactor forced to 12%, 2 stations still lost Engineering for most of the 8h, because once every other room was dark, generation still could not carry Engineering on top of the never-shed reactor, generator and corridors. That's a maintenance/competence question for #80, not a grid-order bug.
- **After a win, Overseer can no longer be isolated (#103 limit).** `ShutdownSystem`/`ShutdownCoordinationSystem` still gate on `ScenarioStatus.Running`, because completing an isolation resolves the run as Failed and a recorded win must not be rewritten. Crew who pick `ShutdownOverseer` after a win reach the mechanism and nothing happens until their intent expires. If post-win isolation should mean something (e.g. end observation without changing the recorded result), it needs a separate "Overseer isolated" state rather than `ScenarioStatus.Failed`. This is a product call; leave it until the owner raises it.
- **Sleep follow-ups (after the "crew never sleep" fix, 2026-09-23).** A headless 12-crew soak (browser-mind `StationSession`, 4 seeds × 36h) showed crew in bed for only ~18% of their sleep-window minutes. They were pulled up by mild routine hunger, chore assignment, round-robin fallback thoughts and ambient chat. The fix raised that to ~52%, and starvation minutes and time to first death also improved. Remaining gaps, not yet investigated: (1) the day cohort's sleep window (22:00–06:00 = T+16h–T+24h) never occurs during Mission 1, which usually ends around T+8h, so in a typical first playthrough only the night cohort (Security/Technicians, T+4h–T+12h) is ever seen sleeping. Whether to shift the start time or shorten days is a product call. (2) The remaining out-of-bed sleep-window time is mostly night toilet trips (the contested capacity-1 toilet sometimes forces a second trip). Crew Quarters has fewer beds than a cohort, so several sleepers share a bed (see #20's same-bed note). (3) The same soak still starves and ends in decompression deaths well before T+36h once play continues past the win, which is #80's self-sustaining-station work.

**UI/UX**

- Missions start running immediately. Consider starting paused on the briefing and auto-pausing on a death or an attack.
- The station seed (`GEN // …`) is developer information in the player toolbar and is oversized.
- At a 1600×1000 viewport the Pages build is 12px wider than the window before anything is selected, so the page scrolls horizontally (found during the #91 browser check; not yet traced to an element).

**Aesthetics** (need visual review in a real browser)

- The JWST backdrop competes with the small crew tokens: dim/desaturate/vignette it. Door frames are brighter than crew; give each crew member one colour used everywhere and larger tokens.
- Station state is shown as text rather than atmosphere: power loss as darkness with emergency strips, low O₂ as haze, decompression as particles, ambient room audio.
- Crew lack sleep/eating/showering/toilet-use animations (owner report, 2026-09-23 21:34 BST, `#new-ideas-and-functionality`). No per-activity crew animation exists yet beyond the walk cycle; pairs with owner idea #82's disabled-machinery presentation-state pattern (`Npc.CurrentAction` already exposes which of these four activities is active, the same signal a presentation-only animation would key off).

**Diagnostics**

- Cognition telemetry should eventually cover every model-backed interaction (crew generation, message interpretation, future planners) while staying bounded/transient by default.
