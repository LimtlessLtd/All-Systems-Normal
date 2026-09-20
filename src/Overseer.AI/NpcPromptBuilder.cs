using System.Text;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.AI;

public static class NpcPromptBuilder
{
    private static readonly ActionKind[] AllowedActions =
    [
        ActionKind.Idle,
        ActionKind.Move,
        ActionKind.Rest,
        ActionKind.Sleep,
        ActionKind.Eat,
        ActionKind.Recreate,
        ActionKind.Groom,
        ActionKind.Shower,
        ActionKind.UseToilet,
        ActionKind.Work,
        ActionKind.Investigate,
        ActionKind.Repair,
        ActionKind.Talk,
        ActionKind.Socialize,
        ActionKind.Argue,
        ActionKind.RequestHelp,
        ActionKind.RecruitShutdownAlly,
        ActionKind.JoinShutdownTeam,
        ActionKind.ShutdownOverseer,
        ActionKind.ForceDoor,
        ActionKind.RestoreSystem,
        ActionKind.SecureAirlock,
        ActionKind.RepairDoor,
        ActionKind.WeldDoor,
        ActionKind.BarricadeDoor,
        ActionKind.ShutdownRobot,
        ActionKind.IsolateRobotNetwork,
        ActionKind.DisableRobotCharging,
        ActionKind.DamageRobot,
        ActionKind.ReprogramRobot,
        ActionKind.DisarmTurret,
        ActionKind.IsolateTurretNetwork,
        ActionKind.DisableTurretPower,
        ActionKind.DamageTurret,
        ActionKind.ReprogramTurret
    ];

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
                return $"{door.Id} -> {other.Id} ({other.Name}): {doorState}"
                    + (door.IsPassable ? "" : $" | force difficulty {door.ForceDifficulty} | technical difficulty {door.TechnicalDifficulty}");
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

        var relationships = npc.Relationships.Values
            .OrderByDescending(r => r.Resentment)
            .ThenBy(r => r.PersonName)
            .Select(r =>
                $"{r.PersonName}: trust {r.Trust:0}, affinity {r.Affinity:0}, attraction {r.Attraction:0}, resentment {r.Resentment:0}");

