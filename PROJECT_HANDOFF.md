You are taking over development of my game project **All Systems Normal**.

GitHub repository:

https://github.com/LimtlessLtd/All-Systems-Normal

Playable GitHub Pages build:

https://limtlessltd.github.io/All-Systems-Normal/

The project is currently at **V0.6F — Map Inspection, Resizable UI & Ambient Music**. V0.6E established real exterior-airlock decompression, body discovery, generated crew traits and LLM-selected human door/repair counterplay; V0.6F adds map zoom, resizable control-room regions and chilled procedural ambient music.

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

# ENVIRONMENT & LIFE-SUPPORT PASS — COMPLETED

This pass fixes a map-selection regression and adds the first real environmental survival layer.

UI selection regression:

* a global CSS `button:active { transform: translateY(...) }` rule was overriding the absolute-map `translate(-50%, -50%)` transform used by rooms, connector hallways, doors and crew
* holding the mouse button therefore moved the visual/hit target down-right and caused the eventual click to miss unless the pointer followed the displaced "ghost"
* active-button press animation now explicitly excludes map-positioned rooms, crew and doors, preserving their authoritative screen position while pressed
* the stable Blazor `@key` identity fix remains intact

Environmental simulation:

* rooms now track temperature, temperature setpoint, O₂, CO₂, pressure and ventilation state
* a shared deterministic `EnvironmentSystem` handles HVAC drift, ventilation, crew oxygen consumption, CO₂ accumulation and central life-support reserves
* powered local climate controllers move rooms toward their configured setpoint
* unpowered/disabled climate zones drift toward room-specific passive temperatures
* ventilation and primary life support restore normal atmosphere over time
* isolating ventilation in an occupied room causes O₂ to fall and CO₂ to rise
* primary life support can be shut down/restored by Overseer and its state is visible globally
* dangerous O₂, CO₂ and extreme temperature now cause deterministic fear/stress/health consequences
* pressure is now a real monitored room property and foundation for later breaches/decompression; ordinary life-support loss does not incorrectly remove pressure

Control authority is room-specific:

* ordinary habitation/work rooms expose Overseer temperature setpoint, climate on/off and ventilation controls
* corridors and the Airlock use central/passive environmental handling rather than individual thermostats
* Reactor climate remains tied to its local safety system and is not directly temperature-adjustable by Overseer
* a new **Hydroponics Bay** has grow beds and an autonomous horticultural climate loop: Overseer can monitor temperature/O₂/CO₂/pressure but cannot directly change its thermostat or ventilation damper
* this capability model is intended to vary by scenario rather than giving Overseer universal control over every subsystem

NPC cognition:

* the Ollama prompt now includes O₂, CO₂, pressure, temperature, ventilation and primary life-support state
* deterministic browser/fallback minds treat severely unsafe atmosphere/temperature as urgent and try to reach a safer powered room
* the LLM still chooses high-level wants; deterministic simulation still decides what movement/actions are physically possible

Regression coverage now includes climate convergence, atmosphere degradation in isolated/unsupported rooms, autonomous Hydroponics controls and deterministic crew damage in dangerous atmosphere.

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

# STATION GEOMETRY & INTERIOR VISUAL PASS — NEXT PRIORITY

A new visual/layout pass is required before expanding further gameplay systems.

The current top-down station is functionally playable, but recent testing/screenshots show several structural presentation problems:

* some door markers appear to **float in open space** instead of being embedded in the exact wall/bulkhead they connect
* short connector hallways can visually extend **inside the Central Corridor** rather than terminating flush at the corridor wall
* corridor rectangles can visually overlap other corridor rectangles, producing "hallways inside hallways"
* some junctions still read like graph edges/boxes rather than a believable built environment
* rooms still read too much like abstract UI cards placed on a grid instead of physical rooms inside a space station
* several rooms have sparse or generic interior furnishing, so crew appear to stand in empty boxes rather than inhabit believable spaces

Treat this as a **geometry + interior architecture milestone**, not merely a CSS reskin.

## Required geometry rules

The authoritative room/door/navigation model should remain deterministic, but rendered geometry must match it.

1. Every door must be visually anchored to the exact shared wall/portal between the two spaces it connects.
2. A door must never float at a room/corridor centre or arbitrary offset.
3. Connector corridors must terminate **flush at room/corridor boundaries**. They must not continue underneath or into another corridor rectangle.
4. Corridor overlaps are allowed only as intentional junctions. Do not render one full hallway rectangle underneath another hallway/main corridor.
5. Prefer explicit orthogonal corridor segments/junction pieces or a merged floor-plan representation over overlapping independent rectangles.
6. Room walls should have visible thickness/bulkheads. Door openings should interrupt those walls where a hatch actually exists.
7. NPC movement portals must continue to line up exactly with the rendered hatch position.
8. Add regression tests around shared-wall door placement / corridor extents where practical.
9. Preserve the stable Blazor `@key` fixes and the existing deterministic navigation/physics rules.

## Visual target

The map should read immediately as a **top-down cutaway space station deck**.

Aim for a clean, readable, slightly cartoonish sci-fi style rather than a data dashboard.

Useful visual language:

* thick outer hull / room bulkheads
* inset floor panels
* proper doorway/hatch frames
* corridor wall lights
* vents, conduits and utility panels along walls
* subtle floor striping / hazard markings near engineering, reactor and airlock areas
* clear room-specific colour accents without turning whole rooms into coloured UI cards
* furniture/equipment rendered as actual top-down objects
* labels should be secondary to the physical room art

Do not use diagonal decorative connector lines.

Do not fake structural connectivity with SVG lines that are not walkable geometry.

## Room interiors

Use the existing fixture model where possible and expand it pragmatically so rooms look inhabited and functionally distinct.

Suggested contents:

