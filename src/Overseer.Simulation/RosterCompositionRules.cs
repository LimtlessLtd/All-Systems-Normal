namespace Overseer.Simulation;

/// <summary>
/// How many crew and robots a fresh run starts with.
/// </summary>
/// <param name="CrewCount">Non-prisoner crew in a freshly generated roster.</param>
/// <param name="RobotCount">Maintenance/security robots seeded on the station.</param>
public sealed record RosterComposition(int CrewCount, int RobotCount);

/// <summary>
/// The fresh-run roster-size contract (owner idea #88): every run has between
/// <see cref="MinCrew"/> and <see cref="MaxCrew"/> crew and between
/// <see cref="MinRobots"/> and <see cref="MaxRobots"/> robots, and every
/// crew/robot combination is equally likely. There is no hidden weighting
/// towards the historical twelve-crew, one-robot station.
/// </summary>
public static class RosterCompositionRules
{
    public const int MinCrew = 4;
    public const int MaxCrew = CrewRosterScalingSystem.DefaultTargetSize;
    public const int MinRobots = 1;
    public const int MaxRobots = 4;

    public static int CrewOptions => MaxCrew - MinCrew + 1;
    public static int RobotOptions => MaxRobots - MinRobots + 1;
    public static int CombinationCount => CrewOptions * RobotOptions;

    /// <summary>Every allowed combination, in a stable order.</summary>
    public static IReadOnlyList<RosterComposition> AllCombinations { get; } =
        Enumerable.Range(0, CombinationCount).Select(FromIndex).ToArray();

    /// <summary>
    /// Deterministically picks one combination from a seed. The seed is mixed
    /// before a single uniform draw over the whole combination grid, so crew
    /// and robot counts are not correlated and no combination is favoured.
    /// </summary>
    public static RosterComposition Sample(int seed) =>
        FromIndex((int)(Mix(seed) % (uint)CombinationCount));

    private static RosterComposition FromIndex(int index) =>
        new(MinCrew + (index / RobotOptions), MinRobots + (index % RobotOptions));

    private static uint Mix(int seed)
    {
        unchecked
        {
            // Salted so the composition draw is independent of the roster
            // generator's own per-member draws from the same seed.
            var value = (uint)seed ^ 0xA5C3_1E77u;
            value ^= value >> 16;
            value *= 0x7FEB_352Du;
            value ^= value >> 15;
            value *= 0x846C_A68Bu;
            value ^= value >> 16;
            return value;
        }
    }
}
