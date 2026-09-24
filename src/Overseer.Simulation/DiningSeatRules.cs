using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #90: where a person who has chosen to eat actually sits. Seats
/// (chairs, and sofa places in the recreation room) are capacity-1. Whoever
/// already sits in one keeps it, and other eaters in the room take the free
/// seats in a deterministic order. Food lives in the galley, so eating
/// anywhere else means collecting a portion there first and carrying it. C#
/// never decides whether or where someone eats, only where their body goes,
/// what they carry and what eating there costs.
/// </summary>
public static class DiningSeatRules
{
    /// <summary>Stress relief per minute for eating while seated.</summary>
    public const double SeatedStressReliefPerMinute = 0.06;

    /// <summary>Stress per minute for eating on your feet because every chair is taken.</summary>
    public const double StandingStressPerMinute = 0.04;

    /// <summary>
    /// Galley stock a person collects to eat elsewhere: one meal, the same
    /// unit <see cref="StationStores.HasMeal"/> requires. Taking only what one
    /// sitting eats keeps food in the galley for everyone else.
    /// </summary>
    public const double CarriedMealSize = 1;

    /// <summary>
    /// Rooms a carried meal may be eaten in besides the galley. Medical is
    /// deliberately a valid mind-chosen destination: its real medical beds act
    /// as bedside meal places, but only when they are physically free.
    /// </summary>
    public static bool IsAwayDiningRoom(Room room) =>
        room.Type is RoomType.Recreation or RoomType.CrewQuarters or RoomType.Medical;

    /// <summary>
    /// Where an <c>Eat</c> intent is eaten: its target when that names a
    /// recreation room, crew quarters or medical bay, otherwise the galley.
    /// </summary>
    public static string DiningRoomFor(GameState state, string? targetId) =>
        targetId is not null
        && state.Facility.Rooms.TryGetValue(targetId, out var room)
        && IsAwayDiningRoom(room)
            ? room.Id
            : GalleyId(state);

    public static string GalleyId(GameState state) =>
        state.Facility.Rooms.Values
            .Where(room => room.Type == RoomType.Kitchen)
            .Select(room => room.Id)
            .OrderBy(id => id.Equals("kitchen", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? "kitchen";

    /// <summary>
    /// A person in the galley collects one meal to carry elsewhere. Fails when
    /// there is no prepared meal to take, they already carry one, or they are
    /// not in the galley.
    /// </summary>
    public static bool TryCollectMeal(GameState state, Npc npc)
    {
        if (npc.CarriedMealPortion > 0
            || !state.Stores.HasMeal
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room)
            || room.Type != RoomType.Kitchen)
        {
            return false;
        }

        state.Stores.Meals -= CarriedMealSize;
        npc.CarriedMealPortion = CarriedMealSize;
        return true;
    }

    /// <summary>
    /// The shared fallback-mind choice of where to eat: carry the meal to the
    /// recreation room when every galley seat is taken and it has a free one.
    /// A person already seated in the galley stays. Null means the galley.
    /// </summary>
    public static string? FallbackDiningTarget(GameState state, Npc npc)
    {
        // Already eating a carried meal away from the galley: stay put.
        if (IsEating(npc)
            && npc.CarriedMealPortion > 0
            && state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var current)
            && IsAwayDiningRoom(current))
        {
            return current.Id;
        }

        if (npc.CarriedMealPortion <= 0 && !state.Stores.HasMeal)
        {
            return null;
        }

        if (!state.Facility.Rooms.TryGetValue(GalleyId(state), out var galley)
            || SeatedAt(state, npc) is not null)
        {
            return null;
        }

        var (galleyFree, galleyTotal) = Availability(state, galley);
        if (galleyTotal == 0 || galleyFree > 0)
        {
            return null;
        }

        var reachable = new NavigationSystem().ReachableRoomsForCrew(state, npc, npc.CurrentRoomId);
        return state.Facility.Rooms.Values
            .Where(room => room.Type == RoomType.Recreation
                && reachable.Contains(room.Id)
                && Availability(state, room).Free > 0)
            .OrderBy(room => room.Id, StringComparer.OrdinalIgnoreCase)
            .Select(room => room.Id)
            .FirstOrDefault();
    }

    public static bool IsEating(Npc npc) =>
        npc.IsAlive && npc.IsPresent && npc.CurrentAction.Kind == ActionKind.Eat;

    /// <summary>The seat this eater is sitting in, or null.</summary>
    public static RoomFixture? SeatedAt(GameState state, Npc npc)
    {
        if (!IsEating(npc)
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            return null;
        }

        return Seats(room).FirstOrDefault(chair => OccupantOf(state, room, chair)?.Id == npc.Id);
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

        var chairs = Seats(room);
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
        && Seats(room).Count > 0
        && SeatFor(state, npc) is null;

    /// <summary>Seats in the room, and how many of them nobody is eating in.</summary>
    public static (int Free, int Total) Availability(GameState state, Room room)
    {
        var seats = Seats(room);
        return (seats.Count(seat => OccupantOf(state, room, seat) is null), seats.Count);
    }

    private static List<RoomFixture> Seats(Room room) =>
        room.Fixtures
            .Where(fixture =>
                fixture.Type is FixtureType.Chair or FixtureType.Sofa
                || (room.Type == RoomType.Medical && fixture.Type == FixtureType.MedicalBed))
            .ToList();

    private static IEnumerable<Npc> EatersIn(GameState state, Room room) =>
        state.Crew
            .Where(other =>
                IsEating(other)
                && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
            // LINQ ordering is stable, so duplicate names retain seeded roster
            // order. Runtime Guid IDs must never arbitrate simulation outcomes.
            .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase);

    private static Npc? OccupantOf(GameState state, Room room, RoomFixture seat)
    {
        // A medical bed is a real scarce physical resource even when the person
        // using it is resting rather than eating. Bedside dining must not route
        // an eater onto a patient who is already lying in that bed.
        if (seat.Type == FixtureType.MedicalBed)
        {
            var bedOccupant = state.Crew
                .Where(other =>
                    other.IsAlive
                    && other.IsPresent
                    && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(other => BedUseRules.IsPhysicallyAt(room, other, seat));

            if (bedOccupant is not null)
                return bedOccupant;
        }

        return EatersIn(state, room)
            .FirstOrDefault(eater => LocalMovementSystem.IsAtInteractionPoint(room, eater, seat));
    }
}