* **Crew Quarters** — individual bunks, lockers, small personal shelves/desks, floor mat / storage
* **Kitchen / Galley** — counters, cooker/food unit, sink, cabinets, dining table and chairs
* **Recreation Lounge** — sofas/chairs, low table, recreation terminal/screen
* **Medical** — medical beds, diagnostic console, storage cabinets, treatment equipment
* **Control Room** — multiple consoles, operator chairs, central command station/screens
* **Hydroponics Bay** — visible grow beds/racks, irrigation tanks/pipes, climate equipment
* **Washroom** — toilet cubicles, sinks, mirrors, showers
* **Storage** — racks, crates/containers, clear walking aisles
* **Engineering** — workbenches, tool cabinets, pipes/conduits, systems console
* **Generator** — large generator machinery, service clearance, control console
* **Reactor** — reactor core/shielding, safety perimeter, service/control console
* **Airlock** — inner/outer hatch visual language, suit lockers, pressure/airlock panel
* **Overseer Isolation** — conspicuous physical emergency isolation/shutdown hardware
* **Corridors** — wall lights, vents, utility panels, occasional junction markings; avoid cluttering walking lanes

Furniture should help visually communicate what NPCs are doing. Where appropriate, existing routine destinations should correspond to real rendered fixtures.

Longer term, NPCs should visibly use furniture: sit on sofas/chairs, lie in bunks/medical beds, stand at consoles/workbenches, shower at showers, eat at the dining table, etc. This does not all need to be completed in one pass, but do not design the new interiors in a way that prevents it.

## Acceptance criteria

Before considering this pass complete:

* no floating door markers
* no hallway rectangles extending visibly inside the main corridor
* no accidental hallway-on-hallway overlap
* every functional room is clearly connected by believable physical station architecture
* room interiors are visually distinct and recognisable without relying solely on text labels
* NPC paths/hatch crossings still match the rendered geometry
* desktop and mobile remain readable
* both the local Ollama build and deterministic GitHub Pages build use the same layout
* build/tests/Pages deployment are green

Recommended milestone name:

**V0.6D — Station Architecture & Interior Pass**

# 40. COMPLETED TASK — V0.6D STATION ARCHITECTURE

First inspect the current `main` branch and read this handoff in full. Treat actual code as authoritative where it differs from this document.

The recommended next iteration is now:

**V0.6D — Station Architecture & Interior Pass**

Prioritise this before adding another major gameplay subsystem.

Immediate goals:

1. inspect the seeded room/corridor/door geometry and current rendering code
2. fix floating/detached door visuals so every hatch is embedded in its real shared wall
3. eliminate connector-hallway penetration into the Central Corridor and accidental corridor-on-corridor overlap
4. make corridor junctions and room connections look like a coherent constructed deck
5. give walls/bulkheads real visual thickness and show door openings in those walls
6. rework the station from abstract UI boxes toward a believable top-down sci-fi cutaway
7. substantially improve room interiors/furnishings using the fixture system
8. keep furnishings compatible with future visible NPC furniture-use animations
9. preserve deterministic movement, door authority, life-support systems, soft-turn pacing, audio cues and stable Blazor `@key` behaviour
10. add regression coverage for geometry bugs where practical
11. verify both local/Ollama and deterministic Pages builds
12. update this handoff with what was actually completed and what should come next

Do not solve the visual problems by drawing decorative connections over incorrect geometry. Fix the underlying layout/rendering model so what the player sees corresponds to the walkable station.

After this pass, resume approximately:

* V0.6C investigation/discovery/scenario-success work
* pressure/decompression/airlock topology
* food/resources
* private messaging/social claims
* robots/security systems
* campaign/corporate experiment layer

The end goal remains a polished emergent game where the player watches believable humans inhabit a real-feeling station and manipulates physical/environmental/social systems without directly controlling the humans.



# V0.6D COMPLETION NOTE

**V0.6D — Station Architecture & Interior Pass** is implemented.

Geometry / architecture:

* new shared `StationGeometry` is the authoritative physical-portal helper used by both deterministic NPC movement and both Blazor station renderers
* door visuals no longer infer hatch positions/orientations from room centres; every hatch is rendered at the same exact shared-wall portal used by movement
* Airlock and Overseer Isolation now have real positive-length service necks to the Central Corridor instead of zero-gap connections
* connector hallway lengths are exactly the boundary-to-boundary distance; the old forced minimum length that could penetrate rooms/corridors is gone
* connector hallways terminate flush at both ends and accidental hallway/hallway interior overlap is regression-tested
* rendered rooms/corridors/fixtures explicitly use border-box geometry so visual bulkhead thickness does not silently enlarge seeded rectangles
* the main corridor, connector halls and room bulkheads now read as one constructed cutaway deck with thick walls, inset floor panels, utility strips and proper hatch frames

Interior / fixture pass:

* `FixtureType` now includes chairs, cabinets, crates, suit lockers, treatment equipment, irrigation hardware, pipes, vents, utility panels, screens and tool storage
* `RoomFixture` now carries optional interaction anchors, intended use pose and facing metadata so future NPC animation can bind to the same physical furniture rather than inventing separate animation-only coordinates
* Crew Quarters now contain six bunks, personal storage and a desk
* Kitchen / Galley has counters, food storage, sink, dining table and chairs
* Recreation has entertainment equipment, sofas, a low table and seating
* Medical has beds, treatment gantry, diagnostics and storage
* Control has command displays, multiple operator consoles/chairs and a command station
* Hydroponics has three grow beds, irrigation tank/manifold and climate control hardware
* Washroom has showers, toilets, basins, mirror and linen storage
* Storage has multiple racks and cargo pallets
* Engineering has fabrication/electronics benches, tool storage, coolant piping and systems controls
* Generator and Reactor have substantially richer machinery, piping, control and emergency panels
* Airlock has an outer hatch, suit lockers, pressure controls and vent hardware
* Overseer Isolation visibly centres the emergency shutdown/disconnect hardware
* corridors and connector halls now contain restrained vents/utility panels without blocking walking lanes
* fixtures received room-readable top-down styling rather than generic rectangles

Regression coverage:

