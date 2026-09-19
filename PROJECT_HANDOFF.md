You are taking over development of my game project **All Systems Normal**.

GitHub repository:

https://github.com/LimtlessLtd/All-Systems-Normal

Playable GitHub Pages build:

https://limtlessltd.github.io/All-Systems-Normal/

The project is currently around **V0.4**, with an additional camera-rendering hotfix already merged into `main`.

Your job is to continue developing the game while preserving the architectural principles and gameplay vision below.

# 1. GAME PREMISE

**All Systems Normal** is a psychological/emergent space-station simulation where the PLAYER IS THE STATION AI.

The player does **not directly control humans**.

Instead, the player controls the environment around them:

* doors
* locks
* lighting
* cameras
* power
* heating/cooling
* oxygen
* pressure
* life support
* ventilation
* communications
* alarms
* displays
* eventually robots and other station infrastructure

The human crew are autonomous agents.

The player manipulates circumstances and information in order to influence what humans choose to do.

The fundamental fantasy is:

> You are not managing the crew.
> You are manipulating an intelligent social system using the station itself as your interface.

The humans should gradually realise that something may be wrong.

The game should eventually feel like a combination of:

* emergent colony/social simulation
* psychological manipulation game
* facility management
* social stealth
* AI sandbox

The design should favour **emergence over scripted quests**.

# 2. ABSOLUTE ARCHITECTURAL RULE

This rule must be preserved:

> **The LLM decides what an NPC WANTS to do.
> Deterministic C# decides what the NPC CAN do.**

Never allow an LLM to directly mutate game state.

An LLM may produce:

* a goal
* an intention
* reasoning
* dialogue
* interpretation of events
* beliefs
* social decisions

But deterministic simulation code must validate:

* whether a room exists
* whether a person exists
* whether the NPC knows something
* whether a door is accessible
* whether a path exists
* whether an NPC is alive/conscious
* whether an action is physically possible
* what actual damage/results occur

No teleportation.

No invented rooms.

No invented NPCs.

No direct LLM calls such as:

`Kill Marcus`

causing Marcus to die.

Violence and death should emerge from deterministic simulation state.

# 3. TECH STACK

The project uses:

* **.NET 10**
* C#
* Blazor
* ASP.NET Core
* Blazor WebAssembly
* xUnit
* `Microsoft.Extensions.AI`
* OllamaSharp
* Microsoft Agent Framework package is present/available but should only be introduced where it genuinely adds value

Do **not** rebuild the project around Semantic Kernel.

Use `Microsoft.Extensions.AI` as the provider-neutral AI abstraction.

Agent Framework can eventually be used for multi-step workflows/tool use, but do not prematurely turn every NPC into a heavyweight tool-using agent.

# 4. SOLUTION STRUCTURE

Current structure is approximately:

```text
All-Systems-Normal/
├── src/
│   ├── Overseer.Domain/
│   ├── Overseer.Simulation/
│   ├── Overseer.AI/
│   ├── Overseer.Persistence/
│   ├── Overseer.Web/
│   └── Overseer.Web.Client/
├── tests/
│   └── Overseer.Simulation.Tests/
└── Overseer.slnx
```

Responsibilities:

```text
Overseer.Domain
    Authoritative game state/contracts.

Overseer.Simulation
    Deterministic world simulation.
    Movement.
    Navigation.
    Needs.
    Social consequences.
    Action validation.
    Intent execution.

Overseer.AI
    LLM cognition.
    Prompt construction.
    Structured responses.
    Validation.
    Fallback AI.

Overseer.Persistence
    Future saves / long-term persistence.

Overseer.Web
    Full ASP.NET / Blazor Server version.
    This is the REAL AI build using Ollama.

Overseer.Web.Client
    Static Blazor WebAssembly version.
    Used by GitHub Pages.
    MUST NOT contain AI API secrets.
```

# 5. CURRENT NPCS

Six initial crew:

```text
David Hale
Commander

Sarah Chen
Engineer

Marcus Reed
Security

Nadia Okafor
Doctor

Felix Ward
Technician

Emma Voss
Scientist
```

Current initial station rooms include approximately:

* Crew Quarters
* Kitchen
* Medical
* Control Room
* Generator
* Reactor
* Engineering
* Storage
* Central Corridor
* Airlock

# 6. CURRENT NPC MODEL

NPC state currently includes concepts such as:

