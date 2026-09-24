using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #92: the recreation room offers distinct physical activities,
/// each tied to a real fixture. A mind picks one as its <c>Recreate</c> target;
/// C# decides where the body goes, whether the fixture works and what doing
/// it is worth. An unnamed <c>Recreate</c> keeps the generic "unwind" rates.
/// </summary>
public static class RecreationActivityRules
{
    public const string WatchTv = "watch-tv";
    public const string PlayGames = "play-games";
    public const string Read = "read";

    /// <summary>Recreation need relief per minute for an unnamed break.</summary>
    public const double GenericReliefPerMinute = 1.45;

    /// <summary>Social need eased per minute while watching with someone else.</summary>
    public const double SharedViewingSocialReliefPerMinute = 0.4;

    public sealed record Activity(
        string Id,
        string Label,
        string Doing,
        FixtureType Fixture,
        bool NeedsPower,
        double RecreationReliefPerMinute,
        double StressReliefPerMinute);

    public static IReadOnlyList<Activity> All { get; } =
    [
        new(WatchTv, "watch TV", "watching TV", FixtureType.Television, true, 1.45, 0.02),
        new(PlayGames, "play games on the console", "playing games", FixtureType.RecreationConsole, true, 1.8, 0),
        new(Read, "read in the reading chair", "reading", FixtureType.Chair, false, 1.1, 0.05)
    ];

    public static Activity? Find(string? id) =>
        id is null
            ? null
            : All.FirstOrDefault(activity => activity.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The named activity this person is recreating with, if any.</summary>
    public static Activity? Current(Npc npc) =>
        npc.CurrentAction.Kind == ActionKind.Recreate
            ? Find(npc.CurrentAction.TargetId)
            : null;

    public static RoomFixture? FixtureFor(Room room, Activity activity) =>
        room.Fixtures.FirstOrDefault(fixture => fixture.Type == activity.Fixture);

    /// <summary>
    /// Whether the activity can be done in this room right now: its fixture is
    /// here, and anything electronic has power.
    /// </summary>
    public static bool IsAvailable(Room room, Activity activity) =>
        FixtureFor(room, activity) is not null
        && (!activity.NeedsPower || room.IsPowered);

    /// <summary>People in this room currently doing this activity.</summary>
    public static IEnumerable<Npc> DoingIn(GameState state, Room room, Activity activity) =>
        state.Crew
            .Where(other =>
                other.IsAlive
                && other.IsPresent
                && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                && Current(other)?.Id == activity.Id)
            .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(other => other.Id);

    /// <summary>
    /// Where the body goes for the chosen activity. TV watchers spread across
    /// the sofas in a stable order; the other activities use their fixture.
    /// </summary>
    public static RoomFixture? PlaceFor(GameState state, Room room, Npc npc, Activity activity)
    {
        if (activity.Id != WatchTv)
        {
            return FixtureFor(room, activity);
        }

        var sofas = room.Fixtures.Where(fixture => fixture.Type == FixtureType.Sofa).ToList();
        if (sofas.Count == 0)
        {
            return FixtureFor(room, activity);
        }

        var index = DoingIn(state, room, activity).ToList().FindIndex(other => other.Id == npc.Id);
        return sofas[Math.Max(0, index) % sofas.Count];
    }

    /// <summary>
    /// Recreation need relief per minute right now: the activity's rate while
    /// it is available, nothing while its fixture is missing or dead, and the
    /// generic rate for an unnamed break.
    /// </summary>
    public static double ReliefPerMinute(GameState state, Npc npc)
    {
        if (npc.CurrentAction.Kind != ActionKind.Recreate)
        {
            return 0;
        }

        var activity = Current(npc);
        if (activity is null)
        {
            return GenericReliefPerMinute;
        }

        return state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room)
            && IsAvailable(room, activity)
                ? activity.RecreationReliefPerMinute
                : 0;
    }

    /// <summary>Whether someone else is watching the same TV with this person.</summary>
    public static bool IsWatchingWithOthers(GameState state, Npc npc)
    {
        if (Current(npc) is not { Id: WatchTv } activity
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room)
            || !IsAvailable(room, activity))
        {
            return false;
        }

        return DoingIn(state, room, activity).Any(other => other.Id != npc.Id);
    }

    /// <summary>
    /// The shared fallback-mind (and routine) pick. Nothing works in the dark
    /// but a book; a lonely person joins people already watching TV; a
    /// stressed one reads; otherwise each person's own stable taste decides
    /// between TV and games. Null when the room offers none of them.
    /// </summary>
    public static string? FallbackChoice(GameState state, Npc npc, string roomId)
    {
        if (!state.Facility.Rooms.TryGetValue(roomId, out var room))
        {
            return null;
        }

        var available = All.Where(activity => IsAvailable(room, activity)).ToList();
        if (available.Count == 0)
        {
            return null;
        }

        var tv = available.FirstOrDefault(activity => activity.Id == WatchTv);
        if (tv is not null
            && npc.SocialNeed >= 45
            && DoingIn(state, room, tv).Any(other => other.Id != npc.Id))
        {
            return tv.Id;
        }

        var read = available.FirstOrDefault(activity => activity.Id == Read);
        if (read is not null && (npc.Stress >= 45 || available.Count == 1))
        {
            return read.Id;
        }

        var screens = available.Where(activity => activity.Id != Read).ToList();
        if (screens.Count == 0)
        {
            return read?.Id;
        }

        return screens[StableTaste(npc.Name) % screens.Count].Id;
    }

    private static int StableTaste(string name)
    {
        var hash = 17;
        foreach (var character in name)
        {
            hash = unchecked((hash * 31) + char.ToLowerInvariant(character));
        }

        return hash & int.MaxValue;
    }
}