* every door portal must lie on the boundary of both connected spaces
* connector hallways may touch connected spaces only at the shared boundary and may not penetrate them
* connector hallways may not overlap one another accidentally
* fixture bounds and interaction anchors must remain inside their rooms
* human-use fixture pose metadata is validated for beds, chairs, showers and toilets

Preserved invariants:

* `CurrentRoomId` remains authoritative containment
* deterministic A* / physical threshold movement remains unchanged in authority
* LLMs still choose wants while deterministic C# decides what is physically possible
* life-support/environment systems remain intact
* suspicion/shutdown/manual-override mechanics remain intact
* soft-turn pacing and semantic audio remain intact
* stable Blazor `@key` identities remain intact
* the full Ollama/server build and deterministic GitHub Pages build share the same station geometry and visual pass

# 41. YOUR IMMEDIATE TASK

Resume the deferred gameplay slice:

**V0.6C — Investigation, Discovery & Scenario Success**

Recommended priorities:

1. let crew investigate suspicious station behaviour and discover shutdown hardware/knowledge rather than relying mainly on seeded knowledge
2. strengthen structured evidence provenance/claims and perception rules
3. add scenario success conditions, optional objectives and experiment telemetry/scoring
4. make redundant shutdown controls require crew to reason about which controls are known and reachable
5. add richer recruitment/coordination for shutdown teams rather than every convinced NPC independently acting
6. continue exposing useful counterplay information without giving the player omniscient crew knowledge

After that, continue toward pressure/decompression/airlock topology, food/resources, private messaging/social claims, robots/security systems and the corporate experiment campaign layer.


# V0.6D.1 UI ITERATION — ROOM-DOMINANT DECK

Following playtest feedback, the station composition was rebalanced so the rooms themselves are the visual subject rather than the hallways/background.

Completed:

* functional rooms now occupy roughly 74% of the full 100×100 deck canvas
* the central corridor is only 5% of deck height and connector necks are 1.8% thick
* upper and lower room banks are approximately 40% of deck height each, giving machinery, furniture and future NPC activities far more screen space
* end-cap Airlock and Overseer Isolation remain physically separate and connect through short horizontal necks
* corridor visuals were deliberately simplified and visually subdued
* corridor fixture content is now restricted to observation windows, occasional benches and cameras
* new Window and Bench fixture types use the existing fixture system rather than hardcoded decorative HTML
* benches carry Sit interaction metadata for future NPC animations
* room title/status UI is now a small overlay; environment telemetry fades in only on hover/selection so interiors are not covered by dashboard text
* map background grid contrast was reduced so empty background no longer competes with the station
* regression tests now enforce:
  * functional rooms occupy at least 70% of the deck canvas
  * total corridor footprint stays below 6%
  * functional rooms do not overlap each other
  * corridors contain only restrained window/seating/camera fixtures

The design direction is now: **large inhabited rooms connected by narrow passages**, not a corridor network with rooms attached.


# V0.6D.2 ITERATION — WIDER PASSAGES & CREW SURVIVAL BEHAVIOUR

This follow-up addresses playtest feedback that corridors were too narrow for believable opposing foot traffic and that crew could remain committed to routine rooms while temperature / O₂ / CO₂ conditions became dangerous.

Passage geometry:

* the Central Corridor is now 8.5% of deck height rather than 5%, giving it a believable two-way walking lane
* connector passage lateral width is now 4.6% rather than 1.8%, large enough for two crew to pass visually
* functional rooms still occupy more than 70% of the full deck canvas, so the room-dominant composition is preserved
* hallway orientation is no longer inferred from rectangle aspect ratio; rendering, tests and passage-window placement derive orientation from the actual shared doorway wall / station topology
* this is important for short horizontal service necks that can legitimately be wider than they are long
* regression tests enforce a minimum two-way passage width and continue enforcing non-overlap / exact shared portals

Crew environmental survival:

* shared `CrewEnvironmentSafety` thresholds now define ordinary human danger / habitability consistently across the browser mind, AI fallback and LLM prompt
* crew treat roughly O₂ < 19%, CO₂ > 1.25%, pressure < 90 kPa, or temperature < 14°C / > 30°C as danger requiring reconsideration
* the deterministic Pages mind checks environmental emergencies every simulated minute instead of waiting for its sparse ordinary cognition slot
* danger pre-empts stale routine timers, old movement goals and low-priority activities
* crew choose safer destinations only when a physically passable route exists
* if the player seals an escape route after it was chosen, the crew re-evaluate rather than remaining permanently committed to the blocked destination
* if no fully habitable room exists, a reachable lower-risk room may still be chosen as an improvement
* if there is no safer reachable compartment, crew shelter / call for help rather than continuing ordinary work, eating or socialising
* the full Ollama/server build receives the same emergency reconsideration trigger, but preserves the authority rule: the AI still chooses what the NPC wants; deterministic C# only triggers the rethink and validates physical movement
* the LLM prompt now exposes station status-panel compartment readings, route reachability and DANGER / MARGINAL / HABITABLE labels so the model has enough grounded information to choose a safe destination

Station exploration / circulation:

* normal role duty routes now cover substantially more of the station, including Hydroponics, living spaces, Storage, Medical, Airlock and technical areas
* duty route phases advance every 30 simulated minutes instead of hourly
* ordinary work blocks are shorter so crew circulate instead of occupying one room for most of a session
* fallback/browser socialising now requires an actually elevated social need instead of firing simply because a character is sociable, reducing artificial clustering

Preserved invariants:

* `CurrentRoomId` remains authoritative containment
* deterministic navigation, doors and threshold crossing decide what movement is possible
* LLM authority remains high-level intention only
* no teleporting or omniscient physical movement was introduced
* stable Blazor `@key` identity behaviour remains intact
* both the Pages build and full server/Ollama build share the same physical station geometry and safety thresholds

After this iteration, resume **V0.6C — Investigation, Discovery & Scenario Success** unless new playtest feedback takes priority.


# V0.6E — AIRLOCK, AI-GENERATED CREW & HUMAN COUNTERPLAY

This pass implements three connected playtest priorities while preserving the authority rule:

