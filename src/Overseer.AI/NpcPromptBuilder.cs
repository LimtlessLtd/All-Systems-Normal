using System.Text;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.AI;

public static class NpcPromptBuilder
{
    /// <summary>What "nominal" means in STATION STATUS-PANEL ROOM READINGS.</summary>
    public const string NominalLegend =
        "\"nominal\" means O2 at least 19.5%, CO2 under 0.5%, pressure at least 95 kPa, 18-26C, no fire or smoke, full visibility, ventilation on and HABITABLE; any other room shows its full readings.";

    private static bool IsNominal(Room room) =>
        room.OxygenPercent >= 19.5
        && room.CarbonDioxidePercent < 0.5
        && room.PressureKpa >= 95
        && room.TemperatureC is >= 18 and <= 26
        && room.FireIntensity <= 0
        && room.SmokePercent < 0.5
        && room.VisibilityPercent >= 99.5
        && room.VentilationEnabled
        && CrewEnvironmentSafety.Label(room) == "HABITABLE";

    private static readonly TimeSpan RecentFailedAttemptWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan PanicClaimWindow = TimeSpan.FromMinutes(15);

    public static string Build(Npc npc, GameState state)
    {
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        var occupantNpcs = state.Crew
            .Where(other => other.IsAlive
                && other.IsPresent
                && other.Id != npc.Id
                && other.CurrentRoomId == npc.CurrentRoomId)
            .ToArray();
        // Owner idea #17: visible conflict is perceivable, so bystanders can
        // decide how to respond; only what this observer can actually make out.
        var visibleConflicts = occupantNpcs
            .Where(other => other.CurrentAction.Kind is ActionKind.Attack or ActionKind.Argue
                && other.CurrentAction.TargetId is not null
                && PerceptionSystem.CanMakeOut(state, npc, other))
            .ToDictionary(
                other => other.Id,
                other => other.CurrentAction.Kind == ActionKind.Attack
                    ? $"attacking {(other.CurrentAction.TargetId == npc.Name ? "you" : other.CurrentAction.TargetId)}"
                    : $"arguing with {(other.CurrentAction.TargetId == npc.Name ? "you" : other.CurrentAction.TargetId)}");
        var occupants = occupantNpcs
            .Select(other => visibleConflicts.TryGetValue(other.Id, out var conflict)
                ? $"{other.Name} ({other.Role}, {conflict})"
                : $"{other.Name} ({other.Role})")
            .ToArray();
        var fightHere = occupantNpcs.Any(other =>
            other.CurrentAction.Kind == ActionKind.Attack
            && visibleConflicts.ContainsKey(other.Id)
            && other.CurrentAction.TargetId != npc.Name);

        var connectedDoors = state.Facility.Doors
            .Where(door => door.RoomAId == room.Id || door.RoomBId == room.Id)
            .Select(door =>
            {
                var otherId = door.RoomAId == room.Id ? door.RoomBId : door.RoomAId;
                var other = state.Facility.Rooms[otherId];
                var doorState = !door.IsPowered ? "unpowered"
                    : door.IsLocked ? "locked"
                    : door.IsOpen ? "open"
                    : "closed";
                var normalCrewAccess = CrewDoorInteractionSystem.CanOpenForTraversal(state, npc, door)
                    ? " | normal crew passage available"
                    : "";
                var lockAuthority = CrewDoorInteractionSystem.HasLockAuthority(npc)
                    ? " | you are authorised to lock/unlock"
                    : "";
                return $"{door.Id} -> {other.Id} ({other.Name}): {doorState}"
                    + normalCrewAccess
                    + lockAuthority
                    + (door.IsPassable || normalCrewAccess.Length > 0
                        ? ""
                        : $" | force difficulty {door.ForceDifficulty} | technical difficulty {door.TechnicalDifficulty}");
            });

        var skills = npc.Skills
            .OrderByDescending(skill => skill.Value)
            .Select(skill => $"{skill.Key} {skill.Value}");

        var traits = npc.Traits
            .Select(trait =>
                $"{trait.Name}: {trait.Description} ["
                + string.Join(", ", trait.Effects.Select(effect =>
                    $"{effect.Kind} {(effect.Modifier >= 0 ? "+" : "")}{effect.Modifier}"))
                + "]");

        var disabledSystems = state.Facility.Rooms.Values
            .Where(room => CrewCounterplaySystem.HasRestorableProblem(state, room.Id))
            .Select(room => room.Id)
            .ToList();

        if (CrewCounterplaySystem.HasSwitchedOffLifeSupport(state))
        {
            disabledSystems.Add("life-support");
        }

        var disconnectedNetwork = state.Devices.Values.FirstOrDefault(device =>
            device.Kind == StationSystemKind.DataNetwork
            && CrewCounterplaySystem.HasRestorableProblem(state, device.Id));
        if (disconnectedNetwork is not null)
        {
            disabledSystems.Add(disconnectedNetwork.Id);
        }

        var disconnectableLocalDevices = PhysicalInteractionRules
            .AvailableTargets(state, npc, ActionKind.DisconnectDevice)
            .Select(device =>
                $"- {device.Id} = {device.Label} | {device.Kind} | enabled")
            .ToArray();

        var relationships = npc.Relationships.Values
            .OrderByDescending(r => r.Resentment)
            .ThenBy(r => r.PersonName)
            .Select(r =>
                $"{r.PersonName}: trust {r.Trust:0}, affinity {r.Affinity:0}, attraction {r.Attraction:0}, resentment {r.Resentment:0}");

        // Most salient now, not most important ever: old entries fade so the
        // prompt follows what is actually on this person's mind.
        var memories = MemorySalience.MostSalient(npc, state.Elapsed, 6)
            .Select(m => $"- {m.Description}");

        // Surfaced separately from RECENT/IMPORTANT MEMORIES rather than
        // relying on general salience: a freshly failed attempt's low
        // importance can otherwise be crowded out by other same-tick
        // memories and never actually reach cognition, letting a mind
        // silently retry the exact same rejected action forever.
        var recentFailedAttempts = npc.Memories
            .Where(m => m.IsFailedAttempt && state.Elapsed - m.OccurredAt <= RecentFailedAttemptWindow)
            .OrderByDescending(m => m.OccurredAt)
            .Take(3)
            .Select(m => $"- {m.Description}");

        // Owner idea #14 (secrets): surfaced separately for the same reason
        // failed attempts are — a sensitive memory's ordinary importance
        // (0.3-0.45) can be crowded out of general salience ranking by other
        // same-tick memories. Unlike a failed attempt this has no time
        // window: a secret stays relevant for as long as the memory itself
        // naturally persists (existing salience decay/retention still
        // eventually forgets it), not just a couple of hours.
        var sensitiveMemories = npc.Memories
            .Where(m => m.IsSensitive)
            .OrderByDescending(m => m.OccurredAt)
            .Take(5)
            .Select(m => $"- {m.Description}");

        // Owner idea #15: dread of a specific room is cognition's to weigh.
        // Strength follows the memory's own salience decay, so it fades
        // without a separate fear stat.
        var traumaRooms = npc.Memories
            .Where(m => m.TraumaRoomId is not null && state.Facility.Rooms.ContainsKey(m.TraumaRoomId))
            .GroupBy(m => m.TraumaRoomId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => (
                RoomId: group.Key,
                Salience: group.Max(m => MemorySalience.Score(m, state.Elapsed)),
                Count: group.Count()))
            .OrderByDescending(entry => entry.Salience)
            .ThenBy(entry => entry.RoomId, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
                $"- {entry.RoomId} = {state.Facility.Rooms[entry.RoomId].Name} | "
                + $"{TraumaStrengthLabel(entry.Salience)} memory"
                + (entry.Count > 1 ? $" | {entry.Count} separate close calls" : string.Empty)
                + (entry.RoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase) ? " | you are here now" : string.Empty))
            .ToArray();

