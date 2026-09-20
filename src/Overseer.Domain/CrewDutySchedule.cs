namespace Overseer.Domain;

/// <summary>
/// Shared rough duty schedule used by routine movement and observer knowledge.
/// It describes where a role is normally expected to be, not where a specific
/// person actually is.
/// </summary>
public static class CrewDutySchedule
{
    private static readonly IReadOnlyDictionary<CrewRole, string[]> WorkRoutes =
        new Dictionary<CrewRole, string[]>
        {
            [CrewRole.Commander] =
                ["control", "medical", "hydroponics", "engineering", "kitchen", "lounge", "quarters", "storage"],
            [CrewRole.Engineer] =
                ["engineering", "reactor", "generator", "hydroponics", "control", "storage", "airlock"],
            [CrewRole.Security] =
                ["corridor", "airlock", "storage", "quarters", "hydroponics", "medical", "control", "engineering"],
            [CrewRole.Doctor] =
                ["medical", "quarters", "hydroponics", "kitchen", "control", "storage", "lounge"],
            [CrewRole.Technician] =
                ["generator", "engineering", "storage", "hydroponics", "airlock", "control", "reactor", "medical"],
            [CrewRole.Scientist] =
                ["reactor", "hydroponics", "medical", "control", "storage", "engineering", "lounge", "kitchen"]
        };

    public static IReadOnlyList<string> RouteFor(CrewRole role) => WorkRoutes[role];

    public static string ExpectedDutyRoomId(CrewRole role, TimeSpan elapsed)
    {
        var route = WorkRoutes[role];
        var minute = Math.Max(0, (int)Math.Floor(elapsed.TotalMinutes));
        var phase = ((minute / 30) + (int)role) % route.Length;
        return route[phase];
    }
}