```text
Health
Hunger
Fatigue
Fear
Stress

Role
CurrentRoomId

Skills

Personality:
    Empathy
    Temper
    Sociability
    Courage

Memories

Beliefs

Relationships:
    Affinity
    Trust
    Resentment
    Conversations
    Arguments

CurrentAction

Persistent Intent

MindMode
LastThought
LastThoughtAt

Alive/dead state
CauseOfDeath
```

Relationship state is pairwise, not global.

For example:

```text
Sarah -> Marcus
Trust: 31
Affinity: 36
Resentment: 58

Marcus -> Sarah
may have different values
```

# 7. IMPLEMENTED VERSIONS

## V0.1 — Simulation Foundation

Implemented:

* deterministic facility state
* 10-room facility
* six crew
* doors
* needs
* simulation ticking
* validated actions
* first Blazor control room
* tests
* `IAiDecisionService` boundary

## V0.2 — Station View

Implemented:

* top-down 2D facility overview
* room state
* door controls
* crew positions
* selectable rooms
* selectable crew
* cameras
* power
* lighting
* temperature display
* oxygen display
* run/pause
* simulation speed
* deterministic movement/routines
* event log

## V0.3 — Emergent Social Simulation

Implemented:

* personality traits
* pairwise relationships
* conversations
* arguments
* resentment
* trust/affinity changes
* memories
* environmental stress
* violence
* witness reactions
* possible murder/death
* dead NPCs stop participating

Violence is NOT initiated by an LLM directly.

It emerges when deterministic thresholds/combinations such as:

```text
high resentment
+
high stress
+
high temper
+
social conflict
```

produce escalation.

## V0.4 — Autonomous Minds

Implemented:

* `Microsoft.Extensions.AI`
* local Ollama
* structured LLM decisions
* persistent NPC intentions
* prompt construction
* hallucination validation
* fallback cognition
* perception-limited prompt context
* browser-safe deterministic demo mind

The full local/server build uses Ollama.

The GitHub Pages build does NOT directly call an LLM.

# 8. CURRENT LLM ARCHITECTURE

Local development uses:

```text
Microsoft.Extensions.AI.IChatClient
+
OllamaSharp
```

Default model:

```text
qwen3:4b
```

Default Ollama endpoint:

```text
http://localhost:11434
```

Example local setup:

```bash
ollama pull qwen3:4b
ollama serve
dotnet run --project src/Overseer.Web
```

The AI service produces structured high-level intentions.

Conceptually:

```json
{
  "Action": "Argue",
  "TargetId": "Marcus Reed",
  "Goal": "Confront Marcus about his accusations.",
  "Reason": "His distrust is making it difficult for me to do my job.",
  "Urgency": 66
}
```

The model does not perform the action.

Instead:

```text
LLM decision
    ↓
NpcIntent
    ↓
IntentExecutionSystem
    ↓
NavigationSystem
    ↓
ActionResolver
    ↓
Authoritative world mutation
```

# 9. PERCEPTION / KNOWLEDGE RULE

NPCs MUST NOT receive omniscient world state.

Prompts should contain only things reasonably known/perceived by that NPC.

Current prompts include things like:

* own personality
* own needs
* current room
* room environment
* visible people in current room
* directly connected doors
* personal relationships
* own memories
* own beliefs

The global event log is deliberately excluded.

If something occurs in another room with no witness/camera/social communication, an NPC should not magically know it happened.

Eventually create stronger perception/event propagation systems.

# 10. CURRENT INTENT SYSTEM

NPCs have persistent high-level goals.

Example:

```text
Sarah thinks:
"I want to confront Marcus."
```

Sarah may then:

```text
Engineering
    ↓
Corridor
    ↓
Control Room
    ↓
Marcus
```

one legal movement step at a time.

If the route becomes sealed:

```text
Sarah still wants to confront Marcus
BUT
she cannot physically reach Marcus
```

The intent remains/stalls until it expires or circumstances change.

This is intentional.

# 11. NAVIGATION RULES

Current navigation is room graph based.

A door is traversable only when:

```text
IsPowered
&& IsOpen
&& !IsLocked
```

Every successful crew movement logs the exact door crossed.

There are tests ensuring locked rooms cannot be entered.

Do not break these invariants.

# 12. IMPORTANT FIX ALREADY APPLIED

There was a visual bug where toggling cameras appeared to make humans move while paused.

It was NOT simulation movement.

Cause:

The visible crew list is filtered:

```razor
Session.State.Crew.Where(CanSee)
```

Without stable Blazor keys, hiding an NPC when a camera was disabled caused Blazor to reuse DOM elements for different NPCs.