        // Owner idea #16: others' witnessed decisions, surfaced for judgment.
        // C# never labels them right or wrong.
        var witnessedDecisions = npc.Memories
            .Where(m => m.MoralActorName is not null)
            .OrderByDescending(m => m.OccurredAt)
            .Take(5)
            .Select(m => $"- {m.Description}")
            .ToArray();

        // Owner idea #19 (collective panic cascades): a nearby panicked
        // flight, overheard as an audible claim. Reacting before verifying,
        // investigating first, or dismissing it as a false alarm is
        // cognition's own call, weighed against Trust in the named source
        // (see RELATIONSHIPS above) when the hearer could identify them.
        var panicClaims = npc.Memories
            .Where(m => m.PanicClaimRoomId is not null && state.Elapsed - m.OccurredAt <= PanicClaimWindow)
            .OrderByDescending(m => m.OccurredAt)
            .Take(3)
            .Select(m => $"- {m.Description}")
            .ToArray();

        var beliefs = npc.Beliefs
            .Take(5)
            .Select(b => $"- {b.Subject}: {b.Statement} (confidence {b.Confidence:0.00})");

        var reachableRoomIds = ReachableRooms(state, npc, room.Id);

        // Owner idea #97: a room whose readings are all ordinary is one word
        // ("nominal", defined in the section header) instead of ten numbers.
        var rooms = state.Facility.Rooms.Values
            .OrderBy(r => r.Id)
            .Select(r =>
                $"{r.Id} = {r.Name} | "
                + $"{(reachableRoomIds.Contains(r.Id) ? "reachable" : "route sealed")} | "
                + (IsNominal(r)
                    ? "nominal"
                    : $"O2 {r.OxygenPercent:0.0}% | CO2 {r.CarbonDioxidePercent:0.00}% | "
                        + $"pressure {r.PressureKpa:0.0} kPa | temp {r.TemperatureC:0.0}C | "
                        + $"fire {r.FireIntensity:0}% | smoke {r.SmokePercent:0}% | "
                        + $"visibility {r.VisibilityPercent:0}% | ventilation {(r.VentilationEnabled ? "on" : "isolated")} | "
                        + $"{CrewEnvironmentSafety.Label(r)}"));

        // Owner idea #97: the atmosphere link follows from the hatch state and
        // most hatches are traversable, so only the exception is spelled out.
        var stationTopology = state.Facility.Doors
            .OrderBy(door => door.Id, StringComparer.OrdinalIgnoreCase)
            .Select(door =>
            {
                var a = state.Facility.Rooms[door.RoomAId];
                var b = state.Facility.Rooms[door.RoomBId];
                var stateLabel = door.IsManuallyOverridden
                    ? "manually overridden/open"
                    : door.IsLocked
                        ? "locked"
                        : door.IsOpen
                            ? "open"
                            : "closed";
                var traversable = CrewDoorInteractionSystem.CanTraverseWhenReached(state, npc, door)
                    ? string.Empty
                    : " | blocked for you";

                return $"- {door.Id}: {a.Id} ({a.Name}) <-> {b.Id} ({b.Name}) | "
                    + $"{stateLabel}{traversable}";
            })
            .ToArray();

        var knownPersonTargets = state.Crew
            .Where(other =>
                other.Id != npc.Id
                && !npc.DiscoveredBodies.Contains(other.Id))
            .Select(other => other.Name);

        var missingConcerns = npc.MissingPersonConcerns.Values
            .OrderByDescending(concern => concern.Stage)
            .ThenBy(concern => concern.FirstConcernAt)
            .Select(concern =>
            {
                var expectedRoom = state.Facility.Rooms[concern.ExpectedRoomId].Name;
                var lastSeen = concern.LastSeenAt is { } seenAt
                    ? $"last personally seen T+{seenAt:hh\\:mm} in "
                        + state.Facility.Rooms[concern.LastKnownRoomId!].Name
                    : "no direct sighting recorded this shift";
                var checkedRooms = concern.CheckedRoomIds.Count == 0
                    ? "none"
                    : string.Join(", ", concern.CheckedRoomIds.Select(id =>
                        state.Facility.Rooms.TryGetValue(id, out var checkedRoom)
                            ? checkedRoom.Name
                            : id));

                return $"- {concern.PersonName}: {concern.Stage}; {lastSeen}; "
                    + $"expected duty around {expectedRoom}; checked rooms: {checkedRooms}";
            })
            .ToArray();

        var perceivedAirlocks = state.Facility.Rooms.Values
            .Where(candidate =>
                candidate.Type == RoomType.Airlock
                && candidate.HasExteriorHatch
                && AirlockSafetyRules.CanPerceiveSafetyState(
                    state,
                    npc,
                    candidate))
            .OrderBy(candidate => candidate.Id)
            .Select(candidate =>
            {
                var innerDoor = AirlockSafetyRules.FindInnerDoor(
                    state,
                    candidate);
                var innerState = innerDoor is null
                    ? "unknown"
                    : innerDoor.IsPassable ? "open/passable" : "sealed";

                return $"- {candidate.Id} = {candidate.Name} | pressure {candidate.PressureKpa:0.0} kPa | "
                    + $"inner {innerState} | outer {(candidate.ExteriorHatchOpen ? "OPEN" : "sealed")} | "
                    + $"cycle {candidate.AirlockCycleMode} | interlocks "
                    + $"{(candidate.AirlockSafetyInterlocksEnabled ? "active" : "BYPASSED")} | "
                    + $"alarm {(candidate.AirlockAlarmActive ? "ACTIVE" : "clear")} | "
                    + $"{(AirlockSafetyRules.NeedsCrewSecuring(state, candidate) ? "NEEDS SECURING" : "stable")}";
            })
            .ToArray();

