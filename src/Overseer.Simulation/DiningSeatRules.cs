using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #90: where a person who has chosen to eat actually sits. Chairs
/// are capacity-1. Whoever already sits in one keeps it, and other eaters in
/// the room take the free chairs in a deterministic order. C# never decides
/// whether someone eats, only where their body goes and what eating there
/// costs.
/// </summary>
public static class DiningSeatRules
{
    /// <summary>Stress relief per minute for eating while seated.</summary>
    public const double SeatedStressReliefPerMinute = 0.06;

    /// <summary>Stress per minute for eating on your feet because every chair is taken.</summary>
    public const double StandingStressPerMinute = 0.04;

    public static bool IsEating(Npc npc) =>
        npc.IsAlive && npc.IsPresent && npc.CurrentAction.Kind == ActionKind.Eat;

    /// <summary>The chair this eater is sitting in, or null.</summary>
    public static RoomFixture? SeatedAt(GameState state, Npc npc)
    {
        if (!IsEating(npc)
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            return null;
        }

        return Chairs(room).FirstOrDefault(chair => OccupantOf(state, room, chair)?.Id == npc.Id);
    }

    /// <summary>
    /// The chair this eater should head for: the one they already sit in,
    /// else a free chair handed out in a stable order among the unseated
    /// eaters here. Null when the room has no free chair for them.
    /// </summary>
    public static RoomFixture? SeatFor(GameState state, Npc npc)
    {
        if (!IsEating(npc)
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            return null;
        }

        var chairs = Chairs(room);
        if (chairs.Count == 0)
        {
            return null;
        }

        var occupants = chairs.ToDictionary(chair => chair, chair => OccupantOf(state, room, chair));
        var own = occupants.FirstOrDefault(pair => pair.Value?.Id == npc.Id).Key;
        if (own is not null)
        {
            return own;
        }

        var seatedIds = occupants.Values.Where(occupant => occupant is not null).Select(occupant => occupant!.Id).ToHashSet();
        var free = chairs.Where(chair => occupants[chair] is null).ToList();
        var waiting = EatersIn(state, room)
            .Where(eater => !seatedIds.Contains(eater.Id))
            .ToList();
        var index = waiting.FindIndex(eater => eater.Id == npc.Id);

        return index >= 0 && index < free.Count ? free[index] : null;
    }

    /// <summary>Eating on your feet: no chair here is free for this eater.</summary>
    public static bool IsEatingStanding(GameState state, Npc npc) =>
        IsEating(npc)
        && state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room)
        && Chairs(room).Count > 0
        && SeatFor(state, npc) is null;

    /// <summary>Chairs in the room, and how many of them nobody is eating in.</summary>
    public static (int Free, int Total) Availability(GameState state, Room room)
    {
        var chairs = Chairs(room);
        return (chairs.Count(chair => OccupantOf(state, room, chair) is null), chairs.Count);
    }

    private static List<RoomFixture> Chairs(Room room) =>
        room.Fixtures.Where(fixture => fixture.Type == FixtureType.Chair).ToList();

    private static IEnumerable<Npc> EatersIn(GameState state, Room room) =>
        state.Crew
            .Where(other =>
                IsEating(other)
                && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(other => other.Id);

    private static Npc? OccupantOf(GameState state, Room room, RoomFixture chair) =>
        EatersIn(state, room)
            .FirstOrDefault(eater => LocalMovementSystem.IsAtInteractionPoint(room, eater, chair));
}