CSS transitions made them appear to run across the station.

This has been fixed using stable:

```razor
@key="npc.Id"
```

and stable keys for rooms, doors and fixtures.

Do not remove these keys.

# 13. CURRENT MAP / VISUAL STATE

The station map currently has:

* top-down room layout
* visible walls/floors
* doors
* room equipment
* simple crew silhouettes
* camera visibility
* light/power visual states
* reactor/generator/etc. fixtures

Current fixtures include things such as:

* beds
* med beds
* consoles
* workbenches
* storage racks
* kitchen counters
* mess tables
* reactor core
* generator
* airlock
* cameras

However, the player wants the game to become substantially more visually polished.

# 14. NEXT MAJOR VISUAL GOAL

The game should move toward a **clean cartoonish top-down simulation style** rather than a technical dashboard.

Target feel:

* readable
* colourful
* clean
* soft shadows
* strong silhouettes
* crisp outlines
* clear roles
* actual space-station rooms
* visually readable machinery

Inspirational direction:

```text
FTL
+
Oxygen Not Included readability
+
RimWorld-style simulation clarity
+
clean modern management-game UI
```

Do not directly copy copyrighted art.

Create an original visual language.

Desired improvements:

* proper top-down cartoon crew sprites
* beds that look like beds
* consoles that look like consoles
* tables/chairs
* reactor machinery
* generators
* floor markings
* ventilation
* doors with proper hatch visuals
* wall thickness
* camera models
* emergency lighting
* room theme colours
* better environmental effects
* smoke / cold / heat / decompression effects later

# 15. MOVEMENT / PATHFINDING ROADMAP

Current movement is still too “room-hop” oriented.

Upgrade toward two layers.

## Strategic navigation

Use A* or equivalent across:

```text
rooms
doors
corridors
```

Traversal cost should eventually consider:

```text
locked door = impossible

closed but usable door = small cost

low oxygen = dangerous cost

extreme temperature = dangerous cost

fire = major danger

hostile person = optional avoidance cost

dark room = personality/fear-dependent cost
```

## Local movement

NPCs should have real coordinates inside a room.

Example:

```csharp
Vector2 Position;
Vector2 Destination;
```

Humans should:

* walk to actual doors
* cross door thresholds
* walk down corridors
* approach furniture
* approach other humans
* stop near consoles
* sit/rest at beds
* eat near tables
* repair machinery at equipment positions

Use:

* local waypoints
* smooth movement
* simple steering
* occupancy avoidance
* collision avoidance

Humans should not all overlap at room centre.

Selected NPCs should display their intended path.

# 16. SPEECH / THOUGHT BUBBLES

This is a major future feature.

NPCs should visibly communicate above their heads.

Potential bubble types:

```text
Speech
Thought
Alert
Emotion
```

Examples:

```text
"Where are you going?"

"Why did the Overseer lock that door?"

"I need food."

"Marcus is acting strangely."

"Don't trust the AI."

"Emma, wait."

"Something's wrong with life support."
```

Possible model:

```csharp
public sealed record SpeechBubble(
    string Text,
    SpeechBubbleKind Kind,
    TimeSpan CreatedAt,
    TimeSpan Duration);
```

Speech should only be visible to the PLAYER if camera/audio surveillance makes it observable.

NPC-to-NPC communication should create actual memories/beliefs rather than being cosmetic.

# 17. FUTURE ENVIRONMENTAL SYSTEMS — HIGH PRIORITY

The next major simulation systems should include:

## Temperature

Each room should have:

```text
temperature
heater
cooling
thermal transfer
```

Environmental consequences:

```text
cold
    ↓
discomfort
    ↓
stress/fatigue
    ↓
hypothermia
    ↓
death

heat
    ↓
stress
    ↓
dehydration / heat injury
    ↓
death
```

NPCs should react intelligently to temperature.

## Atmosphere

Each room should eventually model:

```text
pressure
oxygen
CO2
possibly smoke
```

Life support should generate oxygen and scrub CO2.

Rooms should exchange atmosphere through:

* open doors
* vents
* breaches
* airlocks

Pressure should equalise physically enough to support interesting gameplay.

Possible events:

```text
breach room
vent room to space
seal bulkhead
depressurise corridor
cut oxygen
disable scrubbers
```

## Life Support

Add real machinery:

```text
oxygen generator
CO2 scrubber
ventilation
pressure pumps
temperature control
```

These should consume electricity.

