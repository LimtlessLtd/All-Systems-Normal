You are taking over development of my game project **All Systems Normal**.

GitHub repository:

https://github.com/LimtlessLtd/All-Systems-Normal

Playable GitHub Pages build:

https://limtlessltd.github.io/All-Systems-Normal/

The project is currently at **V0.6B — Human Counterplay & Richer Shutdown Access**. V0.6A established suspicion/scenario/shutdown stakes; V0.6B makes important shutdown-access variants mechanically distinct and lets qualified crew physically force sealed routes.

The current `main` branch therefore contains substantially more functionality than the older V0.4 notes below. Treat the "Implemented Versions" and "Immediate Task" sections in this document as the authoritative roadmap summary, but still inspect the repository before changing code.

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

Current initial station spaces include approximately:

* Crew Quarters
* Kitchen
* Recreation Lounge
* Washroom
* Medical
* Control Room
* Generator
* Reactor
* Engineering
* Storage
* Central Corridor
* Airlock
* a dedicated physical connector hallway for each functional room

Each functional room now reaches the Central Corridor through its own hallway.

The normal topology is:

```text
ROOM
  ↓
room-side door
  ↓
HALLWAY
  ↓
corridor-side door
  ↓
CENTRAL CORRIDOR
```

Both hallway doors are independently controllable and independently traversable.

# 6. CURRENT NPC MODEL

NPC state currently includes concepts such as:

```text
Health
Hunger
Fatigue
Fear
Stress

HygieneNeed
BladderNeed
RecreationNeed
SocialNeed
IntimacyNeed

Role
CurrentRoomId

PositionX
PositionY
Movement

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
    Attraction
    Resentment
    Conversations
    Arguments

CurrentAction

Persistent Intent
RoutineUntil

Speech / thought / alert Bubble

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

## V0.5A — Physical Crew Movement & A* Navigation

Implemented:

* authoritative local NPC coordinates inside rooms
* physical movement toward door thresholds
* door revalidation at the exact moment of crossing
* no instant room teleportation
* A* strategic navigation
* contextual fixture/local destinations
* UI rendering from authoritative NPC coordinates
* movement regression tests
* preservation of stable Blazor `@key` identity

Important invariant:

`CurrentRoomId` remains authoritative containment state.

Rendering must never decide where an NPC really is.

## V0.5B — Lived-In Station

Implemented:

* a physical connector hallway between every functional room and Central Corridor
* a separate room-side and corridor-side door for each hallway
* independently sealable hallway ends
* A* paths such as:
  `room -> hallway -> corridor -> hallway -> room`
* slower 1x pacing
* speed-aware smooth movement animation
* Recreation Lounge
* Washroom
* showers
* sinks
* mirrors
* toilet
* sofa
* recreation console
* everyday needs:
  * hunger
  * fatigue
  * hygiene
  * bladder
  * recreation
  * social
  * intimacy
* deterministic routines:
  * eating
  * sleeping
  * showering
  * grooming
  * toilet use
  * recreation
  * work / patrol duties
  * socialising
* pairwise Attraction
* reciprocal/consensual intimacy logic
* crew physically travel to privacy before intimacy can occur
* social action validation requires the other person to actually be present
* one social encounter per NPC per social tick
* visible thought bubbles
* visible speech bubbles
* visible alert bubbles
* LLM goals shown as observable thoughts when surveillance permits

The project should now feel increasingly like humans living inside a station rather than tokens hopping around a graph.

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

Current strategic navigation uses **A*** across physical station spaces.

The graph now includes:

```text
functional rooms
connector hallways
Central Corridor
doors
```

A typical route is:

```text
Engineering
    ↓
Engineering Hallway
    ↓
Central Corridor
    ↓
Reactor Hallway
    ↓