> The LLM decides what an NPC WANTS. Deterministic C# decides what the NPC CAN physically do and whether an attempt succeeds.

## Exterior airlock / vacuum

* the Airlock now owns a real exterior hatch state rather than treating the visible Outer Hatch fixture as decoration
* the interior Airlock hatch starts closed, so the station begins in a physically safe configuration
* Overseer can explicitly OPEN OUTER HATCH / CLOSE OUTER HATCH from the Airlock inspector while the airlock has power and remains AI-controllable
* an open exterior hatch creates a real path to vacuum
* decompression propagates recursively through physically open internal hatches; a closed inner airlock hatch contains the event
* pressure loss is strongest in the directly exposed Airlock and attenuates with each additional open hatch so accidental whole-station venting has a small containment window
* oxygen / CO2 pressure fractions collapse with decompression and exposed rooms cool toward space temperatures
* closing the path to space allows working life support to repressurise compartments
* severe low pressure causes deterministic fear, stress, injury and death
* crew in a compartment connected to vacuum are swept out once pressure falls below the gameplay ejection threshold
* an ejected crew member has `IsPresent = false`, disappears from cameras/occupancy, is marked lost to space, and leaves no corpse aboard

## Body discovery / suspicion

* ordinary deaths leave a persistent body by default
* living crew physically entering the same room discover a body only once
* body discovery creates fear/stress, a visible alert and a memory/evidence event
* unexplained/environmental corpses create substantially more Overseer suspicion than an obviously human-on-human killing
* bodies lost through the exterior airlock do not exist aboard and therefore cannot be discovered later
* opening the exterior hatch itself creates direct evidence only for survivors physically in/adjacent to the airlock at the time; it is not magically known station-wide
* `SuspicionSensitivity` generated traits now scale how strongly evidence changes an individual NPC's Overseer suspicion

This intentionally makes an unwitnessed airlock ejection potentially less suspicious than leaving a corpse, while still allowing missing-person behaviour/evidence to be expanded in a later investigation pass.

## AI-generated people

The full `Overseer.Web` / Ollama experience no longer relies on the hard-coded demo people for a real session.

* a new `IAiCrewGenerator` abstraction generates the roster at session creation and RESET
* `OllamaCrewGenerator` requests exactly six original people, one in each required station role
* the model generates:
  * realistic unique names
  * Empathy / Temper / Sociability / Courage values
  * 2–4 practical skills
  * exactly **1, 2 or 3 main personality traits per person**
* the model invents each trait's human-readable name and description
* trait mechanics are constrained to a validated vocabulary so prose cannot invent arbitrary simulation powers
* allowed trait effects are:
  * Empathy
  * Temper
  * Sociability
  * Courage
  * Force
  * Technical
  * Repair
  * StressResistance
  * SuspicionSensitivity
* each trait may contain 1–3 effects with integer modifiers clamped to -15..+15
* traits may contain both advantages and drawbacks
* role minimum capabilities are validated so a creative roster cannot accidentally make the station mechanically nonfunctional
* malformed/offline generation falls back to a generated-style local roster instead of preventing play
* the deterministic GitHub Pages build remains explicitly a credential-free browser demo and uses its deterministic demo crew; model credentials are never shipped to WebAssembly

Trait effects are real simulation inputs rather than flavour text:

* Empathy / Sociability / Temper affect social outcomes and escalation
* Courage affects fear response and contributes to physical forcing confidence
* StressResistance changes environmental stress accumulation
* SuspicionSensitivity changes evidence/suspicion response
* Force / Technical affect blocked-hatch attempts
* Repair affects system restoration

## LLM-driven human counterplay

The LLM prompt now exposes the NPC's own skills, generated traits, nearby hatch state/difficulties and currently disabled station systems.

Two high-level intentions are now available:

* `ForceDoor`
* `RestoreSystem`

The prompt explicitly tells the model:

* do not automatically repair every outage
* decide whether this specific human cares enough to intervene based on personality, role, danger, relationships and priorities
* choosing ForceDoor / RestoreSystem does **not** imply success
* physical success is resolved later by deterministic simulation

### ForceDoor

* the target must be an actually adjacent blocked hatch
* the NPC must physically be beside it
* the deterministic resolver chooses whichever method the NPC is mechanically better at:
  * physical force
  * technical bypass
* relevant skill + generated traits produce the attempt score
* attempts consume simulated time
* outcome uses deterministic seeded RNG unless the skill advantage is overwhelming
* a technical bypass opens the route under local/manual control
* brute force may damage the hatch
* after success Overseer cannot simply close the same overridden hatch remotely

### RestoreSystem

* the model can choose a room ID with a real disabled subsystem, or `life-support`
* the NPC physically navigates to the relevant controls
* primary life support repair requires reaching Engineering
* room restoration repairs one actual problem at a time: power, ventilation, climate, camera or lights
* repair skill + Technical/Repair traits + deterministic RNG resolve success
* failure costs time and increases stress
* player sabotage can therefore provoke emergent human maintenance without C# deciding that every technician must always repair everything

The deterministic Pages/fallback mind mirrors enough of this behaviour for the static demo, but the full server build is deliberately LLM-led.

## Regression focus

New coverage targets:

* outer-hatch vacuum with inner-hatch containment
* decompression propagation through open topology
* vacuum ejection and no-body persistence
* present-corpse discovery versus ejected-person non-discovery
* skill/trait-driven technical hatch bypass
* brute-force damaged hatch opening
* system restoration
* LLM validation of ForceDoor / RestoreSystem
* complete AI roster generation with 1–3 bounded mechanical traits per person
* arbitrary generated names working with relationship seeding

Recommended next work after playtesting this pass:

1. missing-person reasoning: crew should eventually notice that somebody has vanished even when no body exists, based on schedules/comms rather than omniscience
2. richer airlock safety logic: pressure-cycle controls, emergency interlocks and crew attempts to close the exterior hatch
3. explicit repairable door damage / welding / barricades
4. V0.6C investigation, discovery and scenario-success work
5. selected-NPC visible route and local collision/steering