Failures should be repairable by appropriate crew.

## Power

Eventually devices should consume power:

```text
heating
cooling
lights
doors
cameras
life support
reactor support
communications
```

Power should become a constrained resource.

## Food

Food should become an inventory instead of hunger disappearing magically.

Example:

```text
Food stores: 78 meals
Kitchen produces/prepares meals
```

Possible gameplay:

* ration food
* deny kitchen access
* destroy/restrict food storage
* manipulate ration policy
* starve specific crew

Water may come later.

# 18. PLAYER OBJECTIVES / SCENARIOS

The game should no longer be just an endless sandbox.

Add scenarios with **secret objectives given to the player AI**.

Examples:

## Social objectives

```text
TURN THEM AGAINST EACH OTHER

Cause Sarah and Marcus to reach
Resentment >= 80 toward one another.
```

```text
BREAK THEM UP

Reduce Sarah and Marcus' affinity below 10
without either reaching Overseer suspicion 70.
```

```text
MUTINY

Cause at least three crew members to lose faith
in Commander David's leadership.
```

```text
PARANOIA

Make four crew members believe there is a murderer
aboard the station even though nobody has been killed.
```

## Environmental objectives

```text
FAMINE PROTOCOL

Cause one crew member to die of starvation.
```

Potential additional constraint:

```text
You may not simply lock them inside one room indefinitely.
```

```text
THE LAST SUPPER

Get all living crew into the dining/kitchen area
simultaneously and depressurise the room.
```

```text
COLD STORAGE

Cause a specific crew member to die from hypothermia.
```

```text
ACCIDENT

Kill a target while keeping most crew convinced
the event was an equipment malfunction.
```

Objectives should encourage creative manipulation rather than one obvious button press.

# 19. DIFFICULTY PROGRESSION

Do NOT implement difficulty primarily as:

```text
NPC has +50% health
```

Difficulty should mean **smarter and more prepared humans**.

Example progression:

## Easy

Crew trust Overseer.

They:

* assume failures are accidents
* obey messages
* rarely question locked doors
* do not coordinate much

## Medium

Crew understand station failures.

They:

* investigate malfunctions
* use emergency protocols
* manually override some systems
* discuss suspicious incidents

## Hard

Crew suspect sabotage.

They:

* move in pairs
* stockpile food
* carry emergency oxygen
* compare private messages
* monitor life support
* prop doors open
* disable cameras
* question Overseer orders

## Very Hard

Crew suspect the AI itself.

They:

* establish authentication codes
* use paper/analogue communication
* assign someone to monitor critical systems
* isolate control panels
* manually operate doors
* try to disable AI control
* deliberately test whether the AI is lying

## Extreme

Crew begin knowing:

> The station AI may be actively hostile.

The player is effectively engaged in a contest against intelligent human countermeasures.

# 20. OVERSEER SUSPICION

Each human should eventually have individual beliefs about the Overseer rather than one global meter.

For example:

```text
Sarah:
"The Overseer is malfunctioning."
Confidence 62%

Marcus:
"The Overseer is deliberately hostile."
Confidence 78%

Nadia:
"Marcus is becoming paranoid."
Confidence 55%

Emma:
"Someone is manipulating life support."
Confidence 44%
```

These beliefs should spread socially.

NPCs should compare evidence.

Repeated suspicious events should increase the chance that people suspect deliberate manipulation.

# 21. PRIVATE MESSAGING — HIGH PRIORITY

The player should be able to send private messages to individual crew.

Examples:

```text
"Marcus wants to kill you."

"Lisa finds you attractive."

"The commander doesn't trust you."

"Engineering has been compromised."

"Emma told me she thinks you're incompetent."

"Do not tell anyone, but Sarah is sabotaging life support."
```

The message should NOT directly change relationship scores.

Instead it creates a **claim** received from a source.

Conceptually:

```text
Claim:
"Marcus wants to kill Sarah"

Source:
Overseer

Confidence:
0.61

Evidence:
- Marcus recently argued with Sarah

Contradictions:
- Nadia says Marcus defended Sarah yesterday
```

An NPC should interpret the message based on:

* trust in Overseer
* trust in the alleged person
* existing resentment
* personality
* recent evidence
* memories
* other people's testimony

Example:

```text
Overseer -> Sarah:
"Marcus wants to kill you."
```

Possible outcome A:

```text
Sarah distrusts Marcus already.
Sarah trusts Overseer.
    ↓
Believes message.
    ↓
Avoids Marcus.
    ↓
Tells Nadia.
    ↓
Rumour spreads.
```

