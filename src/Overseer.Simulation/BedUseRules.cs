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

        var sleepers = state.Crew
            .Where(other =>
                other.IsAlive
                && other.IsPresent
                && other.CurrentAction.Kind == ActionKind.Sleep
                && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
            // OrderBy is stable, so duplicate names retain deterministic
            // seeded roster order. Runtime Guid IDs must not arbitrate beds.
            .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var assignments = new Dictionary<Guid, RoomFixture>();
        var claimed = new HashSet<RoomFixture>(ReferenceEqualityComparer.Instance);

        // Preserve a sleeper who is already physically using a free bed. This
        // keeps bed-use history meaningful and prevents assignment churn when
        // somebody is already settled. If two people overlap one bed, stable
        // crew order lets only one keep it.
        foreach (var sleeper in sleepers)
        {
            var occupied = beds.FirstOrDefault(bed =>
                !claimed.Contains(bed) && IsPhysicallyAt(room, sleeper, bed));
            if (occupied is null)
            {
                continue;
            }

            assignments[sleeper.Id] = occupied;
            claimed.Add(occupied);
        }

        // Everyone else receives the next free real bed. Capacity is physical:
        // once all beds are claimed, additional sleepers have no assigned bed.
        foreach (var sleeper in sleepers)
        {
            if (assignments.ContainsKey(sleeper.Id))
            {
                continue;
            }

            var available = beds.FirstOrDefault(bed => !claimed.Contains(bed));
            if (available is null)
            {
                continue;
            }

            assignments[sleeper.Id] = available;
            claimed.Add(available);
        }

        return assignments.GetValueOrDefault(npc.Id);
    }

    internal static bool IsPhysicallyAt(Room room, Npc npc, RoomFixture bed)
    {
        if (LocalMovementSystem.IsAtInteractionPoint(room, npc, bed))
        {
            return true;
        }

        var nearestX = Math.Clamp(
            npc.PositionX,
            bed.X - (bed.Width / 2),
            bed.X + (bed.Width / 2));
        var nearestY = Math.Clamp(
            npc.PositionY,
            bed.Y - (bed.Height / 2),
            bed.Y + (bed.Height / 2));
        var dx = (npc.PositionX - nearestX) / 100d * room.MapWidth;
        var dy = (npc.PositionY - nearestY) / 100d * room.MapHeight;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= 0.10;
    }

}