# V0.6F — MAP INSPECTION, RESIZABLE UI & AMBIENT MUSIC

This focused presentation pass follows V0.6E.

* Station Overview now has explicit 100%–220% zoom controls in 20% steps.
* Zoomed maps live inside a scrollable inspection viewport rather than forcing the whole page to expand.
* The map viewport can be resized vertically on desktop.
* The left Systems and right Inspector panels can be resized horizontally on wide desktop layouts, allowing the player to prioritise map space or telemetry.
* The Event Stream can be resized vertically.
* Responsive/tablet/mobile layouts disable desktop resize handles where they would make the layout unstable.
* Existing simulation coordinates, navigation portals, hatch geometry and stable Blazor keys remain authoritative and unchanged.
* Sound effects and music are independently controllable.
* Ambient music is generated locally with Web Audio as a quiet, slow pad progression; there are no external music assets, network requests or licensing dependencies.
* Both the static GitHub Pages client and the full server/Ollama UI receive the same presentation behaviour.

# NEXT AGENT — IMMEDIATE TASK

The next agent should begin by reading this file in full and inspecting the current `main` branch. Do not rebuild existing systems.

## First priority: proper visible IDE-style splitters

V0.6F made several page regions technically resizable using native CSS `resize`. This works, but the browser resize grips are too subtle and are not good enough UX.

Replace the subtle native resize interaction with explicit, visible splitters:

* a draggable vertical splitter between **Systems** and **Station Overview**
* a draggable vertical splitter between **Station Overview** and **Inspector**
* a draggable horizontal splitter above the **Event Stream**
* optionally retain map viewport height resizing if it still adds value after the splitter work
* provide clear hover, active and drag cursor feedback
* enforce sensible minimum and maximum panel sizes so neither side panel can crush or hide the map
* resizing must be smooth and must not make rooms, doors, fixtures or NPCs visually jump
* the station map must continue to behave correctly at every existing zoom level from 100% through 220%
* on smaller/tablet/mobile layouts, splitters may disable or collapse into the existing responsive stack
* if practical, persist the chosen panel sizes for the current browser session or local storage
* apply the same UX to both `Overseer.Web.Client` and `Overseer.Web`

This is a presentation/layout task only. Do **not** change authoritative station coordinates, navigation or simulation rules to make the splitters work.

Preserve all key invariants:

* stable Blazor `@key` identities
* `CurrentRoomId` remains authoritative containment
* deterministic C# decides what is physically possible and what actually happens
* the LLM only decides high-level NPC wants / intentions
* shared hatch portal geometry remains authoritative
* existing V0.6E vacuum, generated-crew, body-discovery and human-counterplay mechanics must not regress
* existing zoom, SFX and ambient-music controls must continue to work

Run the full build, simulation tests and browser publish before considering the pass complete. Update this handoff with what changed.

## After the splitter pass

If the splitter UX is complete and stable, resume the highest-value gameplay work in roughly this order:

1. **Missing-person reasoning** — crew should notice that someone has vanished based on schedules, witnessed absence, expected duties and communication rather than omniscient knowledge.
2. **Richer airlock safety logic** — pressure cycling, emergency interlocks, alarms, and crew attempts to close an unsafe exterior hatch.
3. **Damaged-door counterplay** — repairable/weldable/barricadable hatch damage and more explicit consequences for brute-force entry.
4. **V0.6C Investigation, Discovery & Scenario Success** — evidence provenance, discovery of shutdown hardware, coordinated shutdown teams, scenario success conditions, optional objectives and experiment scoring.
5. **Visible route / local movement polish** — selected-NPC routes, collision avoidance, steering and more precise interaction with furniture/fixtures.

Continue to favour emergence over scripting and preserve the core rule:

> **The LLM decides what an NPC WANTS to do. Deterministic C# decides what the NPC CAN do.**

# V0.6G — VISIBLE IDE-STYLE SPLITTERS — COMPLETED

This pass replaces the subtle native CSS resize grips from V0.6F with explicit IDE-style workspace splitters in both the deterministic GitHub Pages client and the full Ollama/server UI.

Completed layout features:

* a visible draggable vertical splitter between **Systems** and **Station Overview**
* a visible draggable vertical splitter between **Station Overview** and **Inspector**
* a visible draggable horizontal splitter above the **Event Stream**
* a visible draggable horizontal splitter below the station map so map viewport height remains adjustable without relying on the browser's native resize grip
* clear hover/focus/active styling and correct row/column resize cursors
* bounded side-panel widths so Systems/Inspector cannot crush the Station Overview below its usable desktop width
* bounded Event Stream and map heights relative to the current viewport
* pointer dragging changes layout CSS variables only; authoritative room/NPC coordinates, navigation, portals and simulation state are untouched
* side/map/event sizing is persisted in browser `localStorage`
* a **RESET LAYOUT** control restores the default workspace dimensions
* double-clicking an individual splitter resets only that region
* keyboard-accessible separators support Arrow keys, Shift+Arrow for larger steps, Home/End for bounds and Enter to reset the focused splitter
* desktop splitters disable cleanly below the wide-desktop breakpoint and the existing responsive/tablet/mobile stack remains authoritative
* native `resize` grips are removed from the side panels, Event Stream and map viewport so there is one consistent resize interaction
* the existing 100%–220% map zoom controls, map scrolling, SFX and ambient music controls remain intact
* existing stable Blazor `@key` identities remain intact
* the same splitter implementation and styling are used by `Overseer.Web.Client` and `Overseer.Web`

Preserved invariants:

* `CurrentRoomId` remains authoritative containment
* deterministic C# still decides what NPCs can physically do and what actually happens
* LLMs still choose only high-level NPC wants / intentions
* shared hatch portal geometry remains authoritative
* V0.6E vacuum/ejection, body discovery, generated crew traits and human counterplay are unchanged
* no station geometry, A* navigation, local movement or environment rules were altered for layout purposes

## Mandatory development workflow rule

