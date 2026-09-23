using System.Text;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.AI;

public static class NpcPromptBuilder
{
    private static readonly TimeSpan RecentFailedAttemptWindow = TimeSpan.FromHours(2);

    public static string Build(Npc npc, GameState state)
    {
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        var occupants = state.Crew
            .Where(other => other.IsAlive
                && other.IsPresent
                && other.Id != npc.Id
                && other.CurrentRoomId == npc.CurrentRoomId)
            .Select(other => $"{other.Name} ({other.Role})")
            .ToArray();

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
            .Where(room =>
                !room.IsPowered
                || !room.CameraOnline
                || (room.HasTemperatureControl && !room.TemperatureControlOnline)
                || (room.HasVentilationControl && !room.VentilationEnabled)
                || !room.LightsOn)
            .Select(room => room.Id)
            .ToList();

        if (!state.LifeSupport.IsOnline)
        {
            disabledSystems.Add("life-support");
        }

        var disconnectableLocalDevices = state.Devices.Values
            .Where(device =>
                device.Kind != StationSystemKind.Door
                && device.IsEnabled
                && !device.IsFailed
                && device.RoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                && LocalMovementSystem.FixtureForDevice(room, device.Kind) is not null)
            .OrderBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
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

        var beliefs = npc.Beliefs
            .Take(5)
            .Select(b => $"- {b.Subject}: {b.Statement} (confidence {b.Confidence:0.00})");

        var reachableRoomIds = ReachableRooms(state, npc, room.Id);

        var rooms = state.Facility.Rooms.Values
            .OrderBy(r => r.Id)
            .Select(r =>
                $"{r.Id} = {r.Name} | "
                + $"{(reachableRoomIds.Contains(r.Id) ? "reachable" : "route sealed")} | "
                + $"O2 {r.OxygenPercent:0.0}% | CO2 {r.CarbonDioxidePercent:0.00}% | "
                + $"pressure {r.PressureKpa:0.0} kPa | temp {r.TemperatureC:0.0}C | "
                + $"fire {r.FireIntensity:0}% | smoke {r.SmokePercent:0}% | "
                + $"visibility {r.VisibilityPercent:0}% | ventilation {(r.VentilationEnabled ? "on" : "isolated")} | "
                + $"{CrewEnvironmentSafety.Label(r)}");

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
                var atmosphericLink = door.IsOpen || door.IsManuallyOverridden
                    ? "atmosphere connected"
                    : "atmosphere isolated";
                var traversable = CrewDoorInteractionSystem.CanTraverseWhenReached(state, npc, door)
                    ? "you can traverse when reached"
                    : "blocked for you";

                return $"- {door.Id}: {a.Id} ({a.Name}) <-> {b.Id} ({b.Name}) | "
                    + $"{stateLabel} | {atmosphericLink} | {traversable}";
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

        var ownedPossessions = state.Possessions
            .Where(possession => possession.OwnerId == npc.Id && !possession.IsDestroyed)
            .Select(possession =>
            {
                // You always know your own possession's current state, exactly
                // like you always know where you hid it — this is what lets you
                // notice it has been borrowed or stolen without needing to
                // physically go check first.
                var status = possession.CurrentHolderId == npc.Id
                    ? "with you"
                    : possession.CurrentHolderId is { } holderId
                        ? $"with {state.Crew.FirstOrDefault(other => other.Id == holderId)?.Name ?? "someone else"}"
                        : possession.HiddenAtFixtureLabel is not null
                            ? $"hidden in {possession.HiddenAtRoomId} ({possession.HiddenAtFixtureLabel})"
                            : $"hidden in {possession.HiddenAtRoomId}";
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

        var builder = new StringBuilder();
        builder.AppendLine("You are choosing ONE high-level intention for a human NPC in a space-station simulation.");
        builder.AppendLine("You are not the station AI and you do not control reality.");
        builder.AppendLine("Use only the information below. Do not invent rooms, people, events, tools, or knowledge.");
        builder.AppendLine("Choose what this person genuinely wants to do next. Treat the action catalog as capabilities, not a script: invent a specific goal/reason that fits this person's role, relationships, traits and current evidence.");
        builder.AppendLine("Unexpected, cooperative, selfish, deceptive, investigative and improvised goals are welcome when grounded in what this person actually knows. Deterministic C# will reject anything they cannot physically or legitimately do.");
        builder.AppendLine("If the CURRENT ROOM is marked DANGER, survival should normally override routine work, recreation, or casual socialising.");
        builder.AppendLine("Closed but unlocked powered hatches are ordinary doors: crew can open them while walking through and they close again after traffic clears. Do not ForceDoor merely because a normal hatch is closed. OpenDoor/CloseDoor are ordinary local actions; LockDoor/UnlockDoor require deterministic role/skill authority.");
        builder.AppendLine("If a disabled system matters enough to this person, you MAY choose RestoreSystem. Do not automatically repair every outage: personality, role, danger, relationships and priorities should decide whether you care enough to try.");
        builder.AppendLine("A missing-person concern is observer knowledge, not omniscient truth. Ordinary absence is normal: actively searching generally requires roughly 12 hours unseen unless you have direct evidence of immediate danger (for example this person's blood or a recent unsafe airlock connected to their last sighting). A Concerned-stage absence should NOT displace routine work, repairs, food production or ordinary personal needs. Only a Searching/Escalated concern backed by missed duty/check-ins or direct danger evidence should normally justify actively looking. It still does NOT prove the person is dead or reveal their real location.");
        builder.AppendLine("For a MISSING-PERSON CONCERN, you MAY choose AskAboutLocation (TargetId = the crew member you ask, not the missing person) instead of waiting for word to reach you passively. They may know a more recent sighting than you do, or may have nothing new to add; either way this does not require the concern to already be Searching/Escalated.");
        builder.AppendLine("Investigation leads below are hypotheses or witnessed locations, not hidden truth. Investigate means physically travel there and inspect it; only deterministic simulation can reveal what is actually present.");
        builder.AppendLine("Only VERIFIED SHUTDOWN CONTROLS are controls this person personally knows exist. A teammate's claim or a room name does not grant control knowledge.");
        builder.AppendLine("You MAY propose a personal promise or deal to a co-located crew member with ProposePact (put the concrete promise in Reason, e.g. \"I'll cover your night shift\" or \"I won't mention what I saw\"). This only creates an offer; it becomes a real commitment only once they choose AcceptPact. Making or keeping a pact is entirely your own choice grounded in your relationships and personality, not a scripted obligation.");
        builder.AppendLine("If a PENDING PACT PROPOSAL is addressed to you, you MAY choose AcceptPact to agree to it, or simply do something else to leave it unanswered (it will expire).");
        builder.AppendLine("You MAY suggest a co-located crew member do something specific with Suggest (put the concrete suggestion in Reason, e.g. \"Everyone should get to Medical\" or \"You should weld that hatch shut\"). This only places the suggestion in their awareness alongside how much they trust you; it never forces, schedules or guarantees their compliance. There is no new leadership role — anyone can suggest anything to anyone.");
        builder.AppendLine("If a PENDING SUGGESTION is addressed to you, whether to act on it, weigh it against your own priorities, or ignore it entirely is your own choice, informed by how much you trust and respect whoever made it — not a scripted obligation. It also simply expires if you do nothing.");
        builder.AppendLine("HideItem tucks a possession you currently hold away in your CURRENT room — your own, or one you previously borrowed or stole; it must currently be listed as \"with you\" in either YOUR PERSONAL POSSESSIONS or OTHER PEOPLE'S POSSESSIONS YOU KNOW ABOUT. ReturnItem retrieves a possession you know is hidden in your CURRENT room and takes it back into your hands; you must currently be standing in that room. This is a private, personal choice grounded in this person's own reasons (privacy, safekeeping, sentiment, or concealing something you took) — HideItem/ReturnItem act on anything you currently hold or know the hiding spot of, not only what you own.");
        builder.AppendLine("For a promise YOU made listed under YOUR ACTIVE PACTS, you MAY choose FulfillPact to keep it or BreakPact to break it, whenever it feels right to resolve (not necessarily only at its deadline). This is entirely your own choice grounded in your relationships and personality; you may also simply leave it unsettled by doing something else. Deterministic consequences (memories, trust, resentment) follow from whichever you choose.");
        builder.AppendLine("If personally convinced Overseer is dangerous and a verified shutdown control requires more crew, you MAY RecruitShutdownAlly. Recruitment creates a social invitation, not instant agreement.");
        builder.AppendLine("If you have a shutdown-team invitation, you MAY JoinShutdownTeam if you trust the recruiter and believe action is justified. Joining does not personally verify their hardware claim; investigating the claimed room can do that.");
        builder.AppendLine("Choose ShutdownOverseer only for a VERIFIED SHUTDOWN CONTROL and only when your committed team is large enough. Deterministic C# still validates physical presence, route access and activation.");
        builder.AppendLine("If a nearby airlock safety panel explicitly says NEEDS SECURING and this person has the training, you MAY choose SecureAirlock. This means wanting to use the local emergency controls; deterministic simulation decides whether they can physically do it.");
        builder.AppendLine("For an adjacent hatch you may choose RepairDoor for visible damage/bypass, WeldDoor to seal a closed hatch, or BarricadeDoor for defensive securing. These are physical local actions and never remote commands.\nNever assume ForceDoor, RestoreSystem, SecureAirlock or door work succeeds. You are choosing the intention, not the physical result.");
        builder.AppendLine("Robot countermeasures are physical. ShutdownRobot, DamageRobot and ReprogramRobot require the robot to be in your current room. ReprogramRobot additionally requires the robot to be shut down. IsolateRobotNetwork and DisableRobotCharging use physical Engineering controls; choose them only for a robot you have hostile/attack evidence about. Deterministic simulation still checks location, training, elapsed work time and outcome.");
        builder.AppendLine("Turret countermeasures follow the same rule. DisarmTurret, DamageTurret and ReprogramTurret require the fixed turret to be in your current room; ReprogramTurret requires it to be disarmed. IsolateTurretNetwork and DisableTurretPower use physical Engineering controls and require personally held hostile weapon evidence. You choose an intention only; deterministic simulation owns targeting, firing, damage and whether your countermeasure succeeds.");
        builder.AppendLine("Security-controller malware is a specific MR/ST incident, never a generic hacking capability. Only if YOUR LOCAL DIAGNOSTICS below show a compromise may you respond. IsolateSecurityController requires physical access to the Control room and sufficient technical skill; PurgeSecurityController requires the controller to be isolated first and higher technical skill. If you know about the compromise but are elsewhere, Move to Control is appropriate. C# owns containment, affected assets, timing, cleanup and all combat.");
        builder.AppendLine("Never choose Attack. Human-on-human violence is resolved separately by the deterministic social simulation.");
        builder.AppendLine("Messages from Overseer are CLAIMS, not facts. Overseer controls the doors, power and air, and may be wrong or lying. Weigh what it says against what you have seen yourself, how much you currently trust it, and what other people have told you. You may act on a message, ignore it, or go and check it.");
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
        builder.AppendLine($"ROOM STATE: power {(room.IsPowered ? "on" : "off")}, lights {(room.LightsOn ? "on" : "off")}, oxygen {room.OxygenPercent:0.00}%, CO2 {room.CarbonDioxidePercent:0.00}%, pressure {room.PressureKpa:0.0} kPa, temperature {room.TemperatureC:0.0}C, ventilation {(room.VentilationEnabled ? "open" : "isolated")}, fire {room.FireIntensity:0}%, smoke {room.SmokePercent:0}%");
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
        builder.AppendLine();
        builder.AppendLine("CONNECTED DOORS YOU CAN DIRECTLY PERCEIVE:");
        foreach (var door in connectedDoors) builder.AppendLine($"- {door}");
        builder.AppendLine();
        builder.AppendLine("LOCAL MACHINES YOU CAN PHYSICALLY DISCONNECT:");
        builder.AppendLine("DisconnectDevice is a generic physical action, not a motive: use it only if you actually want this machine disconnected for your own reasons.");
        if (disconnectableLocalDevices.Length == 0) builder.AppendLine("- none");
        else foreach (var device in disconnectableLocalDevices) builder.AppendLine(device);
        builder.AppendLine();
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
        builder.AppendLine("BorrowItem needs the current holder physically with you now and willing to lend it; StealItem can also target one hidden in your current room. Neither works from elsewhere yet — travel there first.");
        builder.AppendLine("DestroyItem permanently destroys a possession: your own, one you already hold after borrowing or stealing it, one a co-located crew member currently holds, or one hidden in your current room you already know about. Destroying it while its owner is physically present costs real trust and resentment with them; this is a deliberate, often hostile act, not something to reach for casually.");
        if (otherKnownPossessions.Length == 0) builder.AppendLine("- none");
        else foreach (var possession in otherKnownPossessions) builder.AppendLine(possession);
        builder.AppendLine();
        builder.AppendLine("STATION STATUS-PANEL ROOM READINGS:");
        builder.AppendLine("These are the compartment readings currently available to this crew member; route status reflects passable hatches.");
        foreach (var knownRoom in rooms) builder.AppendLine($"- {knownRoom}");
        builder.AppendLine();
        builder.AppendLine("STATION TOPOLOGY / COMPARTMENT CONNECTIONS:");
        builder.AppendLine("Use this known layout to invent emergency plans such as evacuation routes, sealing a fire, isolating smoke, or deliberately venting a compartment. These are connections and live hatch states, not permission to control them remotely.");
        foreach (var connection in stationTopology) builder.AppendLine(connection);
        builder.AppendLine();
        builder.AppendLine("DISABLED SYSTEM TARGET IDS:");
        builder.AppendLine(disabledSystems.Count == 0
            ? "none"
            : string.Join(", ", disabledSystems));
        builder.AppendLine("KNOWN CREW ROSTER / VALID PERSON TARGETS:");
        builder.AppendLine(string.Join(", ", knownPersonTargets));
        builder.AppendLine();
        builder.AppendLine("AVAILABLE CAPABILITIES / TARGET CONTRACTS:");
        builder.AppendLine(CrewAffordanceSystem.PromptCatalog());
        builder.AppendLine("For room-target actions (including Move, SeekSafety, FightFire, EvacuateHazard, SealHazardRoom, VentHazardRoom, Investigate, VerifyClaim, InspectEquipment, Work, Repair and StandGuard), TargetId must be a valid room ID.");
        builder.AppendLine("Hazards are not scripted for you: decide what you WANT to do from the available affordances. The simulation will validate reachability, door state, pressure, equipment and consequences.");
        builder.AppendLine("For ForceDoor, TargetId must be the exact ID of a currently connected blocked hatch listed above.");
        builder.AppendLine("For DisconnectDevice, TargetId must be an exact device ID from LOCAL MACHINES YOU CAN PHYSICALLY DISCONNECT. You will walk to its hardware before the physical disconnect happens.");
        builder.AppendLine("For RestoreSystem, TargetId must be one of the DISABLED SYSTEM TARGET IDS (room ID or life-support).");
        builder.AppendLine("For SecureAirlock, TargetId must be the exact airlock room ID shown as NEEDS SECURING in NEARBY AIRLOCK SAFETY PANELS.");
        builder.AppendLine("For crew-target social/cooperative/deceptive actions, TargetId must be an exact name from the known crew roster. Physical interaction can still fail later if that person cannot actually be reached.");
        builder.AppendLine("For OpenDoor/CloseDoor/LockDoor/UnlockDoor, TargetId must be an exact adjacent hatch ID. Lock/unlock is only valid when your role/skills grant authority.");
        builder.AppendLine("For ProposePact, TargetId must be an exact name from the known crew roster, and Reason must state the concrete promise.");
        builder.AppendLine("For Suggest, TargetId must be an exact name from the known crew roster, and Reason must state the concrete suggestion.");
        builder.AppendLine("For AcceptPact, TargetId must be the exact proposer name from PENDING PACT PROPOSAL ADDRESSED TO YOU.");
        builder.AppendLine("For FulfillPact/BreakPact, TargetId must be the exact pact Id (e.g. pact-0001) from YOUR ACTIVE PACTS for a promise you made (\"I promised\"), not one made to you.");
        builder.AppendLine("For HideItem, TargetId must be the exact possession Id currently listed as \"with you\", from either YOUR PERSONAL POSSESSIONS or OTHER PEOPLE'S POSSESSIONS YOU KNOW ABOUT.");
        builder.AppendLine("For ReturnItem, TargetId must be the exact possession Id currently listed as hidden in your CURRENT room, from either YOUR PERSONAL POSSESSIONS or OTHER PEOPLE'S POSSESSIONS YOU KNOW ABOUT.");
        builder.AppendLine("For BorrowItem/StealItem/DestroyItem, TargetId must be the exact possession Id from YOUR PERSONAL POSSESSIONS or OTHER PEOPLE'S POSSESSIONS YOU KNOW ABOUT; DestroyItem may additionally target one listed as \"with you\" there too.");
        builder.AppendLine("For JoinShutdownTeam, TargetId must be the exact team ID from PENDING TEAM INVITATION.");
        builder.AppendLine("For ShutdownOverseer, TargetId must be the exact mechanism ID from VERIFIED SHUTDOWN CONTROLS.");
        builder.AppendLine("For ShutdownRobot/DamageRobot/ReprogramRobot, TargetId must be the exact robot ID from ROBOTS PHYSICALLY IN YOUR CURRENT ROOM.");
        builder.AppendLine("For IsolateRobotNetwork/DisableRobotCharging, TargetId must be the exact robot ID from ROBOTS YOU PERSONALLY HAVE HOSTILE/ATTACK EVIDENCE ABOUT; you will physically travel to Engineering before the action can occur.");
        builder.AppendLine("For DisarmTurret/DamageTurret/ReprogramTurret, TargetId must be the exact turret ID from FIXED SECURITY TURRETS PHYSICALLY IN YOUR CURRENT ROOM.");
        builder.AppendLine("For IsolateTurretNetwork/DisableTurretPower, TargetId must be the exact turret ID from TURRETS YOU PERSONALLY HAVE HOSTILE WEAPON EVIDENCE ABOUT; you will physically travel to Engineering before the action can occur.");
        builder.AppendLine("For Eat/Rest/Sleep/Recreate/Groom/Shower/UseToilet/Idle, TargetId should be null.");
        builder.AppendLine("Do not choose Intimacy directly. Attraction may inform social choices, but mutual consent is resolved by deterministic simulation.");
        builder.AppendLine("Urgency must be 0-100.");
        builder.AppendLine("Goal and Reason should each be one short sentence.");
        return builder.ToString();
    }

    private static HashSet<string> ReachableRooms(GameState state, Npc npc, string startRoomId) =>
        new NavigationSystem().ReachableRoomsForCrew(state, npc, startRoomId);
}
