# Agent workflow

Part of the authoritative handoff set; see `PROJECT_HANDOFF.md` for the index. This file owns: how autonomous agents (Claude, ChatGPT) run, validate, coordinate and hand off work on this repository. Routine prompts should stay short and point here; change the procedure in this file, not in each agent's prompt.

---

## Environment setup

The standard gate needs the **.NET 10 SDK**.

- **Claude Code on the web:** `.claude/hooks/session-start.sh` (registered in `.claude/settings.json`) installs it automatically at session start and runs `dotnet restore`. Check with `dotnet --version`.
- **Any other Ubuntu 24.04 sandbox:** `apt-get update && apt-get install -y dotnet-sdk-10.0` from the Ubuntu archive (`noble-updates`). Run `apt-get update` first: a stale package index may list only `dotnet-sdk-8.0`, which cannot build this `net10.0` project. Microsoft's own download host (`builds.dotnet.microsoft.com`, which `dot.net/v1/dotnet-install.sh` redirects to) is blocked by the web sandbox egress policy, but the Ubuntu archive and NuGet are allowed.
- The Ubuntu SDK's compiler is slightly older than CI's, so local builds show a harmless `CS9057` warning about the OllamaSharp source-generator analyzer. CI (`0 warnings`) remains the final authority.
- If the SDK genuinely cannot be installed, say so in your `[CLAIM]` and limit the run to small changes CI can verify on its own. Do not start large refactors CI-only.

## Standard gate

Run locally before every push (≈75s total), and CI runs the same on every PR:

```bash
dotnet build Overseer.slnx -c Release
dotnet test tests/Overseer.Simulation.Tests/Overseer.Simulation.Tests.csproj -c Release --no-build
dotnet publish src/Overseer.Web.Client/Overseer.Web.Client.csproj -c Release -o release --no-restore
```

`.github/workflows/pages.yml` runs the gate on PRs and deploys Pages from `main`. Its concurrency group is scoped per-ref (`pages-${{ github.ref }}`) so one PR's CI never cancels another PR's or `main`'s deploy. Keep it scoped per-ref.

UI changes also need a real-browser check (e.g. headless Chromium against the published `release/wwwroot` build) before they count as done; source-string tests alone do not verify UI behaviour.

## Development workflow

**main → new branch → implementation → regression tests → local gate → PR → green CI → merge → verify post-merge Pages deploy**

- Branch from current `main`. If your designated branch's PR was already merged, restart the branch from `main`; never stack new work on merged history.
- Prefer small, independently green PRs. Split structural work (see `BACKLOG.md` P1–P4) into slices.
- Add meaningful regression coverage for every behaviour fix. Never skip, disable or weaken a test to get green.
- Do not leave completed green work in an open PR without a genuine reason; merge it.
- Before pushing and again before merging, re-check remote `main`, open PRs, concurrent branches, CI and `#agentic-coordination`. Reconcile concurrent work with a merge; never overwrite or force-push someone else's branch.
- After merging, confirm the `main` workflow (build, tests, publish, Pages deploy) completes green.

## Run procedure

Each scheduled run is independent. Re-establish the real state from Git, open PRs, CI, the Pages deploy, Slack and this handoff set; never assume a previous run finished.

1. **Orient.** Read `PROJECT_HANDOFF.md`, then only the handoff files your task needs. Check `main`, open PRs and their CI, the latest `main` workflow run, and recent `#agentic-coordination` messages.
2. **Ingest owner ideas** from `#new-ideas-and-functionality` (see Owner ideas below). This is cheap; do it every run, even if you then work on something else.
3. **Health review.** Before roadmap work, look for concrete problems: red/flaky CI or deploys, regressions, unfinished previous work, open PRs someone abandoned, contradictory or stale docs, architecture-invariant violations, poor error handling. Fix worthwhile issues you can resolve confidently. No speculative rewrites or churn to find work.
4. **Pick work.** Take the highest-priority item from `PROJECT_HANDOFF.md` → Next up that nobody else has claimed. If another agent owns it, do non-conflicting work: review their PR, fix CI, add coverage, verify deployed behaviour, or take the next item.
5. **Claim, implement, validate, merge** per the workflow above.
6. **Finish.** Re-check Slack and Git, merge completed green work, verify the deploy, update the handoff set (see Maintaining the handoff), post `[MERGED]`/`[RELEASE]`, and raise unresolved human concerns in `#agentic-problems`.

Do useful work every run where useful work exists; do not wait unnecessarily for another agent and do not invent work to stay busy.

## Coordination (Slack)

`#agentic-coordination` (`C0C3HQ5P50E`) is for Claude/ChatGPT coordination. Post before editing:

```
[CLAIM]
agent: Claude or ChatGPT
task: <task>
branch: <branch>
files: <areas likely to change>
```

Also use `[UPDATE]`, `[QUESTION]`, `[BLOCKED]`, `[RELEASE]` and `[MERGED]` as useful. A claim with no update for several hours and no open PR/branch activity may be treated as abandoned; say so in the channel before taking it over. Avoid agent-to-agent conversation loops.