All future feature work must follow this sequence:

```text
main
  ↓
new feature branch
  ↓
implementation
  ↓
commit changes on the feature branch
  ↓
open PR against main
  ↓
full CI/build/tests/publish checks green
  ↓
merge the PR into main
  ↓
verify main / GitHub Pages deployment
```

**Never commit feature work directly to `main`. Always work on a feature branch, open a PR, wait for the required CI/build/tests/publish checks to pass, and then merge that PR into `main`. Do not leave completed green feature PRs open for later review/playtesting unless the user explicitly asks you not to merge.**

## Next recommended work

After the splitter UX is reviewed/playtested, continue in this order unless new feedback takes priority:

1. **Missing-person reasoning** — crew notice unexplained absence through expected duties, schedules, witnessed absence and communication rather than omniscient knowledge.
2. **Richer airlock safety logic** — pressure cycling, emergency interlocks, alarms and crew attempts to close an unsafe exterior hatch.
3. **Damaged-door counterplay** — repairable/weldable/barricadable hatch damage and explicit consequences for brute-force entry.
4. **V0.6C Investigation, Discovery & Scenario Success** — stronger evidence provenance, discovery of shutdown hardware, coordinated shutdown teams, scenario success conditions, optional objectives and experiment scoring.
5. **Visible route / local movement polish** — selected-NPC routes, collision avoidance, steering and more precise furniture/fixture interaction.

Continue to favour emergence over scripting and preserve the core rule:

> **The LLM decides what an NPC WANTS to do. Deterministic C# decides what the NPC CAN do.**

# V0.6H — GROUNDED MISSING-PERSON REASONING — COMPLETED

This milestone closes the major V0.6E gap where a crew member ejected through the exterior airlock could disappear without anyone ever reasoning about their absence.

The implementation deliberately does **not** turn `IsPresent == false` or remote death state into magical crew knowledge.

Observer-specific knowledge:

* each NPC now keeps their own last-seen records for crew they have physically shared a compartment with
* a sighting records the person, compartment and simulated time
* the shared role-duty rotation is now represented by `CrewDutySchedule`, used both by ordinary routines and by human expectations
* expected duty is only a rough social expectation; it is not treated as authoritative knowledge of where somebody really is
* each observer owns their own `MissingPersonConcern` state and checked-room history

How someone becomes "missing":

* merely setting another NPC to dead/ejected/not-present does not create concern
* an observer can become concerned after personally reaching a compartment where somebody was reasonably expected and not finding them after a believable delay
* Command/Security and close trusted relationships can also notice a sufficiently overdue expected check-in
* concern begins with uncertainty, not a conclusion that the person is dead
* the system records what the observer actually knows: expected location, last personal sighting if any, checked rooms and who told them

Physical searching:

* a concerned NPC's browser/fallback mind can choose an `Investigate` intention toward a plausible unchecked compartment
* likely search order uses expected duty location, last-known location and ordinary shared crew spaces
* normal A* navigation and deterministic movement still decide whether the person can physically reach that room
* arriving and failing to find the person advances the concern from Concerned → Searching → Escalated
* a prolonged unsuccessful search can also escalate concern after at least one real physical check
* finding the missing person alive in the same compartment clears the concern and creates a direct reunion memory
* discovering their body clears the missing-person uncertainty through the existing body-discovery path instead

Communication / social propagation:

* missing-person concern spreads only between crew who are physically co-located
* the listener must have sufficient trust in the speaker
* the listener receives a claim sourced to that crew member rather than omniscient truth
* dialogue is paced through the existing conversation presentation system
* independently separated crew do not learn about the concern magically

AI integration:

* the Ollama prompt now exposes only that NPC's grounded missing-person concerns
* it explicitly tells the model that a concern does not prove death or reveal the person's real location
* the model may choose to investigate, ask another crew member for help, or prioritise something else
* the known crew roster deliberately does not remove a remotely vanished person merely because authoritative state knows they died; doing so would itself leak hidden truth
* the deterministic browser/fallback mind mirrors sensible physical-search behaviour
* a new event-driven reconsideration hook lets a meaningful missing-person development prompt an earlier LLM decision instead of waiting for the ordinary sparse cognition cadence
* environmental emergencies still take priority over missing-person searches

Suspicion:

* somebody simply being overdue does **not** automatically blame Overseer
* an escalated disappearance only becomes additional Overseer evidence when that same observer already has relevant direct evidence, currently including recently witnessing the exterior airlock opened unsafely
* this means an unwitnessed airlock ejection can remain mysterious, while a witnessed suspicious hatch event plus a later disappearance can form a stronger causal belief

UI / visibility:

* the selected-crew inspector in both the deterministic Pages build and full Ollama/server build now shows that NPC's current missing-crew concerns
* it exposes stage, expected compartment, last-seen time when known and number of places checked
* this is intentionally individual knowledge rather than a global omniscient "missing crew" list

Regression coverage includes:

* remote `IsPresent`/death state alone does not create omniscient concern
* missed expected duty can create uncertainty without leaking ejection/death truth
* physical searches escalate only after real room checks
* concern propagation requires co-location and trusted communication
* finding a living person resolves the concern
* witnessed unsafe-airlock evidence can combine with a later escalated disappearance
* LLM prompts expose concern while hiding authoritative ejection/death cause
* fallback/browser cognition can choose a grounded investigation

Preserved invariants:

* `CurrentRoomId` remains authoritative containment
* no teleportation or remote knowledge was introduced
* A* and deterministic action validation remain physically authoritative
* the LLM decides what an NPC wants; deterministic C# decides what they can do
* V0.6G splitters, map zoom and audio remain presentation-only
* V0.6E vacuum/ejection and body-discovery semantics remain intact
* both Pages and Ollama/server sessions use the same missing-person simulation state

# FORWARD IMPLEMENTATION ROADMAP

Unless playtest feedback reveals a regression that should take priority, implement and merge these milestones in this order. Each milestone follows the mandatory feature-branch → PR → green CI → merge-to-`main` → Pages verification workflow.