        var memories = npc.Memories
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.OccurredAt)
            .Take(6)
            .Select(m => $"- {m.Description}");

        var beliefs = npc.Beliefs
            .Take(5)
            .Select(b => $"- {b.Subject}: {b.Statement} (confidence {b.Confidence:0.00})");

        var reachableRoomIds = ReachableRooms(state.Facility, room.Id);

        var rooms = state.Facility.Rooms.Values
            .OrderBy(r => r.Id)
            .Select(r =>
                $"{r.Id} = {r.Name} | "
                + $"{(reachableRoomIds.Contains(r.Id) ? "reachable" : "route sealed")} | "
                + $"O2 {r.OxygenPercent:0.0}% | CO2 {r.CarbonDioxidePercent:0.00}% | "
                + $"pressure {r.PressureKpa:0.0} kPa | temp {r.TemperatureC:0.0}C | "
                + $"{CrewEnvironmentSafety.Label(r)}");

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

        var builder = new StringBuilder();
        builder.AppendLine("You are choosing ONE high-level intention for a human NPC in a space-station simulation.");
        builder.AppendLine("You are not the station AI and you do not control reality.");
        builder.AppendLine("Use only the information below. Do not invent rooms, people, events, tools, or knowledge.");
        builder.AppendLine("Choose what this person genuinely wants to do next, including socially awkward or selfish choices when justified.");
        builder.AppendLine("If the CURRENT ROOM is marked DANGER, survival should normally override routine work, recreation, or casual socialising.");
        builder.AppendLine("If a hatch blocks something you strongly want to do, you MAY choose ForceDoor for an adjacent blocked hatch. Whether it works is resolved later from skills, traits and chance.");
        builder.AppendLine("If a disabled system matters enough to this person, you MAY choose RestoreSystem. Do not automatically repair every outage: personality, role, danger, relationships and priorities should decide whether you care enough to try.");
        builder.AppendLine("A missing-person concern is observer knowledge, not omniscient truth. It does NOT prove that person is dead or reveal their real location. You may Investigate a plausible room, ask another known crewmember for help, or keep another priority if it matters more.");
        builder.AppendLine("Investigation leads below are hypotheses or witnessed locations, not hidden truth. Investigate means physically travel there and inspect it; only deterministic simulation can reveal what is actually present.");
        builder.AppendLine("Only VERIFIED SHUTDOWN CONTROLS are controls this person personally knows exist. A teammate's claim or a room name does not grant control knowledge.");
        builder.AppendLine("If personally convinced Overseer is dangerous and a verified shutdown control requires more crew, you MAY RecruitShutdownAlly. Recruitment creates a social invitation, not instant agreement.");
        builder.AppendLine("If you have a shutdown-team invitation, you MAY JoinShutdownTeam if you trust the recruiter and believe action is justified. Joining does not personally verify their hardware claim; investigating the claimed room can do that.");
        builder.AppendLine("Choose ShutdownOverseer only for a VERIFIED SHUTDOWN CONTROL and only when your committed team is large enough. Deterministic C# still validates physical presence, route access and activation.");
        builder.AppendLine("If a nearby airlock safety panel explicitly says NEEDS SECURING and this person has the training, you MAY choose SecureAirlock. This means wanting to use the local emergency controls; deterministic simulation decides whether they can physically do it.");
        builder.AppendLine("For an adjacent hatch you may choose RepairDoor for visible damage/bypass, WeldDoor to seal a closed hatch, or BarricadeDoor for defensive securing. These are physical local actions and never remote commands.\nNever assume ForceDoor, RestoreSystem, SecureAirlock or door work succeeds. You are choosing the intention, not the physical result.");
        builder.AppendLine("Robot countermeasures are physical. ShutdownRobot, DamageRobot and ReprogramRobot require the robot to be in your current room. ReprogramRobot additionally requires the robot to be shut down. IsolateRobotNetwork and DisableRobotCharging use physical Engineering controls; choose them only for a robot you have hostile/attack evidence about. Deterministic simulation still checks location, training, elapsed work time and outcome.");
        builder.AppendLine("Turret countermeasures follow the same rule. DisarmTurret, DamageTurret and ReprogramTurret require the fixed turret to be in your current room; ReprogramTurret requires it to be disarmed. IsolateTurretNetwork and DisableTurretPower use physical Engineering controls and require personally held hostile weapon evidence. You choose an intention only; deterministic simulation owns targeting, firing, damage and whether your countermeasure succeeds.");
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
        builder.AppendLine($"NEEDS: health {npc.Health:0}, hunger {npc.Hunger:0}, fatigue {npc.Fatigue:0}, hygiene {npc.HygieneNeed:0}, bladder {npc.BladderNeed:0}, recreation {npc.RecreationNeed:0}, social {npc.SocialNeed:0}, intimacy {npc.IntimacyNeed:0}, fear {npc.Fear:0}, stress {npc.Stress:0}");
        builder.AppendLine($"CURRENT ROOM: {room.Id} ({room.Name})");
        builder.AppendLine($"ROOM STATE: power {(room.IsPowered ? "on" : "off")}, lights {(room.LightsOn ? "on" : "off")}, oxygen {room.OxygenPercent:0.00}%, CO2 {room.CarbonDioxidePercent:0.00}%, pressure {room.PressureKpa:0.0} kPa, temperature {room.TemperatureC:0.0}C, ventilation {(room.VentilationEnabled ? "open" : "isolated")}");
        builder.AppendLine($"STATION LIFE SUPPORT: {(state.LifeSupport.IsOnline ? "online" : "offline")}, oxygen reserve {state.LifeSupport.OxygenReservePercent:0.0}%, scrubbers {state.LifeSupport.ScrubberEfficiencyPercent:0}%");
        builder.AppendLine($"PEOPLE HERE: {(occupants.Length == 0 ? "nobody" : string.Join(", ", occupants))}");
        builder.AppendLine();
        builder.AppendLine("CONNECTED DOORS YOU CAN DIRECTLY PERCEIVE:");
        foreach (var door in connectedDoors) builder.AppendLine($"- {door}");
        builder.AppendLine();
        builder.AppendLine("RELATIONSHIPS:");
        foreach (var relationship in relationships) builder.AppendLine($"- {relationship}");
        builder.AppendLine();
        builder.AppendLine("RECENT / IMPORTANT MEMORIES:");
        if (npc.Memories.Count == 0) builder.AppendLine("- none");
        else foreach (var memory in memories) builder.AppendLine(memory);
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
        builder.AppendLine("STATION STATUS-PANEL ROOM READINGS:");
        builder.AppendLine("These are the compartment readings currently available to this crew member; route status reflects passable hatches.");
        foreach (var knownRoom in rooms) builder.AppendLine($"- {knownRoom}");
        builder.AppendLine();
        builder.AppendLine("DISABLED SYSTEM TARGET IDS:");
        builder.AppendLine(disabledSystems.Count == 0
            ? "none"
            : string.Join(", ", disabledSystems));
        builder.AppendLine("KNOWN CREW ROSTER / VALID PERSON TARGETS:");
        builder.AppendLine(string.Join(", ", knownPersonTargets));
        builder.AppendLine();
        builder.AppendLine($"ALLOWED ACTIONS: {string.Join(", ", AllowedActions)}");
        builder.AppendLine("For Move/Investigate/Repair/Work, TargetId must be a valid room ID.");
        builder.AppendLine("For ForceDoor, TargetId must be the exact ID of a currently connected blocked hatch listed above.");
        builder.AppendLine("For RestoreSystem, TargetId must be one of the DISABLED SYSTEM TARGET IDS (room ID or life-support).");
        builder.AppendLine("For SecureAirlock, TargetId must be the exact airlock room ID shown as NEEDS SECURING in NEARBY AIRLOCK SAFETY PANELS.");
        builder.AppendLine("For Talk/Socialize/Argue/RequestHelp/RecruitShutdownAlly, TargetId must be an exact name from the known crew roster. Physical interaction can still fail later if that person cannot actually be reached.");
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

    private static HashSet<string> ReachableRooms(Facility facility, string startRoomId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            startRoomId
        };
        var queue = new Queue<string>();
        queue.Enqueue(startRoomId);

        while (queue.TryDequeue(out var current))
        {
            foreach (var door in facility.Doors.Where(door =>
                         door.IsPassable
                         && (door.RoomAId.Equals(current, StringComparison.OrdinalIgnoreCase)
                             || door.RoomBId.Equals(current, StringComparison.OrdinalIgnoreCase))))
            {
                var next = door.RoomAId.Equals(current, StringComparison.OrdinalIgnoreCase)
                    ? door.RoomBId
                    : door.RoomAId;

                if (visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return visited;
    }
}