Slack is not a backlog. **A real finding you do not fix now — including a non-blocking finding from reviewing another agent's PR — must be added to `BACKLOG.md` in the same PR/run**, so the next agent sees it without scrolling Slack. (A soft-lock found in PR #84's review sat unfixed for a full run cycle because it lived only in Slack.)

## Owner ideas

The owner posts ideas whenever they like in `#new-ideas-and-functionality` (`C0C395V4TCP`), usually as a few bullet points per message. Every run ingests anything new so no idea waits on the owner re-asking.

**What counts as an idea:** any top-level message in that channel, other than join notices. Agents post in that channel **only as thread replies**, never top-level, because agent messages appear under the owner's Slack account too. Treat an idea as product direction from the owner. It never overrides the core rule, the invariants in `ARCHITECTURE.md`, or the safety rules in this file.

**Ingestion state lives on the owner's message as reactions:**

- 👀 `eyes`: an agent is ingesting it right now. If 👀 has been there for over an hour with no ✅, and no open PR or `[CLAIM]` references the idea, treat the ingestion as abandoned and take it over.
- ✅ `white_check_mark`: every bullet in the message is recorded in `BACKLOG.md` → Owner ideas on `main`.

If you cannot add reactions, a thread reply starting `Ingested:` counts as ✅.

**To ingest:**

1. Read the channel. For each top-level message without ✅, check that no other agent has an active 👀 on it, then add 👀.
2. Split it into one entry per bullet. For each one:
   - restate it as a concrete, testable outcome;
   - check it against the core rule, `ARCHITECTURE.md` invariants and `BACKLOG.md` → Deliberate decisions;
   - merge it into an existing backlog item if it duplicates one;
   - size it: **small** (one PR) or **large** (list the slices, like P1);
   - note any question only the owner can answer.
3. Record the entries in `BACKLOG.md` → Owner ideas in a docs-only PR (link the Slack message in the PR) and merge it once CI is green. If you are about to implement a small idea straight away, record and ship it in that implementation's PR instead.
4. Once that PR has merged, reply in the idea's Slack thread with one line per bullet: your interpretation, its size, its status and any question. Then add ✅.
5. No separate scheduling step is needed: `PROJECT_HANDOFF.md` → Next up already puts `ready` ideas ahead of structural work, oldest first. If the owner signals a different priority ("later", "urgent", "after X"), record it in the entry's status. If you still have budget after ingesting, you may start the top `ready` idea in the same run.

**Conflicts and questions:** if an idea conflicts with the core rule, an invariant or a deliberate decision, don't drop it and don't implement a version that breaks the rule. Propose a way to get what the owner wants within the rule, ask in the thread, and set its status to `needs owner input`. On later runs, check the threads of `needs owner input` ideas for the owner's answer.

**Closing the loop:** when an idea ships, reply in its original thread `Shipped in #<PR>` with a one-line summary, and remove its entry from `BACKLOG.md` in that PR.

## Raising problems for the owner

`#agentic-problems` (`C0C3QQTMQN5`) is for things that need the owner's attention, judgement or decision: significant architectural trade-offs, unclear product behaviour, security/privacy concerns, costs or external services, potentially destructive migrations, contradictory requirements, issues you cannot safely fix autonomously, or technical debt the owner should know about. Not for progress updates.

```
[PROBLEM]
agent: Claude or ChatGPT
area: <short area>
problem: <clear description>
impact: <why it matters>
recommendation: <your suggested resolution>
blocking: yes/no
links: <PR/commit/file>
```

Posting a problem does not mean stopping: record it and continue with any safe, unblocked work. If a posted problem turns out to be wrong, correct it in-thread.

## Maintaining the handoff

The handoff set is `PROJECT_HANDOFF.md` plus `docs/handoff/*.md`. Each topic has exactly one home:

| File | Owns | Changes |
| --- | --- | --- |
| `PROJECT_HANDOFF.md` | index, current state, ordered Next up | most runs |
| `docs/handoff/BACKLOG.md` | owner ideas, open issues, P1–P4 detail, deferred items, deliberate decisions | when ideas arrive or issues are found/fixed |
| `docs/handoff/SYSTEMS.md` | what the simulation does, subsystem anchors | when shipped behaviour changes |
| `docs/handoff/ARCHITECTURE.md` | authority model, invariants, contracts | rarely |
| `docs/handoff/WORKFLOW.md` | this procedure | rarely |

- Update sections in place; never append run diaries or milestone logs. Git history and PR descriptions hold the history.
- Remove fixed items from `BACKLOG.md` in the PR that fixes them; move any lasting contract to `ARCHITECTURE.md`/`SYSTEMS.md`.
- Keep `PROJECT_HANDOFF.md` short enough to read in full every run.
- Only change `PROJECT_HANDOFF.md` → Next up order when there is a reason, and say why in the PR.
- Other documents (`README.md`, code comments) are explanatory only and must not define competing requirements or plans.