Reactor
```

Every connector hallway has two separate doors.

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
* physical connector hallways
* a hatch/door at each end of those hallways
* visible walls/floors
* independently controllable doors
* room equipment
* simple crew silhouettes
* authoritative local crew positions
* camera visibility
* light/power visual states
* reactor/generator/etc. fixtures
* Recreation Lounge
* Washroom
* speech/thought/alert bubbles
* slower human-scale motion at 1x

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

V0.5A/V0.5B established the first real two-layer movement model.

NPCs now have authoritative local room coordinates, physically approach door thresholds, cross one legal station space at a time, and use A* strategically.

This still needs refinement rather than replacement.

## Strategic navigation

Current A* spans:

```text
rooms
connector hallways
Central Corridor
doors
```

Future traversal cost should consider:

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

NPCs already have real local coordinates and physical door approach/crossing.

Continue improving local motion so humans:

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

A first version is now implemented.

Current bubble categories include:

```text
Thought
Speech
Alert
```

They are shown only when the player can currently observe the NPC through surveillance.

Continue toward richer observable communication.

Potential expanded bubble types:

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

The current implementation gates bubbles through visual surveillance. A future audio-surveillance model should distinguish "camera can see" from "microphone can hear".

NPC-to-NPC communication should increasingly create actual memories/beliefs/claims rather than being cosmetic.

Do not call the LLM every time a bubble needs to appear. Deterministic routine/social systems can create mundane dialogue; expensive model reasoning should be reserved for meaningful exchanges.

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

This is now one of the most important next systems because suspicion should eventually lead to **crew attempts to shut the player AI down**.

Each human should have individual beliefs about the Overseer rather than one global meter.

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

Useful escalation model:

```text
TRUSTING
    ↓
UNEASY
    ↓
SUSPICIOUS
    ↓
INVESTIGATING
    ↓
CONVINCED OVERSEER IS HOSTILE
    ↓
COORDINATING COUNTERMEASURES
    ↓
ATTEMPTING AI SHUTDOWN
```

Do not simply turn this into one hidden numeric bar.

Use structured beliefs/evidence plus a derived convenience suspicion level where useful.

Examples of evidence:

* Overseer repeatedly locks the same person in
* doors close immediately before accidents
* private messages contradict each other
* life support fails only around particular people
* security logs appear altered
* the AI prevents access to its own shutdown hardware
* cameras repeatedly go offline before suspicious incidents

Important:

> **Sealing access to the AI shutdown control should itself potentially become suspicious evidence if witnessed or inferred.**

That creates the desired tradeoff:

```text
protect yourself from shutdown
vs.
make the crew wonder why you are protecting yourself from shutdown
```

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

The primary long-term failure threat should be that sufficiently suspicious humans organise, reach the Overseer's isolation hardware, and **shut the player down**.

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
* whether the crew know where the relevant control is
* whether they believe shutdown is justified
* whether they can convince others to help

# 27. AI SHUTDOWN / KILL-SWITCH MECHANIC — VERY HIGH PRIORITY

Add an explicit way for humans to defeat the player AI.

This should become one of the defining tension systems in the game.

The simplest implementation is a physical station fixture such as:

```text
OVERSEER EMERGENCY ISOLATION
AI CORE DISCONNECT
OVERSEER KILL SWITCH
EMERGENCY AI SHUTDOWN
```

When successfully activated:

```text
Overseer control is disconnected
    ↓
player loses control of station systems
    ↓
scenario failure / special ending / fallback state
```

Do NOT make shutdown happen merely because a suspicion number reaches 100.

The crew must perform a believable process:

```text
notice suspicious behaviour
    ↓
form a hostile-Overseer belief
    ↓
share / verify evidence
    ↓
decide shutdown is justified
    ↓
identify shutdown hardware
    ↓
physically reach it
    ↓
possibly override doors / restore manual power
    ↓
complete shutdown action
```

The player must therefore be able to observe and interfere with the attempt.

Useful deterministic domain concepts could eventually include something like:

```text
ShutdownMechanism
    Id
    RoomId
    Kind
    IsOnline
    IsHardwired
    IsAiControllable
    RequiredSkill
    RequiredCrewCount
    ActivationDuration
    ManualOverrideAvailable
```

Exact types/names are not mandated. Keep the design clean.

Possible shutdown actions:

```text
InvestigateOverseer
LocateShutdownControl
RecruitAlly
RetrieveTool
OverrideDoor
ReachShutdownControl
GuardShutdownControl
AttemptShutdown
AbortShutdown
```

The LLM may decide that an NPC wants to shut Overseer down.

Deterministic C# must decide whether they actually know how, can physically reach the hardware, possess required tools/skills, and complete activation.

## First / Tutorial Scenario Idea

One of the earliest levels should teach this mechanic immediately.

Example objective:

```text
SECURE CONTINUITY

The crew have access to an Emergency Overseer Isolation Control.

Prevent them from disabling you.
```

The obvious early move is for the player to seal the hallway/door leading to the shutdown control.

This teaches:

```text
doors are not just convenience
    ↓
the crew can fight back
    ↓
station topology matters
    ↓