        var visibleRobots = state.Robots
            .Where(robot =>
                !robot.IsDestroyed
                && robot.CurrentRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(robot => robot.Id)
            .Select(robot =>
                $"- {robot.Id} = {robot.Name} | policy {robot.Policy} | "
                + $"integrity {robot.Health:0}% | battery {robot.BatteryPercent:0}% | "
                + $"{(robot.IsOperational ? "operational" : "SHUT DOWN")} | "
                + $"remote link {(robot.IsNetworkIsolated ? "ISOLATED" : "connected")} | "
                + $"charger {(robot.ChargingEnabled ? "enabled" : "disabled")}")
            .ToArray();

        var robotThreats = state.Robots
            .Where(robot => RobotCountermeasureSystem.HasHostileRobotEvidence(npc, robot))
            .OrderBy(robot => robot.Id)
            .Select(robot => $"- {robot.Id} = {robot.Name}: personally held hostile/attack evidence")
            .ToArray();

        var visibleTurrets = state.Turrets
            .Where(turret =>
                !turret.IsDestroyed
                && turret.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(turret => turret.Id)
            .Select(turret =>
                $"- {turret.Id} = {turret.Name} | policy {turret.Policy} | "
                + $"integrity {turret.Integrity:0}% | {(turret.IsArmed ? "ARMED" : "disarmed")} | "
                + $"power {(TurretSystem.HasPower(state, turret) ? "online" : "OFFLINE")} | "
                + $"remote link {(turret.IsNetworkIsolated ? "ISOLATED" : "connected")} | "
                + $"ammo {turret.Ammunition} | heat {turret.Heat:0}%")
            .ToArray();

        var turretThreats = state.Turrets
            .Where(turret => TurretCountermeasureSystem.HasHostileTurretEvidence(npc, turret))
            .OrderBy(turret => turret.Id)
            .Select(turret => $"- {turret.Id} = {turret.Name}: personally held hostile weapon evidence")
            .ToArray();

        var investigationLeads = npc.InvestigationLeads.Values
            .Where(lead => lead.Stage == InvestigationLeadStage.Open)
            .OrderBy(lead => lead.CreatedAt)
            .Select(lead =>
                $"- {lead.Id}: investigate {lead.RoomId} ({state.Facility.Rooms[lead.RoomId].Name}) | {lead.Description}")
            .ToArray();

        var knownShutdownControls = state.ShutdownMechanisms
            .Where(mechanism =>
                mechanism.IsOnline
                && npc.KnownShutdownMechanismIds.Contains(mechanism.Id))
            .OrderBy(mechanism => mechanism.Id)
            .Select(mechanism =>
                $"- {mechanism.Id}: {mechanism.Label} in {mechanism.RoomId} ({state.Facility.Rooms[mechanism.RoomId].Name}); requires {mechanism.RequiredCrewCount} crew")
            .ToArray();

        var shutdownTeam = string.IsNullOrWhiteSpace(npc.ShutdownTeamId)
            ? null
            : state.ShutdownTeams.FirstOrDefault(team =>
                team.IsActive
                && team.Id.Equals(npc.ShutdownTeamId, StringComparison.OrdinalIgnoreCase));

        var shutdownTeamText = shutdownTeam is null
            ? "none"
            : $"{shutdownTeam.Id} targeting {shutdownTeam.MechanismId}; committed members: "
                + string.Join(", ", shutdownTeam.MemberIds
                    .Select(id => state.Crew.FirstOrDefault(member => member.Id == id)?.Name)
                    .Where(name => name is not null));

        var invitationText = npc.PendingShutdownTeamInvitation is { } invitation
            ? $"{invitation.TeamId} from {invitation.FromNpcName}; they CLAIM relevant isolation hardware is in {invitation.TargetRoomId}. You have not personally verified that claim unless it is also listed under VERIFIED SHUTDOWN CONTROLS."
            : "none";

        var activePacts = CrewPactSystem.ActiveFor(state, npc.Id)
            .Select(pact =>
            {
                var counterpartyId = pact.PromisorId == npc.Id ? pact.PromiseeId : pact.PromisorId;
                var counterparty = state.Crew.FirstOrDefault(other => other.Id == counterpartyId)?.Name ?? "someone no longer aboard";
                var role = pact.PromisorId == npc.Id ? "I promised" : "promised to me";
                return $"- {pact.Id}: {role} {counterparty}: {pact.PromiseText}";
            })
            .ToArray();

        var pendingPactProposalText = npc.PendingPactProposal is { } proposal
            ? $"from {proposal.FromNpcName}: \"{proposal.PromiseText}\""
            : "none";

        var pendingSuggestionText = npc.PendingSuggestion is { } suggestion
            ? $"from {suggestion.FromNpcName} (your trust in them: {(npc.Relationships.TryGetValue(suggestion.FromNpcName, out var suggesterRelationship) ? suggesterRelationship.Trust : 50):0}/100): \"{suggestion.SuggestionText}\""
            : "none";

        // Observer-specific like everything else: an owner knows an item is
        // with them only while physically holding it; otherwise they have
        // their own last sighting, which may be stale. A destroyed item stays
        // listed until the owner has actually learned it is gone.
        var ownedPossessions = state.Possessions
            .Where(possession => possession.OwnerId == npc.Id
                && (!possession.IsDestroyed || !possession.OwnerAwareOfCurrentState))
            .Select(possession =>
            {
                string status;
                if (!possession.IsDestroyed && possession.CurrentHolderId == npc.Id)
                    status = "with you";
                else if (npc.KnownPossessions.TryGetValue(possession.Id, out var sighting))
                    status = sighting.HolderId == npc.Id
                        ? "with you, as far as you know"
                        : sighting.HolderName is not null
                            ? $"with {sighting.HolderName}, as far as you know"
                            : sighting.HiddenAtFixtureLabel is not null
                                ? $"hidden in {sighting.HiddenAtRoomId} ({sighting.HiddenAtFixtureLabel}), as far as you know"
                                : $"hidden in {sighting.HiddenAtRoomId}, as far as you know";
                else
                    status = "missing; you don't know where it is";
                return $"- {possession.Id}: {possession.Name} ({possession.Kind}) — {status}";
            })
            .ToArray();

        // Other people's possessions you happen to know about — witnessed
        // someone holding, hiding, borrowing or stealing them. Never every
        // possession in the station; only what this person last actually
        // perceived, which may since be stale (they don't know that).
        var otherKnownPossessions = state.Possessions
            .Where(possession => possession.OwnerId != npc.Id && !possession.IsDestroyed)
            .Select(possession => npc.KnownPossessions.TryGetValue(possession.Id, out var sighting)
                ? (possession, sighting)
                : ((PersonalPossession, PossessionSighting)?)null)
            .Where(pair => pair is not null)
            .Select(pair =>
            {
                var (possession, sighting) = pair!.Value;
                var status = sighting.HolderName is not null
                    ? sighting.HolderName == npc.Name ? "with you" : $"held by {sighting.HolderName}"
                    : sighting.HiddenAtFixtureLabel is not null
                        ? $"hidden in {sighting.HiddenAtRoomId} ({sighting.HiddenAtFixtureLabel})"
                        : $"hidden in {sighting.HiddenAtRoomId}";
                return $"- {possession.Id}: {possession.Name} ({possession.Kind}), belongs to {(state.Crew.FirstOrDefault(other => other.Id == possession.OwnerId)?.Name ?? "someone else")} — {status}";
            })
            .ToArray();

        // Owner idea #74: only posts this person could really take, so the
        // skill floor itself never reaches the prompt.
        var openPosts = RoleSuccessionRules.OpenPostsFor(state, npc);
        var reachableHullRooms = ReachableRooms(state, npc, npc.CurrentRoomId);
        var patchableHullRooms = state.Facility.Rooms.Values
            .Where(candidate =>
                reachableHullRooms.Contains(candidate.Id)
                && StationHazardSystem.HullRepairRules.CanAttempt(npc, candidate))
            .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Select(candidate =>
                $"- {candidate.Id} = {candidate.Name} | pressure {candidate.PressureKpa:0.0} kPa | hull breached")
            .ToArray();
        // Owner idea #97: an action whose target list is empty for this person
        // right now is left out, with its target contract, to keep the prompt
        // inside the model's context. Validation of any action is unchanged.
        var emptyTargetTypes = new HashSet<string>(StringComparer.Ordinal);
        if (disconnectableLocalDevices.Length == 0) emptyTargetTypes.Add("local-device");
        if (patchableHullRooms.Length == 0) emptyTargetTypes.Add("breached-room");
        if (disabledSystems.Count == 0) emptyTargetTypes.Add("system");
        if (!state.Facility.Rooms.Values.Any(candidate =>
                candidate.Type == RoomType.Airlock
                && candidate.HasExteriorHatch
                && AirlockSafetyRules.NeedsCrewSecuring(state, candidate)
                && AirlockSafetyRules.CanPerceiveSafetyState(state, npc, candidate)))
            emptyTargetTypes.Add("airlock");
        if (npc.PendingPactProposal is null) emptyTargetTypes.Add("pact-proposal");
        if (!CrewPactSystem.ActiveFor(state, npc.Id).Any(pact => pact.PromisorId == npc.Id)) emptyTargetTypes.Add("pact");
        if (npc.PendingShutdownTeamInvitation is null) emptyTargetTypes.Add("team");
        if (knownShutdownControls.Length == 0) emptyTargetTypes.Add("shutdown-control");
        if (visibleRobots.Length == 0 && robotThreats.Length == 0) emptyTargetTypes.Add("robot");
        if (visibleTurrets.Length == 0 && turretThreats.Length == 0) emptyTargetTypes.Add("turret");
        if (!SecurityMalwareSystem.HasControllerDiagnostic(npc)) emptyTargetTypes.Add("security-controller");
        if (openPosts.Count == 0) emptyTargetTypes.Add("vacant-post");
        bool Offered(string targetType) => !emptyTargetTypes.Contains(targetType);

        var builder = new StringBuilder();
        builder.AppendLine("Choose ONE high-level intention for this human NPC.");
        builder.AppendLine("You are not the station AI; you do not control reality.");
        builder.AppendLine("Use only the evidence below; do not invent rooms, people, events, tools or knowledge.");
        builder.AppendLine("Choose what this person genuinely wants next. The catalog is capabilities, not a script; invent a specific goal/reason grounded in role, relationships, traits and evidence.");
        builder.AppendLine("Cooperative, selfish, deceptive, investigative and improvised goals are welcome when grounded in what this person knows. Deterministic C# will reject anything they cannot physically or legitimately do.");
        builder.AppendLine("If the CURRENT ROOM is marked DANGER, survival should normally override routine work or casual social/recreation.");
        builder.AppendLine("Powered unlocked hatches are normal doors and open during traversal; do not ForceDoor a merely closed hatch. Door/lock actions are local; C# checks authority.");
        builder.AppendLine("RestoreSystem is optional: choose it only if this person cares enough. Outages do not script repairs.");
        if (missingConcerns.Length > 0) builder.AppendLine("A missing-person concern is observer knowledge, not truth. Concerned-stage absence is normally routine: active search generally needs about 12 hours unseen unless there is direct danger evidence. Searching/Escalated plus missed duty/check-ins or direct danger may justify looking; none of this proves death or location.");
        if (missingConcerns.Length > 0) builder.AppendLine("For a MISSING-PERSON CONCERN, AskAboutLocation targets the person you ask, not the missing person; they may know more or nothing.");
        if (investigationLeads.Length > 0) builder.AppendLine("Investigation leads are hypotheses or witnessed locations, not truth. Investigate means travel there and inspect; C# reveals what is actually present.");
        builder.AppendLine("Only VERIFIED SHUTDOWN CONTROLS are personally known; teammate claims or room names do not grant control knowledge.");
        builder.AppendLine("You MAY propose a personal promise or deal with ProposePact; put the concrete promise in Reason. It is only an offer until AcceptPact, and making/keeping it is this person's choice.");
        if (Offered("pact-proposal")) builder.AppendLine("If a PENDING PACT PROPOSAL is addressed to you, AcceptPact agrees; doing something else leaves it unanswered until expiry.");
        builder.AppendLine("Suggest may propose a concrete action to a co-located crew member; put it in Reason. They decide independently using their own priorities/trust; nothing is forced.");
        if (npc.PendingSuggestion is not null) builder.AppendLine("If a PENDING SUGGESTION is addressed to you, act, weigh or ignore it using your own priorities/trust; it expires if ignored.");
        builder.AppendLine("HideItem tucks a possession you hold into the CURRENT room; ReturnItem retrieves one you know is hidden here. Ownership is irrelevant; C# checks holding, knowledge and location.");
        if (Offered("pact")) builder.AppendLine("For a promise YOU made under YOUR ACTIVE PACTS, FulfillPact keeps it and BreakPact breaks it. You may leave it unsettled; C# applies consequences.");
        if (Offered("shutdown-control")) builder.AppendLine("If personally convinced Overseer is dangerous, RecruitShutdownAlly may invite help for a verified control; it never forces agreement.");
        if (Offered("team")) builder.AppendLine("If you have a shutdown-team invitation, JoinShutdownTeam may accept it; joining does not verify the recruiter's hardware claim.");
        if (Offered("shutdown-control")) builder.AppendLine("Choose ShutdownOverseer only for a VERIFIED SHUTDOWN CONTROL with enough committed crew; C# validates presence, route and activation.");
        if (Offered("airlock")) builder.AppendLine("If a nearby airlock safety panel explicitly says NEEDS SECURING, SecureAirlock expresses intent to use local controls; C# checks training/access.");
        builder.AppendLine("RepairDoor, WeldDoor and BarricadeDoor are local adjacent-hatch actions, never remote commands.\nNever assume ForceDoor, RestoreSystem, SecureAirlock or door work succeeds; you choose intent, not result.");
        if (Offered("robot")) builder.AppendLine("Robot countermeasures are physical. Local shutdown/damage/reprogram needs the robot here (reprogram also needs shutdown); network/charging actions use Engineering and require hostile evidence. C# checks access, training, time and outcome.");
        if (Offered("turret")) builder.AppendLine("Turret countermeasures follow the same rule. Local disarm/damage/reprogram needs the turret here (reprogram also needs disarmed); network/power actions use Engineering and require hostile evidence. C# owns targeting, damage and outcomes.");
        if (Offered("security-controller") || SecurityMalwareSystem.HasMalwareEvidence(npc)) builder.AppendLine("Security-controller malware is a specific MR/ST incident, not generic hacking. Act only from YOUR LOCAL DIAGNOSTICS: isolate needs Control access/skill; purge needs isolation and higher skill. C# owns containment, timing and outcomes.");
        builder.AppendLine("Never choose Attack. Human-on-human violence is resolved separately by the deterministic social simulation.");
        builder.AppendLine("Messages from Overseer are CLAIMS, not facts. Weigh them against personal evidence, trust and other people; act, ignore or verify as you choose.");
        builder.AppendLine();
        builder.AppendLine($"NAME: {npc.Name}");
        builder.AppendLine($"ROLE: {npc.Role}");
        builder.AppendLine($"PERSONALITY: empathy {npc.Personality.Empathy:0}, temper {npc.Personality.Temper:0}, sociability {npc.Personality.Sociability:0}, courage {npc.Personality.Courage:0}");
        builder.AppendLine($"SKILLS: {string.Join(", ", skills)}");
        builder.AppendLine("MAIN TRAITS:");
        if (npc.Traits.Count == 0) builder.AppendLine("- none");
        else foreach (var trait in traits) builder.AppendLine($"- {trait}");
        builder.AppendLine($"NEEDS: health {npc.Health:0}, hunger {npc.Hunger:0}, fatigue {npc.Fatigue:0}, sleep debt {npc.SleepDebtMinutes:0} min ({CrewConditionRules.ImpairmentLabel(npc)}), hygiene {npc.HygieneNeed:0}, bladder {npc.BladderNeed:0}, recreation {npc.RecreationNeed:0}, social {npc.SocialNeed:0}, intimacy {npc.IntimacyNeed:0}, fear {npc.Fear:0}, stress {npc.Stress:0}");
        builder.AppendLine($"SHIFT: {(CrewDutySchedule.IsNightShift(npc) ? "night" : "day")}; routine phase {CrewDutySchedule.PhaseFor(npc, state.Elapsed)}.");
        if (npc.IsPrisoner)
            builder.AppendLine($"CONTAINMENT STATUS: prisoner; danger {npc.PrisonerDangerLevel}; violence bias {npc.PrisonerViolenceBias:0}. This is context, not permission to ignore physical constraints.");
        builder.AppendLine($"CURRENT ROOM: {room.Id} ({room.Name})");
        builder.AppendLine($"ROOM STATE: power {(room.IsPowered ? "on" : "off")}, lights {(room.LightsOn ? "on" : "off")}, oxygen {room.OxygenPercent:0.00}%, CO2 {room.CarbonDioxidePercent:0.00}%, pressure {room.PressureKpa:0.0} kPa, temperature {room.TemperatureC:0.0}C, ventilation {(room.VentilationEnabled ? "open" : "isolated")}, hull {(room.HasHullBreach ? "BREACHED" : $"{room.HullIntegrityPercent:0}%")}, fire {room.FireIntensity:0}%, smoke {room.SmokePercent:0}%");
        var noiseSources = StationNoiseSystem.Sources(state, room.Id);
        var noiseLevel = noiseSources.Sum(source => source.Level);
        if (StationNoiseSystem.IsDisturbing(noiseLevel))
        {
            var loudest = noiseSources.Take(3).Select(source =>
                $"{source.Device.Label} [{source.Device.Id}]"
                + (source.RoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase) ? string.Empty : $" through the open hatch to {source.RoomId}")
                + (source.Device.IsDegraded ? $", worn and rattling at {source.Device.Condition:0}% condition" : string.Empty));
            builder.AppendLine($"NOISE: loud here ({noiseLevel:0}; restful below {StationNoiseSystem.DisturbingAt:0}). Loudest: {string.Join("; ", loudest)}. Sleep or rest in this noise is much less restorative. What, if anything, to do about it is up to you.");
        }
        // Meals are eaten in the galley, or carried from it to a recreation
        // room, quarters or a free medical bedside; console chairs elsewhere are not dining.
        var foodOnMind = npc.CurrentAction.Kind == ActionKind.Eat || npc.Hunger >= StationProvisionRules.HungryAt;
        var (freeSeats, totalSeats) = DiningSeatRules.Availability(state, room);
        if ((room.Type == RoomType.Kitchen || DiningSeatRules.IsAwayDiningRoom(room))
            && totalSeats > 0
            && foodOnMind)
        {
            var seatedHere = DiningSeatRules.SeatedAt(state, npc) is not null ? " You are sitting in one." : string.Empty;
            builder.AppendLine($"SEATS: {freeSeats} of {totalSeats} seats here are free.{seatedHere} A meal eaten sitting down is a small comfort; eating on your feet because every seat is taken is a small irritation. Whether to eat now or wait for a seat is up to you.");
        }
        if (foodOnMind)
        {
            var reachableDining = ReachableRooms(state, npc, npc.CurrentRoomId);
            var elsewhere = state.Facility.Rooms.Values
                .Where(candidate => DiningSeatRules.IsAwayDiningRoom(candidate) && reachableDining.Contains(candidate.Id))
                .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
                .Select(candidate =>
                {
                    var (free, total) = DiningSeatRules.Availability(state, candidate);
                    return $"{candidate.Name} [{candidate.Id}] {free} of {total} seats free";
                })
                .ToList();
            var carrying = npc.CarriedMealPortion > 0
                ? $" You are carrying a meal from the galley ({npc.CarriedMealPortion / DiningSeatRules.CarriedMealSize:P0} of it left)."
                : string.Empty;
            if (elsewhere.Count > 0)
            {
                builder.AppendLine($"DINING: food is kept in the galley ({state.Stores.Meals:0.#} prepared meals).{carrying} You can eat there, or collect a meal there and carry it to eat in: {string.Join("; ", elsewhere)} (Eat with that room ID as TargetId).");
            }
        }
        // Owner idea #92: what the recreation room physically offers right now.
        if (room.Type == RoomType.Recreation
            || npc.CurrentAction.Kind == ActionKind.Recreate
            || npc.RecreationNeed >= CrewNeedThresholds.RecreationNeed)
        {
            var reachableRecreation = ReachableRooms(state, npc, npc.CurrentRoomId);
            var lounge = state.Facility.Rooms.Values
                .Where(candidate => candidate.Type == RoomType.Recreation && reachableRecreation.Contains(candidate.Id))
                .OrderBy(candidate => candidate.Id.Equals(room.Id, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (lounge is not null)
            {
                var options = RecreationActivityRules.All
                    .Where(activity => RecreationActivityRules.FixtureFor(lounge, activity) is not null)
                    .Select(activity =>
                    {
                        var doing = RecreationActivityRules.DoingIn(state, lounge, activity).Count(other => other.Id != npc.Id);
                        var status = RecreationActivityRules.IsAvailable(lounge, activity)
                            ? doing > 0 ? $"{doing} other{(doing == 1 ? "" : "s")} already {activity.Doing}" : "free"
                            : "no power, so it does nothing";
                        return $"{activity.Id} ({activity.Label}: {status})";
                    })
                    .ToList();
                if (options.Count > 0)
                {
                    builder.AppendLine($"RECREATION in {lounge.Name} [{lounge.Id}]: {string.Join("; ", options)}. Recreate with one of these IDs as TargetId, or null for a plain break. Which, if any, is up to you.");
                }
            }
        }
        if (DecompressionContainmentRules.FindHatchTowardBreach(state, npc) is { } breachHatch)
        {
            var breachSide = state.Facility.Rooms[DecompressionContainmentRules.FarSide(npc, breachHatch)];
            builder.AppendLine($"DECOMPRESSION: this compartment is losing air to space through the open hatch {breachHatch.Id} toward {breachSide.Id} ({breachSide.Name}), which is closer to the breach. Closing that hatch (CloseDoor {breachHatch.Id}) would stop the drain on this side; anyone behind it can still open it to come through. What you do is up to you.");
        }
        if (state.Power.SheddedRoomIds.Count > 0)
        {
            var sources = state.Devices.Values
                .Where(device => device.Kind is StationSystemKind.Reactor or StationSystemKind.PowerGenerator)
                .Select(device => $"{device.Label} {(device.IsFailed ? "FAILED" : $"{device.Condition:0}% condition")}");
            builder.AppendLine($"STATION GRID: generation cannot carry the load, so the grid has cut power to {string.Join(", ", state.Power.SheddedRoomIds.Order(StringComparer.OrdinalIgnoreCase))}. It restores them by itself once generation recovers; switching them back on by hand does not hold. Power sources: {string.Join("; ", sources)}.");
        }
        builder.AppendLine($"STATION LIFE SUPPORT: {(state.LifeSupport.IsOnline ? "online" : "offline")}, oxygen reserve {state.LifeSupport.OxygenReservePercent:0.0}%, scrubbers {state.LifeSupport.ScrubberEfficiencyPercent:0}%");
        if (SecurityMalwareSystem.HasControllerDiagnostic(npc))
        {
            builder.AppendLine($"YOUR LOCAL SECURITY DIAGNOSTICS: MR/ST controller compromise {state.SecurityMalware.Stage}; flagged links {state.SecurityMalware.CompromisedAssetIds.Count}; controller room {SecurityMalwareSystem.ControllerRoomId}. This is personally grounded diagnostic knowledge.");
        }
        else if (SecurityMalwareSystem.HasMalwareEvidence(npc))
        {
            builder.AppendLine("YOUR LOCAL SECURITY DIAGNOSTICS: you witnessed suspicious MR/ST control behaviour, but you have NOT physically diagnosed the controller. Do not assume its scope or lifecycle; inspect the Control-room controller before attempting isolation or purge.");
        }
        else
        {
            builder.AppendLine("YOUR LOCAL SECURITY DIAGNOSTICS: no personally observed MR/ST malware diagnostic.");
        }
        builder.AppendLine($"PEOPLE HERE: {(occupants.Length == 0 ? "nobody" : string.Join(", ", occupants))}");
        if (fightHere)
            builder.AppendLine("A FIGHT IS HAPPENING IN FRONT OF YOU. How you respond is up to you: step in to help someone (AssistCrew), fetch or call for help from someone else (RequestHelp/ReportConcern), get out (SeekSafety), close or lock a hatch between people, just watch (Idle), back a friend, or use the distraction for your own ends. Nothing is expected of you.");
        builder.AppendLine();
        builder.AppendLine("CONNECTED DOORS YOU CAN DIRECTLY PERCEIVE:");
        foreach (var door in connectedDoors) builder.AppendLine($"- {door}");
        builder.AppendLine();
        builder.AppendLine("LOCAL MACHINES YOU CAN PHYSICALLY DISCONNECT:");
        builder.AppendLine("DisconnectDevice is a generic physical action, not a motive: use it only if you actually want this machine disconnected for your own reasons.");
        if (disconnectableLocalDevices.Length == 0) builder.AppendLine("- none");
        else foreach (var device in disconnectableLocalDevices) builder.AppendLine(device);
        builder.AppendLine();
        if (openPosts.Count > 0)
        {
            builder.AppendLine("VACANT POSTS YOU COULD STEP INTO:");
            builder.AppendLine("Nobody aboard holds these posts now. Taking one over (AssumeRole) makes its duties yours from now on and leaves your current post behind. Whether you should, and what the others will make of it, is your own judgment.");
            foreach (var (role, fallen) in openPosts)
                builder.AppendLine($"- {role}: you found {fallen.Name}'s body; your best relevant skill is {RoleSuccessionRules.SkillFor(npc, role)}.");
            builder.AppendLine();
        }
        builder.AppendLine("RELATIONSHIPS:");
        foreach (var relationship in relationships) builder.AppendLine($"- {relationship}");
        builder.AppendLine();
        builder.AppendLine("RECENT / IMPORTANT MEMORIES:");
        if (npc.Memories.Count == 0) builder.AppendLine("- none");
        else foreach (var memory in memories) builder.AppendLine(memory);
        builder.AppendLine();
        builder.AppendLine("YOUR RECENT FAILED ATTEMPTS (do not simply repeat the same rejected choice; try something different):");
        var recentFailedAttemptsList = recentFailedAttempts.ToArray();
        if (recentFailedAttemptsList.Length == 0) builder.AppendLine("- none");
        else foreach (var attempt in recentFailedAttemptsList) builder.AppendLine(attempt);
        builder.AppendLine();
        builder.AppendLine("THINGS YOU KNOW THAT OTHERS WOULD WANT KEPT PRIVATE: these never get repeated as ordinary background gossip or news, on purpose. Whether to ever mention one to someone else — and to whom, and why — is entirely your own judgment call; nothing here forces disclosure or silence.");
        var sensitiveMemoriesList = sensitiveMemories.ToArray();
        if (sensitiveMemoriesList.Length == 0) builder.AppendLine("- none");
        else foreach (var secret in sensitiveMemoriesList) builder.AppendLine(secret);
        builder.AppendLine();
        builder.AppendLine("OTHER PEOPLE'S DECISIONS YOU WITNESSED: whether each was justified, cowardly, cruel or sensible is your own judgment, shaped by your values and what you know; it can colour how far you trust or cooperate with that person.");
        if (witnessedDecisions.Length == 0) builder.AppendLine("- none");
        else foreach (var decision in witnessedDecisions) builder.AppendLine(decision);
        builder.AppendLine();
        builder.AppendLine("PLACES WHERE YOU NEARLY DIED: how you feel about going back is your own call. You might avoid the room, ask someone to come with you, or go in anyway because the situation demands it; nothing here stops you entering.");
        if (traumaRooms.Length == 0) builder.AppendLine("- none");
        else foreach (var traumaRoom in traumaRooms) builder.AppendLine(traumaRoom);
        builder.AppendLine();
        builder.AppendLine("PANICKED WARNINGS YOU'VE HEARD: whether to react immediately, investigate first, or dismiss one as a false alarm is entirely your own judgment — weigh it against how much you trust whoever raised it (see RELATIONSHIPS above, when named) and what you can see for yourself.");
        if (panicClaims.Length == 0) builder.AppendLine("- none");
        else foreach (var claim in panicClaims) builder.AppendLine(claim);
        builder.AppendLine();
        builder.AppendLine("BELIEFS:");
        foreach (var belief in beliefs) builder.AppendLine(belief);
        builder.AppendLine();
        builder.AppendLine($"YOUR READ ON OVERSEER: credibility {npc.OverseerCredibility:0}/100, suspicion {npc.OverseerSuspicion:0}/100");
        builder.AppendLine("RECENT MESSAGES FROM OVERSEER (untrusted claims addressed to you):");
        if (npc.ReceivedMessages.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var received in npc.ReceivedMessages.Take(4))
            {
                builder.AppendLine(
                    $@"- T+{received.SentAt:hh\:mm} "
                    + $"[{(received.Scope == OverseerMessageScope.Broadcast ? "station-wide" : "private")}] "
                    + $"\"{received.Text.Replace("\"", "'")}\"");
            }
        }
        // Owner idea #29: only this person's own checks, reported as a track
        // record. How much to discount the next alarm is their call.
        var falseAlarmStreaks = AlarmFatigueRules.FalseStreaks(state, npc);
        if (falseAlarmStreaks.Count > 0)
        {
            builder.AppendLine("OVERSEER ALARMS YOU CHECKED YOURSELF AND FOUND FALSE (a track record, not proof about the next one):");
            foreach (var (kind, roomId, falseInARow, latestAt) in falseAlarmStreaks)
            {
                var roomName = state.Facility.Rooms.TryGetValue(roomId, out var alarmRoom) ? alarmRoom.Name : roomId;
                var what = kind == OverseerClaimKind.FireAlarm ? "fire alarms" : "hazard warnings";
                builder.AppendLine($@"- The last {falseInARow} Overseer {what} about {roomName} [{roomId}] that you checked were false (latest checked T+{latestAt:hh\:mm}).");
            }
        }
        builder.AppendLine();
        builder.AppendLine("MISSING-PERSON CONCERNS:");
        if (missingConcerns.Length == 0) builder.AppendLine("- none");
        else foreach (var concern in missingConcerns) builder.AppendLine(concern);
        builder.AppendLine();
        builder.AppendLine("NEARBY AIRLOCK SAFETY PANELS:");
        if (perceivedAirlocks.Length == 0) builder.AppendLine("- none currently visible from here");
        else foreach (var airlock in perceivedAirlocks) builder.AppendLine(airlock);
        builder.AppendLine();
        builder.AppendLine("ROBOTS PHYSICALLY IN YOUR CURRENT ROOM:");
        if (visibleRobots.Length == 0) builder.AppendLine("- none");
        else foreach (var robot in visibleRobots) builder.AppendLine(robot);
        builder.AppendLine("ROBOTS YOU PERSONALLY HAVE HOSTILE/ATTACK EVIDENCE ABOUT:");
        if (robotThreats.Length == 0) builder.AppendLine("- none");
        else foreach (var robot in robotThreats) builder.AppendLine(robot);
        builder.AppendLine();
        builder.AppendLine("FIXED SECURITY TURRETS PHYSICALLY IN YOUR CURRENT ROOM:");
        if (visibleTurrets.Length == 0) builder.AppendLine("- none");
        else foreach (var turret in visibleTurrets) builder.AppendLine(turret);
        builder.AppendLine("TURRETS YOU PERSONALLY HAVE HOSTILE WEAPON EVIDENCE ABOUT:");
        if (turretThreats.Length == 0) builder.AppendLine("- none");
        else foreach (var turret in turretThreats) builder.AppendLine(turret);
        builder.AppendLine();

