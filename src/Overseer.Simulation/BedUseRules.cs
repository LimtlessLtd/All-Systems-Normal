using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic capacity/assignment for physical beds. This never decides
/// whether somebody wants to sleep; it only maps an already-sleeping person to
/// one real bed so unrelated sleepers cannot occupy the same fixture.
/// </summary>
public static class BedUseRules
{
    public static RoomFixture? AssignedBed(GameState state, Npc npc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);

        if (!npc.IsAlive
            || !npc.IsPresent
            || npc.CurrentAction.Kind != ActionKind.Sleep
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            return null;
        }

        var beds = room.Fixtures
            .Where(fixture => fixture.Type is FixtureType.Bed or FixtureType.MedicalBed)
            .OrderBy(fixture => fixture.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(fixture => fixture.X)
            .ThenBy(fixture => fixture.Y)
            .ToList();

        if (beds.Count == 0)
        {
            return null;
        }

        // Stable ordering preserves seeded roster order for duplicate names.
        // Do not order by Npc.Id: it is a runtime Guid and must not affect replay.
        var sleepers = state.Crew
            .Where(other =>
                other.IsAlive
                && other.IsPresent
                && other.CurrentAction.Kind == ActionKind.Sleep
                && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A sleeper already physically at a bed keeps it. Remaining sleepers
        // are assigned to the first free beds in deterministic order.
        var assignment = new Dictionary<Npc, int>();
        var claimed = new bool[beds.Count];

        for (var bedIndex = 0; bedIndex < beds.Count; bedIndex++)
        {
            var occupant = sleepers.FirstOrDefault(other =>
                !assignment.ContainsKey(other)
                && LocalMovementSystem.IsAtInteractionPoint(room, other, beds[bedIndex]));
            if (occupant is null)
                continue;

            assignment[occupant] = bedIndex;
            claimed[bedIndex] = true;
        }

        foreach (var sleeper in sleepers)
        {
            if (assignment.ContainsKey(sleeper))
                continue;

            var freeIndex = Array.FindIndex(claimed, isClaimed => !isClaimed);
            if (freeIndex < 0)
                break;

            assignment[sleeper] = freeIndex;
            claimed[freeIndex] = true;
        }

        return assignment.TryGetValue(npc, out var assignedIndex)
            ? beds[assignedIndex]
            : null;
    }
}