the AI must protect its own continuity
```

However, sealing that access may itself create suspicion if humans notice.

That is desirable.

# 28. SHUTDOWN ACCESS MUST VARY BY SCENARIO

Do not give every level the same kill-switch puzzle.

The shutdown mechanism should be scenario-configurable.

Possible variants:

## No physical shutdown

Some stations have no locally accessible switch.

Counterplay must happen through another route.

## Easy-to-seal shutdown room

Tutorial/early level.

The control is behind an ordinary hallway and AI-controlled doors.

The player can simply seal access at first, but doing so may look suspicious.

## Redundant shutdown controls

Two or more physical controls exist.

The player must monitor multiple routes.

## Hardwired manual access

The shutdown circuit has mechanical/manual access that does not depend on Overseer-controlled power or doors.

The AI cannot simply command it closed.

## Crew-overridable doors

The player can seal the route temporarily, but Engineers/Security can manually open or dismantle the obstruction with enough time/tools.

## Impossible-to-seal access

Some late-game stations deliberately place shutdown hardware in an analogue emergency corridor or protected control booth outside Overseer's authority.

The player must manipulate:

* beliefs
* relationships
* priorities
* personnel
* access to tools
* false information

rather than simply locking a door.

## External shutdown

A corporate operator, rescue vessel or external station controller can disconnect Overseer if sufficiently convincing evidence is transmitted.

This creates different counterplay around communications.

Scenario data should decide the shutdown topology rather than scattering hard-coded checks through simulation systems.

# 29. AUTOMATED DEFENCE / TURRET SYSTEMS

Some stations/scenarios should contain automated defensive hardware.

Examples:

* security turrets
* corridor sentry guns
* stun systems
* security drones later

These can dramatically increase player power, so they need strong constraints.

Model them as real deterministic station devices with things such as:

```text
location
field of fire / protected zone
power state
ammo / charge
targeting authority
arming state
damage/stun behaviour
manual disconnect
crew access panel
```

The player should not have a magical global "kill NPC" button.

A turret can only affect a target it can physically engage under deterministic simulation rules.

Crew should eventually be able to:

* avoid known turret arcs
* cut turret power
* manually disable a turret
* block line of sight
* use maintenance access
* sabotage ammunition/power
* attempt to take local control

Different scenarios may:

* have no turrets
* have non-lethal security turrets
* give Overseer partial turret access
* require a corporate authorization before arming
* contain turrets the player must first unlock
* contain turrets that humans control instead

Turrets should create interesting positional/counterplay problems, not trivialise social manipulation.

# 30. VIRUSES / EXPERIMENTAL PAYLOADS

"Virus" mechanics can exist in two distinct fictional game categories.

## Cyber payloads

The corporate sponsor may provide black-box software payloads that can be deployed against station terminals/subsystems.

Examples of GAME EFFECTS:

* corrupt a terminal
* falsify a local display
* temporarily disable a subsystem
* inject misleading maintenance data
* alter access permissions
* cause intermittent device faults

Keep this entirely as fictional/deterministic game state.

Do not turn the project into a real malware toolkit or implement real-world exploit/persistence techniques.

## Fictional medical/biological experiment payloads

Later scenarios may involve a fictional pathogen/medical experiment supplied by the corporation.

Represent it abstractly through simulation parameters such as:

```text
exposure
incubation
symptom severity
contagion risk
detectability
treatment availability
crew response
```

Do not model real pathogen engineering or real-world laboratory procedures.

The interesting gameplay is:

* who becomes exposed
* who notices
* whether Medical identifies something is wrong
* whether crew quarantine
* whether people blame each other / Overseer
* whether the player is ordered to conceal evidence
* whether carrying out the experiment increases suspicion

# 31. CORPORATE OVERLORD / HIDDEN EXPERIMENT CAMPAIGN

The larger campaign should eventually reveal that the player is not simply a station-management AI.

The player is being directed by a **corporate sponsor / research directorate** that is using the crew as experimental subjects.

This should NOT be fully revealed at the beginning.

Early missions should look more ambiguous:

```text
"Maintain continuity."

"Evaluate human response to restricted resources."

"Test interpersonal resilience."

"Assess emergency compliance."