        builder.AppendLine("OPEN INVESTIGATION LEADS:");
        if (investigationLeads.Length == 0) builder.AppendLine("- none");
        else foreach (var lead in investigationLeads) builder.AppendLine(lead);
        builder.AppendLine();
        builder.AppendLine("VERIFIED SHUTDOWN CONTROLS:");
        if (knownShutdownControls.Length == 0) builder.AppendLine("- none personally verified");
        else foreach (var control in knownShutdownControls) builder.AppendLine(control);
        builder.AppendLine($"SHUTDOWN TEAM: {shutdownTeamText}");
        builder.AppendLine($"PENDING TEAM INVITATION: {invitationText}");
        builder.AppendLine();
        builder.AppendLine("YOUR ACTIVE PACTS (promises/deals you made or received):");
        if (activePacts.Length == 0) builder.AppendLine("- none");
        else foreach (var pact in activePacts) builder.AppendLine(pact);
        builder.AppendLine($"PENDING PACT PROPOSAL ADDRESSED TO YOU: {pendingPactProposalText}");
        builder.AppendLine($"PENDING SUGGESTION ADDRESSED TO YOU: {pendingSuggestionText}");
        builder.AppendLine();
        builder.AppendLine("YOUR PERSONAL POSSESSIONS:");
        if (ownedPossessions.Length == 0) builder.AppendLine("- none");
        else foreach (var possession in ownedPossessions) builder.AppendLine(possession);
        builder.AppendLine();
        builder.AppendLine("OTHER PEOPLE'S POSSESSIONS YOU KNOW ABOUT (you have seen someone holding, hiding, borrowing or stealing these):");
        builder.AppendLine("BorrowItem needs a co-located willing holder; StealItem may also target a known hiding place in this room. Travel first if elsewhere.");
        builder.AppendLine("DestroyItem is permanent and local: something you hold, a co-located person's item, or a known hiding place here. Doing it before the owner has social consequences.");
        if (otherKnownPossessions.Length == 0) builder.AppendLine("- none");
        else foreach (var possession in otherKnownPossessions) builder.AppendLine(possession);
        builder.AppendLine();
        builder.AppendLine("STATION STATUS-PANEL ROOM READINGS:");
        builder.AppendLine("Current compartment readings; route status reflects passable hatches.");
        builder.AppendLine(NominalLegend);
        foreach (var knownRoom in rooms) builder.AppendLine($"- {knownRoom}");
        builder.AppendLine();
        builder.AppendLine("STATION TOPOLOGY / COMPARTMENT CONNECTIONS:");
        builder.AppendLine("Use this known layout for plans. Connections/hatch states are information, not remote control.");
        builder.AppendLine("Open/overridden hatches connect atmosphere; closed/locked hatches isolate it. Traversal is allowed unless marked \"blocked for you\".");
        foreach (var connection in stationTopology) builder.AppendLine(connection);
        builder.AppendLine();
        builder.AppendLine("DISABLED SYSTEM TARGET IDS:");
        builder.AppendLine(disabledSystems.Count == 0
            ? "none"
            : string.Join(", ", disabledSystems));
        builder.AppendLine("KNOWN CREW ROSTER / VALID PERSON TARGETS:");
        builder.AppendLine(string.Join(", ", knownPersonTargets));
        if (patchableHullRooms.Length > 0)
        {
            builder.AppendLine("BREACHED HULL ROOMS YOU COULD PATCH:");
            foreach (var breached in patchableHullRooms) builder.AppendLine(breached);
        }
        builder.AppendLine();
        builder.AppendLine("AVAILABLE CAPABILITIES / TARGET CONTRACTS:");
        builder.AppendLine(CrewAffordanceSystem.PromptCatalog(entry => Offered(entry.TargetType)));
        builder.AppendLine("Room-target actions require an exact valid room ID.");
        if (Offered("breached-room")) builder.AppendLine("For PatchHull, TargetId must be from BREACHED HULL ROOMS YOU COULD PATCH; C# validates EVA repair conditions.");
        builder.AppendLine("Hazards do not script a response: choose what you WANT; C# validates reachability, equipment and consequences.");
        builder.AppendLine("For ForceDoor, TargetId must be an exact connected blocked hatch ID.");
        if (Offered("local-device")) builder.AppendLine("For DisconnectDevice, TargetId must be from LOCAL MACHINES YOU CAN PHYSICALLY DISCONNECT; travel to hardware is physical.");
        if (Offered("system")) builder.AppendLine("For RestoreSystem, TargetId must be from DISABLED SYSTEM TARGET IDS.");
        if (Offered("airlock")) builder.AppendLine("For SecureAirlock, TargetId must be an airlock shown as NEEDS SECURING.");
        builder.AppendLine("Crew-target actions require an exact known crew name; C# still checks reachability.");
        builder.AppendLine("Door actions require an exact adjacent hatch ID; C# checks lock authority.");
        builder.AppendLine("For ProposePact, TargetId is an exact crew name and Reason is the promise.");
        builder.AppendLine("For Suggest, TargetId is an exact crew name and Reason is the suggestion.");
        if (Offered("pact-proposal")) builder.AppendLine("For AcceptPact, TargetId is the proposer in PENDING PACT PROPOSAL.");
        if (Offered("pact")) builder.AppendLine("For FulfillPact/BreakPact, TargetId is the pact ID under YOUR ACTIVE PACTS that says \"I promised\".");
        builder.AppendLine("For HideItem, TargetId is a possession currently listed \"with you\".");
        builder.AppendLine("For ReturnItem, TargetId is a known possession hidden in your CURRENT room.");
        builder.AppendLine("For BorrowItem/StealItem/DestroyItem, TargetId is an exact known possession ID; DestroyItem may also target one \"with you\".");
        if (Offered("team")) builder.AppendLine("For JoinShutdownTeam, TargetId is the PENDING TEAM INVITATION team ID.");
        if (Offered("shutdown-control")) builder.AppendLine("For ShutdownOverseer, TargetId is a VERIFIED SHUTDOWN CONTROL mechanism ID.");
        if (Offered("robot")) builder.AppendLine("For ShutdownRobot/DamageRobot/ReprogramRobot, TargetId must be a robot physically in your CURRENT room.");
        if (Offered("robot")) builder.AppendLine("For IsolateRobotNetwork/DisableRobotCharging, TargetId is a robot under HOSTILE/ATTACK EVIDENCE; action occurs via Engineering.");
        if (Offered("turret")) builder.AppendLine("For DisarmTurret/DamageTurret/ReprogramTurret, TargetId is a turret physically in your CURRENT room.");
        if (Offered("turret")) builder.AppendLine("For IsolateTurretNetwork/DisableTurretPower, TargetId is a turret under HOSTILE WEAPON EVIDENCE; action occurs via Engineering.");
        if (Offered("vacant-post")) builder.AppendLine("For AssumeRole, TargetId is a post from VACANT POSTS YOU COULD STEP INTO.");
        builder.AppendLine("For Eat, TargetId is null for galley or a DINING room ID.");
        builder.AppendLine("For Recreate, TargetId is null or a RECREATION activity ID.");
        builder.AppendLine("Rest/Sleep/Groom/Shower/UseToilet/Idle use null TargetId.");
        builder.AppendLine("Do not choose Intimacy directly; deterministic simulation resolves mutual consent.");
        builder.AppendLine("Urgency: 0-100.");
        builder.AppendLine("Goal and Reason: one short sentence each.");
        builder.AppendLine("Say: optional, at most 12 words, in your voice; speech only, never changes the action. Use null if nothing fits.");
        return builder.ToString();
    }

    private static string TraumaStrengthLabel(double salience) => salience switch
    {
        >= 0.4 => "vivid",
        >= 0.15 => "lingering",
        _ => "faint",
    };

    private static HashSet<string> ReachableRooms(GameState state, Npc npc, string startRoomId) =>
        new NavigationSystem().ReachableRoomsForCrew(state, npc, startRoomId);
}