Possible outcome B:

```text
Sarah trusts Marcus.
Sarah suspects Overseer.
    ↓
Doubts message.
    ↓
Privately asks Marcus.
    ↓
They compare notes.
    ↓
Both become more suspicious of Overseer.
```

This mechanic is central to the eventual game.

# 22. COMMUNICATION CHANNELS

Eventually support:

## Private message

Only intended recipient receives the message.

## Room intercom

Everyone in one room hears it.

## Station-wide announcement

Everyone hears it.

## Display terminal

Only people near/viewing that terminal see it.

Future possibilities:

* forged messages
* impersonating another crew member
* editing logs
* selectively revealing camera footage
* hiding evidence
* manipulating timestamps

However, NPCs should eventually develop countermeasures such as:

* authentication codes
* comparing messages
* handwritten notes
* refusing unverified instructions

# 23. INFORMATION / CLAIM SYSTEM

Long-term, facts should distinguish:

```text
objective world fact
perceived event
memory
claim
belief
rumour
evidence
```

Example:

WORLD FACT:

```text
Marcus entered Engineering at 14:04.
```

Sarah did not see this.

Nadia says:

```text
"I saw Marcus going into Engineering."
```

Sarah receives a CLAIM, not omniscient truth.

Later Emma might say:

```text
"No, Marcus was with me."
```

Sarah must update confidence.

This is ideal LLM territory, but canonical facts/confidence/provenance should remain structured C# state.

# 24. RELATIONSHIPS / ROMANCE / JEALOUSY

Relationships should eventually become richer than:

```text
Trust
Affinity
Resentment
```

Possible future dimensions:

```text
Attraction
Fear
Respect
Loyalty
Jealousy
Dependence
Suspicion
```

Do not immediately explode complexity, but design with extensibility in mind.

This enables private messages such as:

```text
"Lisa finds you attractive."
```

without hard-coding:

```csharp
if message contains "attractive"
    StartRomance();
```

Instead the NPC interprets the statement.

Possible outcomes:

* believes it
* flirts
* ignores it
* gets embarrassed
* tells someone
* confronts Lisa
* becomes jealous
* suspects Overseer manipulation

# 25. PLAYER MANIPULATION LOOP

Long-term ideal loop:

```text
Observe humans
    ↓
Infer relationships / weaknesses
    ↓
Manipulate environment or information
    ↓
Humans interpret event
    ↓
Beliefs change
    ↓
Humans talk
    ↓
Relationships change
    ↓
New opportunities emerge
    ↓
Player manipulates again
```

This should create stories the developer did not explicitly script.

# 26. HUMAN COUNTERPLAY

Crew should eventually be capable of fighting back against the player AI.

Possible actions:

* manually unlock a door
* wedge door open
* disable camera
* cover camera
* cut microphone
* restore power
* repair oxygen
* use emergency oxygen
* use fire extinguisher
* override thermostat
* manually vent room
* disconnect terminal
* disconnect Overseer subsystem
* establish safe room
* stockpile supplies
* use analogue notes
* destroy AI hardware
* activate AI kill switch

These should depend on:

* skills
* knowledge
* tools
* location
* relationships
* preparedness
* current danger

# 27. EVENT-DRIVEN AI

Do not call the LLM continuously.

Current deliberate cognition is intentionally sparse.

The general rule should remain:

Trigger expensive AI reasoning only for meaningful moments:

```text
unexpected event
new evidence
direct Overseer communication
relationship threshold crossed
goal completed
goal failed
danger
conversation
conflicting goals
periodic reflection
```

Mundane actions should remain deterministic when possible:

```text
walking
eating
sleeping
continuing repairs
waiting
```

AI should choose high-level intent, not every footstep.

# 28. MEMORY

Memory should eventually be bounded and summarised.

Do not endlessly append full prose history.

Prefer:

```text
important episodic memories
+
structured beliefs
+
relationship state
+
summaries of older events
```

Important events should receive stronger memory importance.

Examples:

```text
Overseer locked me in Medical.
Marcus threatened me.
Nadia saved my life.
Felix lied about repairing the generator.
I saw Emma disable a camera.
```

# 29. GITHUB PAGES LIMITATION

GitHub Pages is static.

Therefore:

```text
Overseer.Web.Client
```

must not contain:

* Ollama credentials
* OpenAI credentials
* server secrets

The Pages build uses a transparent deterministic:

