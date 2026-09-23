using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #19 (collective panic cascades): the moment any mind — Ollama,
/// <see cref="RuleBasedAiDecisionService"/> or <see cref="BrowserMindSystem"/>
/// — decides to flee a genuinely dangerous room, that flight becomes a
/// witnessable/audible claim for whoever is in earshot, exactly like a
/// person shouting about a fire as they run. C# only records who heard what
/// and from whom (when identifiable); it never decides whether a hearer
/// reacts immediately, investigates first, or ignores it — that is
/// <see cref="NpcPromptBuilder"/>'s "PANICKED WARNINGS" block plus the
/// hearer's own <see cref="Relationship.Trust"/> in the source, already
/// visible in the RELATIONSHIPS block, for cognition to weigh.
/// </summary>
public sealed class PanicAlertSystem
{
    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            var currentRoom = state.Facility.Rooms[npc.CurrentRoomId];

            if (!CrewEnvironmentSafety.IsDangerous(currentRoom))
            {
                npc.PanicAlertSounded = false;
                continue;
            }

            if (npc.PanicAlertSounded
                || npc.Intent is not { Action: ActionKind.Move or ActionKind.SeekSafety or ActionKind.EvacuateHazard })
            {
                continue;
            }

            npc.PanicAlertSounded = true;
            SoundAlert(state, npc, currentRoom);
        }
    }

    private static void SoundAlert(GameState state, Npc fleeing, Room room)
    {
        var hazard = HazardDescription(room);

        foreach (var hearer in state.Crew.Where(candidate =>
                     candidate.IsAlive
                     && candidate.IsPresent
                     && candidate.Id != fleeing.Id
                     && candidate.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
        {
            hearer.Memories.Add(PerceptionSystem.CanMakeOut(state, hearer, fleeing)
                ? new Memory(
                    $"{fleeing.Name} fled past you in a panic shouting about {hazard} in {room.Name}!",
                    state.Elapsed,
                    0.55,
                    PanicClaimRoomId: room.Id)
                : new Memory(
                    $"Someone fled past you in a panic shouting about {hazard} in {room.Name}!",
                    state.Elapsed,
                    0.5,
                    PanicClaimRoomId: room.Id));
            hearer.NeedsMindReconsideration = true;
        }

        // The shout carries through an open hatch even without a line of
        // sight, the same convention smoke/sound already follow elsewhere;
        // a hearer through a wall can never identify the source.
        foreach (var door in state.Facility.Doors.Where(door =>
                     door.IsOpen
                     && (door.RoomAId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                         || door.RoomBId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))))
        {
            var adjacentRoomId = door.RoomAId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                ? door.RoomBId
                : door.RoomAId;

            foreach (var hearer in state.Crew.Where(candidate =>
                         candidate.IsAlive
                         && candidate.IsPresent
                         && candidate.CurrentRoomId.Equals(adjacentRoomId, StringComparison.OrdinalIgnoreCase)))
            {
                hearer.Memories.Add(new Memory(
                    $"Heard someone shout in panic about {hazard} through the hatch from {room.Name}!",
                    state.Elapsed,
                    0.4,
                    PanicClaimRoomId: room.Id));
                hearer.NeedsMindReconsideration = true;
            }
        }
    }

    private static string HazardDescription(Room room)
    {
        if (room.FireIntensity >= 12) return "a fire";
        if (room.SmokePercent >= 35) return "heavy smoke";
        if (room.OxygenPercent < 19.0) return "an oxygen problem";
        if (room.PressureKpa < 90) return "a pressure loss";
        if (room.TemperatureC is < 6 or > 38) return "the temperature";
        return "danger";
    }
}
