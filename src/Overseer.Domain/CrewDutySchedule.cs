namespace Overseer.Domain;

public enum CrewRoutinePhase
{
    Sleep,
    Duty,
    Meal,
    Recreation
}

/// <summary>
/// Shared deterministic station-day schedule. T+00:00 maps to 06:00 station
/// local time so a new run begins around shift change rather than at midnight.
/// Security and technicians form a night cohort so a twelve-person station
/// retains meaningful coverage while the day cohort sleeps.
/// </summary>
public static class CrewDutySchedule
{
    private const int DayMinutes = 24 * 60;
    private const int ScenarioStartLocalMinute = 6 * 60;

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

    public static int LocalMinuteOfDay(TimeSpan elapsed)
    {
        var minute = (int)Math.Floor(elapsed.TotalMinutes) + ScenarioStartLocalMinute;
        return ((minute % DayMinutes) + DayMinutes) % DayMinutes;
    }

    public static bool IsNightShift(Npc npc) =>
        npc.Role is CrewRole.Security or CrewRole.Technician;

    public static bool IsSleepWindow(Npc npc, TimeSpan elapsed)
    {
        var minute = LocalMinuteOfDay(elapsed);
        if (IsNightShift(npc))
            return minute >= 10 * 60 && minute < 18 * 60;

        return minute >= 22 * 60 || minute < 6 * 60;
    }

    public static int MinutesUntilWake(Npc npc, TimeSpan elapsed)
    {
        var minute = LocalMinuteOfDay(elapsed);
        var wake = IsNightShift(npc) ? 18 * 60 : 6 * 60;
        return (wake - minute + DayMinutes) % DayMinutes;
    }

    public static CrewRoutinePhase PhaseFor(Npc npc, TimeSpan elapsed)
    {
        if (IsSleepWindow(npc, elapsed))
            return CrewRoutinePhase.Sleep;

        var minute = LocalMinuteOfDay(elapsed);
        var shifted = IsNightShift(npc)
            ? (minute - (18 * 60) + DayMinutes) % DayMinutes
            : (minute - (6 * 60) + DayMinutes) % DayMinutes;

        if (shifted < 60 || shifted is >= 6 * 60 and < 7 * 60 || shifted is >= 12 * 60 and < 13 * 60)
            return CrewRoutinePhase.Meal;

        if (shifted >= 13 * 60 && shifted < 16 * 60)
            return CrewRoutinePhase.Recreation;

        return CrewRoutinePhase.Duty;
    }

    public static string ExpectedDutyRoomId(CrewRole role, TimeSpan elapsed)
    {
        var route = WorkRoutes[role];
        var minute = Math.Max(0, (int)Math.Floor(elapsed.TotalMinutes));
        var phase = ((minute / 30) + (int)role) % route.Length;
        return route[phase];
    }
}
