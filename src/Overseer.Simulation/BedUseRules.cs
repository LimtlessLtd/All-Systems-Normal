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

        var sleepers = state.Crew
            .Where(other =>
                other.IsAlive
                && other.IsPresent
                && other.CurrentAction.Kind == ActionKind.Sleep
                && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(other => other.Id)
            .ToList();

        var index = sleepers.FindIndex(other => other.Id == npc.Id);
        return index >= 0 && index < beds.Count ? beds[index] : null;
    }
}