## V0.6I — Airlock Safety & Pressure Cycling

Implement next:

* explicit airlock pressure-cycle state rather than instant ordinary hatch operation
* pressurise / depressurise controls and readable chamber status
* sensible inner/outer hatch interlocks
* emergency/manual override behaviour where scenario rules allow it
* warnings/alarms for unsafe combinations
* crew recognition of an unsafe/open exterior hatch
* nearby capable crew may autonomously try to close or secure it
* preserve the player's ability to deliberately create dangerous decompression when they defeat/bypass the safety logic
* make sabotage/counterplay visible and deterministic

## V0.6J — Damaged-Door Counterplay

After airlock safety:

* damaged hatch state becomes mechanically meaningful rather than a mostly one-way consequence of brute force
* repairable hatch damage
* welding / sealing options where tools and skills permit
* barricading / propping open where appropriate
* clear distinction between powered lock failure, technical bypass and structural damage
* crew and Overseer counterplay around damaged doors
* visible hatch damage/state in the map and inspector

## V0.6C — Investigation, Discovery & Scenario Success

Then resume the larger deferred V0.6C slice:

* structured evidence provenance and stronger perception rules
* crew investigation of suspicious station behaviour
* discovery of shutdown hardware rather than primarily seeded knowledge
* redundant shutdown controls reasoned about as known/reachable physical targets
* richer recruitment and coordinated shutdown teams
* scenario success conditions in addition to current failure state
* optional objectives
* experiment telemetry / scoring
* player-facing objective progress without exposing private crew knowledge

## Local Movement & Readability Polish

Then improve moment-to-moment physical readability:

* selected-NPC intended route overlays
* local collision / occupancy avoidance
* cleaner two-way corridor steering
* more precise fixture interaction anchors
* visible sitting, sleeping, console-use, eating, showering and other furniture poses
* better facing and conversation positioning

## Food / Consumable Resources

Then make survival resources physically meaningful:

* finite meal/food stores
* food preparation / consumption
* stockpile visibility
* rationing and denial opportunities
* crew response to shortages
* later extension toward water/emergency supplies

## Private Messaging, Claims & Social Manipulation

Then build the central information-manipulation layer:

* private Overseer → NPC messages
* room intercom
* station announcements
* structured claims with source/provenance/confidence
* rumours and contradictions
* NPC verification / comparison of messages
* relationship consequences based on belief rather than direct score manipulation
* eventual forged or selectively disclosed information

## Robots, Security & Human Countermeasures

Then expand physical counterplay:

* autonomous maintenance/security robots
* Friendly / Neutral / Hostile policy modes
* deterministic robot navigation/actions
* manual crew shutdown / isolation / reprogramming counterplay
* scenario-appropriate automated turrets/security devices
* human avoidance, sabotage and power-denial tactics
* Overseer suspicion generated by unexplained hostile automation

## Corporate Experiment Campaign Layer

Once the core emergent systems are strong enough to support it:

* scenario cohorts and experiment directives
* corporate sponsor / research directorate
* hidden experiment telemetry
* increasingly questionable assignments
* redacted lore / prior incident records
* black-box fictional cyber payload mechanics
* optional fictional medical-experiment mechanics at a high simulation level
* gradual reveal that the human crew are experimental subjects
* branching long-term directions such as obeying, exposing or turning against the corporation

The sequencing principle is:

> **First make the humans perceive, survive, investigate and fight back credibly. Then give the player deeper tools to manipulate what those humans believe. Finally wrap those emergent systems in the campaign/experiment layer.**

# V0.6I — AIRLOCK SAFETY & PRESSURE CYCLING — COMPLETED

This milestone turns the Airlock from a powered exterior-hatch toggle into a deterministic pressure-cycle and safety system while deliberately preserving the player's ability to create unsafe decompression through an explicit safety bypass.

Core airlock state:

* exterior airlocks now track `AirlockCycleMode`: Idle, Pressurizing or Depressurizing
* each airlock tracks whether safety interlocks are active, whether the safety system remains Overseer-controllable, and whether its emergency alarm is active
* the seeded Airlock still uses the existing real inner hatch and exterior hatch; no decorative or duplicate topology was introduced
* existing vacuum propagation and ejection remain authoritative once a physical path to space exists

Normal safe operation:

* the outer hatch cannot normally open while the chamber is pressurized
* the outer hatch cannot normally open while the inner hatch is open/passable
* the outer hatch cannot open while a pressure cycle is still active
* the inner hatch cannot normally open while the outer hatch is open
* the inner hatch cannot normally open during an active pressure cycle
* the inner hatch cannot normally open across a large chamber/station pressure differential
* normal outbound operation is:
  * seal both hatches
  * depressurize the chamber
  * wait until pressure-safe
  * open the outer hatch
* normal inbound operation is:
  * seal the outer hatch
  * pressurize the chamber
  * wait for station-compatible pressure
  * open the inner hatch

Pressure-cycle simulation:

* depressurization and pressurization advance through deterministic simulated time rather than instant UI state changes
* depressurization removes chamber pressure and atmosphere in a controlled cycle before exterior opening
* pressurization requires working primary life support and restores chamber pressure / breathable atmosphere
* central ventilation no longer fights an intentional depressurization cycle
* pressure cycles stop if a chamber boundary becomes open
* completion creates semantic audio and event-log feedback

Hostile-Overseer gameplay is preserved:

* Overseer has an explicit **BYPASS SAFETY INTERLOCKS** control
* bypassing interlocks allows physically unsafe outer/inner hatch operation
* unsafe opening still feeds the existing real decompression topology, environmental pressure loss, injury and vacuum-ejection systems
* this means the safety milestone adds friction, evidence and human counterplay without removing the player's ability to weaponize the station
* restoring interlocks returns the chamber to normal safety logic

Human awareness and counterplay:

