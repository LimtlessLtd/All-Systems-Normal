# All Systems Normal — Project Handoff

Repository: https://github.com/LimtlessLtd/All-Systems-Normal
Playable Pages build: https://limtlessltd.github.io/All-Systems-Normal/

This file plus `docs/handoff/*.md` is the **single authoritative source** for architecture, invariants, roadmap, priorities and handoff state. Each topic lives in exactly one of these files (table below); if any other document conflicts with them, they win. Read this file in full every run, and open the others only as your task needs them.

> **The LLM decides what an NPC WANTS to do. Deterministic C# decides what the NPC CAN do and what actually happens.**

| Read | When |
| --- | --- |
| [`docs/handoff/WORKFLOW.md`](docs/handoff/WORKFLOW.md) | **every run**: environment setup, validation gate, Git/PR flow, Slack coordination, how to update these docs |
| [`docs/handoff/BACKLOG.md`](docs/handoff/BACKLOG.md) | choosing or scoping work: owner ideas, open issues, P1–P4 detail, deferred items, deliberate "do not fix" decisions |
| [`docs/handoff/ARCHITECTURE.md`](docs/handoff/ARCHITECTURE.md) | before changing simulation, generation, presentation or persistence: authority model, invariants, contracts |
| [`docs/handoff/SYSTEMS.md`](docs/handoff/SYSTEMS.md) | finding your way: what each system does and where it lives |

---

## Current state

**V0.13 shipped:** the complete standalone *Containment Transfer* assignment, plus playtest polish: collision-safe physical-speed movement, committed authoritative task progress, per-bay hydroponics, 12-hour missing-person escalation, physically grounded sleep/fatigue, escalating fire/smoke with topology-aware cognition, a station-only gameplay workspace, a reordered crew Inspector and raw Ollama cognition diagnostics.

`main` is green and Pages is deployed. Local builds are possible in agent sandboxes (see `WORKFLOW.md` → Environment setup).

## Next up

In order. Take the first item nobody has claimed; see `BACKLOG.md` for evidence and suggested slices. Health-review findings (red CI, regressions, broken deploys) always come first, and new ideas in `#new-ideas-and-functionality` are ingested every run (`WORKFLOW.md` → Owner ideas).

1. **Owner ideas** with status `ready` in `BACKLOG.md` → Owner ideas, oldest first, unless an entry says otherwise. They are the owner's direct product intent, so they come ahead of structural work.
2. **P1: converge the two fallback decision ladders** (`BrowserMindSystem` / `RuleBasedAiDecisionService`): shared thresholds. Airlock rules (#88), reachability (#91) and the repair-skill formula are done; next slice is the need thresholds.
3. **P2: decompose `Home.razor` / `Home.razor.css`** into components with scoped styles, then add a bUnit/Playwright smoke suite.
4. **P3: tick-loop contract** (`ISystem`, an explicit ordered pipeline, rename `SimulationEngine`).
5. **P4: share stateless services** (`NavigationSystem`, `CrewDoorInteractionSystem`) through a minimal composition root.
6. Smaller open issues in `BACKLOG.md` → Open issues, when they block the above or the owner asks.
