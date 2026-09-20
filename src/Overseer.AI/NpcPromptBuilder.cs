using System.Text;
using Overseer.Domain;

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
        ActionKind.ForceDoor,
        ActionKind.RestoreSystem,
        ActionKind.SecureAirlock,
        ActionKind.RepairDoor,
        ActionKind.WeldDoor,
        ActionKind.BarricadeDoor
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

        var builder = new StringBuilder();
        builder.AppendLine("You are choosing ONE high-level intention for a human NPC in a space-station simulation.");
        builder.AppendLine("You are not the station AI and you do not control reality.");
        builder.AppendLine("Use only the information below. Do not invent rooms, people, events, tools, or knowledge.");
        builder.AppendLine("Choose what this person genuinely wants to do next, including socially awkward or selfish choices when justified.");
        builder.AppendLine("If the CURRENT ROOM is marked DANGER, survival should normally override routine work, recreation, or casual socialising.");
        builder.AppendLine("If a hatch blocks something you strongly want to do, you MAY choose ForceDoor for an adjacent blocked hatch. Whether it works is resolved later from skills, traits and chance.");
        builder.AppendLine("If a disabled system matters enough to this person, you MAY choose RestoreSystem. Do not automatically repair every outage: personality, role, danger, relationships and priorities should decide whether you care enough to try.");
        builder.AppendLine("A missing-person concern is observer knowledge, not omniscient truth. It does NOT prove that person is dead or reveal their real location. You may Investigate a plausible room, ask another known crewmember for help, or keep another priority if it matters more.");
        builder.AppendLine("If a nearby airlock safety panel explicitly says NEEDS SECURING and this person has the training, you MAY choose SecureAirlock. This means wanting to use the local emergency controls; deterministic simulation decides whether they can physically do it.");
        builder.AppendLine("For an adjacent hatch you may choose RepairDoor for visible damage/bypass, WeldDoor to seal a closed hatch, or BarricadeDoor for defensive securing. These are physical local actions and never remote commands.\nNever assume ForceDoor, RestoreSystem, SecureAirlock or door work succeeds. You are choosing the intention, not the physical result.");
        builder.AppendLine("Never choose Attack. Violence is resolved separately by the deterministic social simulation.");
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
        builder.AppendLine("MISSING-PERSON CONCERNS:");
        if (missingConcerns.Length == 0) builder.AppendLine("- none");
        else foreach (var concern in missingConcerns) builder.AppendLine(concern);
        builder.AppendLine();
        builder.AppendLine("NEARBY AIRLOCK SAFETY PANELS:");
        if (perceivedAirlocks.Length == 0) builder.AppendLine("- none currently visible from here");
        else foreach (var airlock in perceivedAirlocks) builder.AppendLine(airlock);
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
        builder.AppendLine("For Talk/Socialize/Argue/RequestHelp, TargetId must be an exact name from the known crew roster. Physical interaction can still fail later if that person cannot actually be reached.");
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