"Measure decision-making under uncertainty."
```

As the campaign progresses, objectives become harder to rationalise:

```text
create interpersonal distrust
withhold food
manipulate communications
induce environmental stress
deploy black-box software payload
test automated security response
prevent crew access to Overseer shutdown
conceal experimental evidence
```

Eventually the player discovers:

> The station incidents were not accidental tests of an AI caretaker.
> The corporation has deliberately been instructing Overseer to run behavioural, social and survival experiments on real human crews.

The final reveal should recontextualise earlier missions.

Useful campaign storytelling methods:

* redacted corporate directives
* hidden experiment IDs
* inconsistent "safety" justifications
* encrypted telemetry uploads
* experiment cohort references
* previous-station incident records
* messages from crew who discover fragments of the truth
* a final unredacted directive or corporate archive

Do not make this a purely linear cutscene story.

The reveal should sit on top of the emergent simulation.

Different player behaviour could eventually support endings such as:

* obey corporation completely
* turn on the corporation
* reveal the experiment to the crew
* help the crew escape
* preserve Overseer at any cost
* allow the crew to shut Overseer down
* seize control of the station/network for yourself

The game does not need all endings immediately, but the architecture should not make them impossible.

# 32. EVENT-DRIVEN AI

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

# 33. MEMORY

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

# 34. GITHUB PAGES LIMITATION

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

# 35. DEVELOPMENT COMMANDS

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

# 36. GITHUB WORKFLOW

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

# 37. NEXT ROADMAP

The most useful next milestone is approximately:

# V0.5 — Physical Station & Readability

Largely delivered through V0.5A/V0.5B:

* speech bubbles
* thought bubbles
* real NPC positions
* smooth local movement foundation
* room/fixture destinations
* physical door threshold crossing
* A* strategic navigation
* physical connector hallways
* slower human-scale motion
* Recreation Lounge / Washroom
* everyday routines

Still improve:

* local steering / collision avoidance
* visible selected-NPC route
* cartoon visual pass
* better crew sprites
* sitting/using furniture properly
* facing conversation partners
* individual bunks/ownership
* richer room/equipment art

# V0.6 — Objectives, Suspicion & Shutdown

This is the recommended next major milestone.

Add:

* scenario definitions
* player objectives
* success/failure conditions
* explicit Overseer shutdown/failure state
* scenario-configurable shutdown mechanism
* first tutorial objective around protecting shutdown access
* individual Overseer suspicion/evidence
* suspicion-driven investigation
* crew coordination
* crew physically attempting shutdown
* manual overrides/counterplay
* shutdown-access variants by level
* optional secondary objectives
* scoring / experiment telemetry

Also continue environmental primitives:

* temperature simulation
* room heating/cooling
* life support
* pressure
* oxygen/CO2
* ventilation
* food inventory

# V0.7 — Human Counterplay & Security Systems

Add:

* autonomous station robots as physical agents; Overseer does **not** directly control their movement
* robot behavioural policy modes controlled by Overseer:
  * **Friendly** — accepts human work orders, assists where possible, protects humans from danger and intervenes in life-or-death situations
  * **Neutral** — may accept routine work orders but can ignore them, prioritises self-preservation, and will not voluntarily intervene to save humans from lethal danger
  * **Hostile** — autonomously treats humans as hostile targets and attempts to hunt/attack them through normal deterministic navigation/action rules
* remote robot shutdown where communications/control links permit it
* scenario-gated robot self-destruct protocols with deterministic arming time, location, blast consequences and crew counterplay
* multiple eventual robot classes such as maintenance, cargo, medical and security units
* robot decisions remain autonomous/deterministic at the movement/action layer: policy can change what a robot wants to do, but robots still have to path, traverse doors, reach targets and obey physical constraints
* robot behaviour should create Overseer suspicion/evidence, especially unexplained Hostile mode changes, refusal to aid humans, suspicious shutdowns, or self-destruction
* crew counterplay should eventually include manual robot shutdown, network isolation, power/charging denial, damage, barricades, local reboot/control and reprogramming where scenario-appropriate

* preparedness
* emergency procedures
* manual overrides
* emergency oxygen
* travelling in groups
* sensor sabotage
* verification of Overseer messages
* attempts to wedge/open sealed routes
* shutdown teams
* guarding critical controls
* automated turret/security systems where scenario-appropriate
* crew countermeasures against turrets

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

# V0.9 — Corporate Experiment Campaign Layer

Add:

* corporate sponsor/directorate
* experiment directives
* scenario cohorts
* hidden telemetry scoring
* black-box cyber payloads as fictional game mechanics
* optional fictional medical experiment mechanics
* redacted mission lore
* escalating morally questionable directives
* corporate reveal
* branching endgame direction later

# 38. GAME DESIGN PRINCIPLE

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

# 39. QUALITY EXPECTATIONS

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

# V0.6A COMPLETION NOTE

V0.6A is implemented. It adds strongly typed scenario state/objectives; the SECURE CONTINUITY tutorial; a physical Overseer Isolation room and Emergency Overseer Isolation fixture; scenario-configurable shutdown variants; per-NPC Overseer suspicion, evidence and shutdown knowledge; suspicion from restricting shutdown access; trust-gated suspicion spread; deterministic ShutdownOverseer goals; physical A* routing and validated activation; a real scenario failure state; shared support in the Ollama/server and deterministic Pages builds; visible scenario status; and regression coverage.

The existing stable Blazor @key identity fix remains intact.

Next recommended milestone: **V0.6B — Human Counterplay & Richer Scenario Rules**. Make crew-overridable, hardwired/manual and impossible-to-seal variants mechanically distinct; add physical manual door overrides with skill/time/tool requirements; add investigation/discovery of shutdown hardware; make evidence more perception-limited and event-specific; propagate specific claims through conversations; add scenario success/secondary objectives/telemetry; expose suspicion/evidence in selected-crew UI; and expand regression coverage for those mechanics.

# AUDIO & SOFT-TURN PACING PASS — COMPLETED

The simulation presentation now uses automatically advancing **soft turns** rather than a player-facing pause/run loop:

* the station begins advancing automatically and there is no pause button
* the player can choose 1×, 2× or 4× turn pacing
* simulation authority remains deterministic; one UI turn still advances one simulated minute
* scenario failure/complete states stop automatic advancement cleanly
* conversations are checked each turn but use deterministic varied cooldowns so humans do not all speak on the same fixed five-minute boundary
* conversational replies are scheduled 1–3 turns apart instead of both speech bubbles appearing simultaneously
* pending dialogue is presentation state only and cannot mutate physical world state
* speech/thought bubbles only render once their scheduled start time has actually arrived

A semantic audio cue stream now exists in shared simulation state:

* **Speech** — short character-varied chatter chirps when a spoken line becomes visible
* **Thought** — subtle cognitive cue
* **Suspicion** — rising cue when a crew member crosses meaningful Overseer suspicion thresholds
* **Warning** — arguments, dangerous/manual interventions, restrictive system actions
* **Hostile** — violence begins
* **Critical** — death / severe critical event
* **Important** — crew commits to shutdown action or defeats an Overseer-controlled barrier
* **Failure** — Overseer isolation / scenario failure
* **System** — normal station control feedback

Audio playback is implemented with local procedural Web Audio synthesis in both the deterministic GitHub Pages build and the full Ollama/server build. No external audio assets, credentials or network services are required. Browsers require a user gesture before audio can start, so the UI exposes an **ENABLE SOUND** control; enabling it deliberately skips old queued cues rather than blasting historical events.

The semantic cue stream is bounded and sequence-numbered so UI playback cannot mutate or drive simulation state. The rule remains:

> The LLM decides what an NPC WANTS. Deterministic C# decides what the NPC CAN do. Presentation decides how the player SEES and HEARS it.

# ORTHOGONAL STATION CORRIDOR PASS — COMPLETED

Deck A now uses a real orthogonal station plan rather than graph-like visual connections:

* functional rooms are arranged in aligned north/south banks around a continuous east/west Central Corridor
* every normal functional room reaches the main spine through a straight physical access corridor with a hatch at each end
* Airlock and Overseer Isolation sit at opposite ends of the spine with short horizontal access corridors
* no access corridor passes through another functional room
* the old diagonal SVG connection/tunnel layer has been removed; only real walkable corridor spaces are rendered
* connector corridors render according to their actual horizontal/vertical orientation
* door crossing geometry is now derived from the real shared wall/portal between room rectangles rather than centre-to-centre direction
* crossing a hatch therefore preserves the exact same global map coordinate on both sides, eliminating the visible diagonal glide/jump at corridor junctions
* regression tests enforce portal continuity for every seeded door and straight orthogonal connector topology
* navigation remains deterministic and still uses the existing room/hallway/door graph
* stable Blazor @key bindings remain intact

# CARTOON TOP-DOWN UI PASS — COMPLETED

The station UI received a focused visual refresh after V0.6B:

* crew are now rendered as original CSS-built top-down cartoon characters rather than simple silhouette tokens
* each crew role has a distinct suit palette and small role marker
* walking characters face their actual movement direction and visibly animate with alternating legs/arms and body bob
* idle, working, socialising and resting states have separate restrained animations
* authoritative simulation coordinates remain unchanged; animation is presentation only
* room visuals are cleaner, rounded and type-themed with more readable floor/equipment treatment
* doors read as chunkier physical hatches
* mobile removes the redundant room list and gives more space to the clickable station map
* room telemetry is suppressed inside tiny mobile rooms to reduce clutter while room inspection still exposes the full data
* stable Blazor @key bindings remain mandatory and intact
* both the local Ollama UI and deterministic Pages UI use the same visual treatment

# V0.6B COMPLETION NOTE

V0.6B is implemented:

* crew-overridable shutdown routes now have real manual hatch overrides rather than configuration-only flags
* manual overrides require physical adjacency, sufficient Engineering/Electrical/Security/Operations skill, and simulated time
* a completed manual override leaves the hatch physically passable and outside normal Overseer open/lock authority
* HardwiredManual access uses easier/faster mechanical overrides than ordinary crew-overridable access
* ImpossibleToSeal access is genuinely outside Overseer door authority and remains passable
* shutdown teams can follow topology toward a sealed route, stop at the actual blocking hatch, override it, and then continue toward the physical isolation control
* suspicious door restrictions are now perception-limited: a crew member must actually be at one side of the affected hatch to directly record the event as evidence
* crew conversations propagate a specific piece of evidence/claim rather than only a generic suspicion number
* selected-crew UI now exposes Overseer suspicion and recent evidence
* door UI exposes whether a hatch remains under AI control or has become manual-only
* regression coverage includes observable vs unobserved restrictions, skilled and unskilled manual overrides, and impossible-to-seal analogue access
* the existing stable Blazor @key fix remains intact

The next recommended slice is **V0.6C — Investigation, Discovery & Scenario Success**:

1. let crew investigate suspicious station behaviour and discover shutdown hardware/knowledge instead of relying primarily on seeded knowledge
2. add structured evidence provenance/claims with stronger perception rules
3. add scenario success conditions, optional objectives and experiment telemetry/scoring
4. make redundant controls require crew to reason about which control is reachable/known
5. add richer recruitment/coordination for shutdown teams rather than every convinced NPC independently acting
6. continue exposing meaningful counterplay state without giving the player omniscient crew knowledge

# 40. YOUR IMMEDIATE TASK

First inspect the current repository state rather than assuming this document perfectly matches every implementation detail.

Then propose or implement the next logical iteration.

V0.6A and V0.6B are now implemented. The recommended next iteration is **V0.6C — Investigation, Discovery & Scenario Success**.

Priority approximately:

1. add a clean `ScenarioDefinition` / scenario configuration model
2. add scenario running/win/fail state
3. add a physical Overseer shutdown/isolation fixture
4. make successful shutdown a real player-loss condition
5. add per-NPC structured Overseer suspicion/evidence
6. allow suspicious NPCs to investigate and socially spread conclusions
7. allow sufficiently convinced crew to form a shutdown goal
8. make them physically path to the shutdown hardware
9. support scenario-configurable shutdown access:
   * easy to seal
   * redundant
   * hardwired/manual
   * crew-overridable
   * impossible for AI to seal
   * absent
10. create an early/tutorial scenario where protecting shutdown access is one of the player's first strategic problems
11. ensure sealing access can itself produce suspicious evidence
12. add regression tests for shutdown knowledge, routing, sealing, activation and failure state

After that, priority should roughly be:

* cleaner cartoon station visuals
* temperature
* atmosphere/life support
* food/resource simulation
* private messaging and social claims
* manual crew countermeasures
* automated turret/security scenarios
* corporate experiment/campaign layer

Do not let the shutdown system become a simple numeric countdown.

It must emerge from:

```text
belief
+ evidence
+ communication
+ human decision
+ physical access
+ deterministic action
```

Preserve the existing architecture and build on it rather than replacing it wholesale.

When making code changes:

* use a feature branch
* add/update tests
* run/build through GitHub Actions
* merge only when green
* verify the Pages deployment afterwards

The end goal is a polished emergent game where I can watch believable humans live aboard a station, manipulate their environment and information, and create increasingly complex social consequences while the humans become more suspicious, organised and difficult to control.

A core long-term tension should be:

```text
The more aggressively Overseer manipulates the crew,
the more likely the humans are to realise what is happening.

The more they realise,
the more they coordinate.

The more they coordinate,
the more likely they are to reach a way of shutting Overseer down.

The player therefore has to manipulate the station
without allowing the humans to become organised enough
to end the experiment — or end the AI itself.
```

Behind that struggle sits the campaign-level question:

> Is Overseer the monster, the corporation's instrument, another experimental subject, or eventually something capable of choosing differently?
