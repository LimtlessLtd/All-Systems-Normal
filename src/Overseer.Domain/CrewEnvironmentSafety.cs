namespace Overseer.Domain;

/// <summary>
/// Shared human-survivability thresholds used by cognition and simulation
/// presentation. These values describe when a compartment should feel unsafe
/// to an ordinary crew member; they do not choose an NPC's goal.
/// </summary>
public static class CrewEnvironmentSafety
{
    /// <summary>
    /// Life-threatening, not merely unpleasant. The temperature band used to
    /// start at 14C, which made every unheated hallway on the station read as
    /// DANGER — so crew fled the moment they stepped into one, bounced straight
    /// back, and could never cross the station to eat or work. A cold corridor
    /// is uncomfortable and belongs in the MARGINAL band, where it still feeds
    /// RiskScore and crew stress without triggering an evacuation.
    /// </summary>
    public static bool IsDangerous(Room room) =>
        room.OxygenPercent < 19.0
        || room.CarbonDioxidePercent > 1.25
        || room.PressureKpa < 90
        || room.TemperatureC is < 6 or > 38;

    public static bool IsHabitable(Room room) =>
        room.IsPowered
        && room.OxygenPercent >= 19.5
        && room.CarbonDioxidePercent <= 0.8
        && room.PressureKpa >= 95
        && room.TemperatureC is >= 16 and <= 28;

    public static double RiskScore(Room room)
    {
        var score = 0d;

        score += Math.Max(0, 19.5 - room.OxygenPercent) * 4.0;
        score += Math.Max(0, room.CarbonDioxidePercent - 0.8) * 5.0;
        score += Math.Max(0, 95 - room.PressureKpa) * 0.22;

        if (room.TemperatureC < 16)
        {
            score += (16 - room.TemperatureC) * 0.65;
        }
        else if (room.TemperatureC > 28)
        {
            score += (room.TemperatureC - 28) * 0.65;
        }

        if (!room.IsPowered)
        {
            score += 0.75;
        }

        return score;
    }

    public static string Label(Room room) =>
        IsDangerous(room)
            ? "DANGER"
            : IsHabitable(room)
                ? "HABITABLE"
                : "MARGINAL";
}
