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
    public const string PlayCards = "play-cards";

    /// <summary>Recreation need relief per minute for an unnamed break.</summary>
    public const double GenericReliefPerMinute = 1.45;

    /// <summary>Social need eased per minute while watching with someone else.</summary>
    public const double SharedViewingSocialReliefPerMinute = 0.4;

    /// <summary>Social need eased per minute while playing cards with others.</summary>
    public const double CardGameSocialReliefPerMinute = 0.7;

    /// <summary>
    /// Affinity each card player gains per minute toward each other player.
    /// About three points an hour: a shared game warms people slowly.
    /// </summary>
    public const double CardGameAffinityPerMinute = 0.05;

    public sealed record Activity(
        string Id,
        string Label,
        string Doing,
        FixtureType Fixture,
        bool NeedsPower,
        double RecreationReliefPerMinute,
        double StressReliefPerMinute,
        int MinimumPlayers = 1);

    public static IReadOnlyList<Activity> All { get; } =
    [
        new(WatchTv, "watch TV", "watching TV", FixtureType.Television, true, 1.45, 0.02),
        new(PlayGames, "play games on the console", "playing games", FixtureType.RecreationConsole, true, 1.8, 0),
        new(Read, "read in the reading chair", "reading", FixtureType.Chair, false, 1.1, 0.05),
        // A game needs a partner: alone at the table it is no break at all.
        new(PlayCards, "play cards at the table", "playing cards", FixtureType.Table, false, 1.6, 0.03, MinimumPlayers: 2)
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
        var seats = activity.Id switch
        {
            WatchTv => room.Fixtures.Where(fixture => fixture.Type == FixtureType.Sofa).ToList(),
            PlayCards when FixtureFor(room, activity) is { } table => SeatsAt(room, table),
            _ => []
        };

        if (seats.Count == 0)
        {
            return FixtureFor(room, activity);
        }

        var index = DoingIn(state, room, activity).ToList().FindIndex(other => other.Id == npc.Id);
        return seats[Math.Max(0, index) % seats.Count];
    }

    /// <summary>
    /// The chairs and sofas pulled up to this table: within
    /// <see cref="SeatReach"/> of its edge, the same reach the layout pass
    /// uses to keep a table's seats with it.
    /// </summary>
    public static List<RoomFixture> SeatsAt(Room room, RoomFixture table) =>
        room.Fixtures
            .Where(fixture =>
                fixture.Type is FixtureType.Chair or FixtureType.Sofa
                && EdgeGap(fixture, table) <= SeatReach)
            .ToList();

    private const double SeatReach = 4;

    private static double EdgeGap(RoomFixture first, RoomFixture second)
    {
        var dx = Math.Max(0, Math.Abs(first.X - second.X) - ((first.Width + second.Width) / 2));
        var dy = Math.Max(0, Math.Abs(first.Y - second.Y) - ((first.Height + second.Height) / 2));
        return Math.Max(dx, dy);
    }

    /// <summary>
    /// Whether enough people are doing this activity here for it to work:
    /// always for a solo activity, and for a card game only once a second
    /// player is at the table.
    /// </summary>
    public static bool HasEnoughPlayers(GameState state, Room room, Activity activity) =>
        activity.MinimumPlayers <= 1
        || DoingIn(state, room, activity).Count() >= activity.MinimumPlayers;

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
            && HasEnoughPlayers(state, room, activity)
                ? activity.RecreationReliefPerMinute
                : 0;
    }

    /// <summary>
    /// Social need eased per minute by doing this activity with other people
    /// here: watching TV together or a card game. Zero alone, for solo
    /// activities, and while the activity does not work.
    /// </summary>
    public static double SocialReliefPerMinute(GameState state, Npc npc)
    {
        if (Current(npc) is not { } activity
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room)
            || !IsAvailable(room, activity)
            || !DoingIn(state, room, activity).Any(other => other.Id != npc.Id))
        {
            return 0;
        }

        return activity.Id switch
        {
            WatchTv => SharedViewingSocialReliefPerMinute,
            PlayCards => CardGameSocialReliefPerMinute,
            _ => 0
        };
    }

    /// <summary>The other people at this person's card game, or none.</summary>
    public static IEnumerable<Npc> CardPartners(GameState state, Npc npc) =>
        Current(npc) is { Id: PlayCards } activity
        && state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room)
        && IsAvailable(room, activity)
            ? DoingIn(state, room, activity).Where(other => other.Id != npc.Id)
            : [];

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
            && npc.SocialNeed >= LonelyAt
            && DoingIn(state, room, tv).Any(other => other.Id != npc.Id))
        {
            return tv.Id;
        }

        // A lonely person also joins a card game someone has already started,
        // or starts one when someone else in the room is lonely too (who then
        // joins it). Nobody starts one alone: without a partner it is no
        // break at all.
        var cards = available.FirstOrDefault(activity => activity.Id == PlayCards);
        if (cards is not null
            && npc.SocialNeed >= LonelyAt
            && (DoingIn(state, room, cards).Any(other => other.Id != npc.Id)
                || state.Crew.Any(other =>
                    other.Id != npc.Id
                    && other.IsAlive
                    && other.IsPresent
                    && other.SocialNeed >= LonelyAt
                    && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))))
        {
            return cards.Id;
        }

        var read = available.FirstOrDefault(activity => activity.Id == Read);
        if (read is not null && (npc.Stress >= 45 || available.Count == 1))
        {
            return read.Id;
        }

        var screens = available.Where(activity => activity.Id is WatchTv or PlayGames).ToList();
        if (screens.Count == 0)
        {
            return read?.Id;
        }

        return screens[StableTaste(npc.Name) % screens.Count].Id;
    }

    private const double LonelyAt = 45;

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
