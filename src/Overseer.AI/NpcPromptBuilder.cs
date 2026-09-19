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
        ActionKind.Eat,
        ActionKind.Investigate,
        ActionKind.Repair,
        ActionKind.Talk,
        ActionKind.Socialize,
        ActionKind.Argue,
        ActionKind.RequestHelp
    ];

    public static string Build(Npc npc, GameState state)
    {
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        var occupants = state.Crew
            .Where(other => other.IsAlive
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
                return $"{other.Id} ({other.Name}): {doorState}";
            });

        var relationships = npc.Relationships.Values
            .OrderByDescending(r => r.Resentment)
            .ThenBy(r => r.PersonName)
            .Select(r =>
                $"{r.PersonName}: trust {r.Trust:0}, affinity {r.Affinity:0}, resentment {r.Resentment:0}");

        var memories = npc.Memories
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.OccurredAt)
            .Take(6)
            .Select(m => $"- {m.Description}");

        var beliefs = npc.Beliefs
            .Take(5)
            .Select(b => $"- {b.Subject}: {b.Statement} (confidence {b.Confidence:0.00})");

        var rooms = state.Facility.Rooms.Values
            .OrderBy(r => r.Id)
            .Select(r => $"{r.Id} = {r.Name}");

        var livingCrew = state.Crew
            .Where(other => other.IsAlive && other.Id != npc.Id)
            .Select(other => other.Name);

        var builder = new StringBuilder();
        builder.AppendLine("You are choosing ONE high-level intention for a human NPC in a space-station simulation.");
        builder.AppendLine("You are not the station AI and you do not control reality.");
        builder.AppendLine("Use only the information below. Do not invent rooms, people, events, tools, or knowledge.");
        builder.AppendLine("Choose what this person genuinely wants to do next, including socially awkward or selfish choices when justified.");
        builder.AppendLine("Never choose Attack. Violence is resolved separately by the deterministic social simulation.");
        builder.AppendLine();
        builder.AppendLine($"NAME: {npc.Name}");
        builder.AppendLine($"ROLE: {npc.Role}");
        builder.AppendLine($"PERSONALITY: empathy {npc.Personality.Empathy:0}, temper {npc.Personality.Temper:0}, sociability {npc.Personality.Sociability:0}, courage {npc.Personality.Courage:0}");
        builder.AppendLine($"NEEDS: health {npc.Health:0}, hunger {npc.Hunger:0}, fatigue {npc.Fatigue:0}, fear {npc.Fear:0}, stress {npc.Stress:0}");
        builder.AppendLine($"CURRENT ROOM: {room.Id} ({room.Name})");
        builder.AppendLine($"ROOM STATE: power {(room.IsPowered ? "on" : "off")}, lights {(room.LightsOn ? "on" : "off")}, oxygen {room.OxygenPercent:0.0}%, temperature {room.TemperatureC:0.0}C");
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
        builder.AppendLine("VALID ROOM TARGET IDS:");
        builder.AppendLine(string.Join(", ", rooms));
        builder.AppendLine("VALID PERSON TARGETS:");
        builder.AppendLine(string.Join(", ", livingCrew));
        builder.AppendLine();
        builder.AppendLine($"ALLOWED ACTIONS: {string.Join(", ", AllowedActions)}");
        builder.AppendLine("For Move/Investigate/Repair, TargetId must be a valid room ID.");
        builder.AppendLine("For Talk/Socialize/Argue/RequestHelp, TargetId must be an exact living person's name.");
        builder.AppendLine("For Eat/Rest/Idle, TargetId should be null.");
        builder.AppendLine("Urgency must be 0-100.");
        builder.AppendLine("Goal and Reason should each be one short sentence.");
        return builder.ToString();
    }
}