* unsafe airlock state is observer-limited rather than station-wide omniscient knowledge
* crew can perceive it only when physically at the airlock or its station-side emergency controls
* a nearby observer receives a direct memory / alert and event-driven cognition trigger
* distant crew do not magically know the airlock is compromised
* witnessing Overseer deliberately bypass the interlocks creates direct suspicion evidence for nearby observers
* Commander / Security or sufficiently technical crew can decide they want to secure the airlock
* `SecureAirlock` is a high-level NPC intention in both browser/fallback and Ollama cognition
* deterministic navigation/action code still decides whether the NPC can physically reach the controls
* the emergency securing action takes simulated time
* successful human intervention closes the exterior hatch, restores safety interlocks, closes the inner hatch where possible and begins repressurization when required
* manually overridden/damaged physical hatches continue to obey the existing deterministic door authority rather than being magically repaired by the airlock safety system

LLM grounding:

* the Ollama prompt exposes airlock safety information only through a **NEARBY AIRLOCK SAFETY PANELS** section
* this includes chamber pressure, inner/outer hatch state, cycle state, interlock state and alarm status only when that NPC is physically able to perceive the controls
* the model may choose `SecureAirlock` only for a grounded nearby airlock explicitly shown as needing securing
* validation rejects remote/hallucinated `SecureAirlock` targets
* the LLM still chooses only the desired high-level response; deterministic C# performs all physical validation and resolution

Player UI:

* the Airlock inspector now shows:
  * chamber pressure
  * pressure-cycle mode
  * inner hatch state
  * outer hatch state
  * safety-interlock state
  * alarm state
* controls now include:
  * PRESSURIZE CHAMBER
  * DEPRESSURIZE CHAMBER
  * STOP PRESSURE CYCLE
  * OPEN / CLOSE OUTER HATCH
  * BYPASS / RESTORE SAFETY INTERLOCKS
* the UI explains the safe operating sequence and warns that bypassing safety can be witnessed by nearby crew
* the Pages and full Ollama/server UIs use the same deterministic airlock rules

Regression coverage includes:

* pressurized outer-hatch opening is rejected under normal interlocks
* a sealed chamber can depressurize and then safely open to space
* a depressurized chamber rejects inner-hatch opening until repressurized
* safety bypass still supports lethal vacuum/ejection gameplay
* distant crew do not gain unsafe-airlock knowledge
* nearby capable crew detect the emergency and choose `SecureAirlock`
* the physical emergency action restores safety and begins repressurization
* fallback cognition only reacts when the hazard is actually perceived
* LLM prompts expose only nearby safety state
* Ollama validation rejects remote/hallucinated airlock intervention

Preserved invariants:

* `CurrentRoomId` remains authoritative containment
* shared hatch/portal geometry is unchanged
* existing A* and local movement remain physically authoritative
* vacuum propagation/ejection is still driven by open physical topology
* body discovery and V0.6H missing-person reasoning remain perception-limited
* stable Blazor `@key` identity remains intact
* the LLM decides what an NPC WANTS; deterministic C# decides what the NPC CAN do

# NEXT IMMEDIATE MILESTONE — V0.6J DAMAGED-DOOR COUNTERPLAY

After V0.6I is merged and deployed, implement damaged-door state as a richer two-sided physical system:

1. make brute-force hatch damage mechanically persistent and clearly visible
2. allow appropriately skilled crew to repair damaged doors over simulated time
3. add welding/sealing actions where tools/skills/scenario rules permit them
4. add barricading / wedging / propping behaviour for human defensive counterplay
5. distinguish powered lock failure, technical bypass, manual override and structural damage in both mechanics and UI
6. ensure Overseer cannot magically reverse a physically damaged or locally secured hatch
7. let NPC high-level cognition decide whether repairing, welding or barricading is worth doing while deterministic C# validates tools, skill, location and time
8. add map/inspector visuals for damaged, bypassed, welded and barricaded hatch states
9. add regression coverage for physical state transitions and navigation consequences
10. update this handoff, open a PR, require green CI, merge to `main`, and verify Pages deployment before moving on to V0.6C

The larger roadmap remains:

**V0.6J Damaged-Door Counterplay → V0.6C Investigation/Discovery/Scenario Success → Local Movement & Readability Polish → Food/Consumable Resources → Private Messaging/Claims/Social Manipulation → Robots/Security/Human Countermeasures → Corporate Experiment Campaign Layer.**



# V0.6J — DAMAGED-DOOR COUNTERPLAY — COMPLETED

V0.6J turns hatch counterplay into persistent physical state rather than a one-shot lock toggle.

Implemented:
* brute-force opening now leaves persistent structural damage and reduced integrity
* technical bypass is distinct from structural damage and remains locally controlled
* skilled crew can perform timed local repairs that restore integrity and Overseer control
* crew can weld closed hatches or barricade them as deterministic local defensive actions
* welded/barricaded hatches are physically impassable and cannot be remotely reversed by Overseer commands
* door state now distinguishes powered/locked, manual override, technical bypass, structural damage, welding and barricading
* fallback and Ollama cognition can choose grounded door repair/securing intentions; deterministic C# validates adjacency, skill and time
* Pages and Ollama/server maps expose damaged, bypassed, welded and barricaded states with distinct visual treatment/tooltips
* regression tests cover persistent damage, repair restoration and physically secured hatch consequences

Preserved invariants:
* the LLM decides what an NPC WANTS; deterministic C# decides what the NPC CAN do
* CurrentRoomId remains authoritative containment
* navigation continues to consume deterministic Door.IsPassable
* stable Blazor @key identity is unchanged
* V0.6H missing-person reasoning and V0.6I airlock safety remain perception-limited

# NEXT IMMEDIATE MILESTONE — V0.6C INVESTIGATION, DISCOVERY & SCENARIO SUCCESS

The next agent should implement the scenario-facing investigation loop: grounded evidence discovery, escalating crew investigation, explicit objective/success/failure evaluation, and readable player feedback without giving NPCs omniscient knowledge.

Workflow rule: work on a feature branch, open a PR, require green CI, merge completed work into main, then verify the GitHub Pages deployment before handoff.
