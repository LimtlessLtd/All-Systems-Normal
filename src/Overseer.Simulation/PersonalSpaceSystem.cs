using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic personal-space preference telemetry. It observes where crew
/// physically spend time; it never decides which fixture an NPC wants to use.
/// </summary>
public static class PersonalSpaceSystem
{
    public static string FixtureKey(string roomId, RoomFixture fixture) =>
        $"{roomId}::{fixture.Type}::{fixture.Label}";

    public static void RecordUse(
        Npc npc,
        string roomId,
        RoomFixture fixture,
        double minutes)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(fixture);

        if (minutes <= 0)
            return;

        var key = FixtureKey(roomId, fixture);
        npc.FixtureUseMinutes[key] =
            npc.FixtureUseMinutes.GetValueOrDefault(key) + minutes;
    }

    public static string? PreferredBedKey(Npc npc) =>
        npc.FixtureUseMinutes
            .Where(pair => pair.Key.Contains("::Bed::", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key)
            .FirstOrDefault();
}