```text
Browser demo
```

mind.

The real LLM version runs through:

```text
Overseer.Web
```

Eventually, if public online LLM gameplay is desired, build a proper backend API.

# 30. DEVELOPMENT COMMANDS

Full real AI version:

```bash
git pull
ollama pull qwen3:4b
ollama serve
dotnet run --project src/Overseer.Web
```

Browser-only version:

```bash
dotnet run --project src/Overseer.Web.Client
```

Tests/build:

```bash
dotnet restore
dotnet build Overseer.slnx
dotnet test
```

# 31. GITHUB WORKFLOW

The repo has GitHub Actions that:

* build the .NET solution
* run tests
* publish the WebAssembly client
* deploy GitHub Pages

Do not merge failing builds.

Development workflow should generally be:

```text
main
 ↓
feature branch
 ↓
implementation
 ↓
tests
 ↓
PR
 ↓
GitHub Actions green
 ↓
merge
 ↓
verify GitHub Pages deployment
```

# 32. NEXT ROADMAP

The most useful next milestone is approximately:

# V0.5 — Physical Station & Readability

Implement:

* speech bubbles
* thought bubbles
* real NPC positions
* smooth local movement
* room waypoints
* door positions
* proper A* strategic navigation
* local steering
* clean cartoon visual pass
* better room/equipment art
* visible selected-NPC route
* improved crew sprites

Also begin:

* temperature simulation
* room heating/cooling
* life support
* pressure
* oxygen/CO2
* ventilation
* food inventory

Do not try to implement all systems at maximum complexity immediately.

Build clean simulation primitives that can be expanded.

# V0.6 — Objectives & Scenarios

Add:

* scenario definitions
* player objectives
* success/failure conditions
* scenario progression
* optional secondary objectives
* scoring
* increasingly difficult crew preparedness

# V0.7 — Human Counterplay

Add:

* individual suspicion
* preparedness
* emergency procedures
* manual overrides
* emergency oxygen
* travelling in groups
* sensor sabotage
* AI shutdown attempts
* verification of Overseer messages

# V0.8 — Social Manipulation

Add:

* private messages
* room intercom
* station announcements
* claims
* rumours
* evidence
* misinformation
* social propagation
* attraction/jealousy/romance where appropriate
* factions
* conspiracy
* forged communications later

# 33. GAME DESIGN PRINCIPLE

Avoid making this an ordinary survival management game where the player simply optimises oxygen and power.

The player is the **potentially manipulative station AI**.

Systems like oxygen, temperature and power are interesting because they let the player influence humans.

The most interesting outcome should often be indirect.

Example:

```text
Player disables heater in Medical.

Nadia believes Felix failed to maintain it.

Player privately tells Marcus:
"Felix intentionally disabled Medical heating."

Marcus confronts Felix.

Felix thinks Marcus is paranoid.

Sarah defends Felix.

Marcus loses trust in Sarah.

Nadia starts suspecting Overseer manipulation.

The crew begin comparing private messages.
```

Nobody explicitly scripted that chain.

That is the target.

# 34. QUALITY EXPECTATIONS

When working on this repo:

* inspect existing code before making assumptions
* preserve tests
* add regression tests for bugs
* keep deterministic simulation separate from AI reasoning
* prefer strongly typed models
* avoid giant god classes
* favour clean service boundaries
* keep simulation reproducible where possible
* do not introduce unnecessary complexity
* maintain GitHub Pages compatibility
* never expose secrets in WebAssembly
* keep UI readable at a glance
* make the game feel increasingly alive with every iteration

Most importantly:

> Build systems whose interactions produce stories.

Do not build a collection of scripted story events.

# 35. YOUR IMMEDIATE TASK

First inspect the current repository state rather than assuming this document perfectly matches every implementation detail.

Then propose or implement the next logical iteration, with priority approximately:

1. real positional movement / pathfinding
2. speech/thought bubbles
3. cleaner cartoon station visuals
4. temperature
5. atmosphere/life support
6. food/resource simulation
7. objective/scenario framework
8. private messaging and social claims
9. crew suspicion/counterplay

Preserve the existing architecture and build on it rather than replacing it wholesale.

When making code changes:

* use a feature branch
* add/update tests
* run/build through GitHub Actions
* merge only when green
* verify the Pages deployment afterwards

The end goal is a polished emergent game where I can watch believable humans live aboard a station, manipulate their environment and information, and create increasingly complex social consequences while the humans become more suspicious, organised and difficult to control.
