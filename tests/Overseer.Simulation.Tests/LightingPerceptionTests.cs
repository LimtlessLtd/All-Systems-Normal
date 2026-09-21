using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class LightingPerceptionTests
{
    // Beyond dark sight (26 x 0.35 = 9.1 map units) but well inside lit sight.
    private const double MidRange = 12;

    [Fact]
    public void DarknessShortensHumanSight()
    {
        var (state, room, observer, target) = Pair(separation: MidRange);

        Assert.True(PerceptionSystem.CanSee(state, observer, target));

        room.LightsOn = false;
        Assert.False(PerceptionSystem.CanSee(state, observer, target));

        // Up close you can still make someone out.
        target.PositionX = observer.PositionX + (4 / room.MapWidth * 100);
        Assert.True(PerceptionSystem.CanSee(state, observer, target));
    }

    [Fact]
    public void UnpoweredRoomsAreDarkToo()
    {
        var (state, room, observer, target) = Pair(separation: MidRange);
        room.IsPowered = false;

        Assert.False(PerceptionSystem.CanSee(state, observer, target));
    }

    [Fact]
    public void AttackInTheDark_IsHeardButNotAttributed()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);

        // Violence needs a short temper; this pair is the existing escalation case.
        var aggressor = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var victim = state.Crew.Single(npc => npc.Name == "Emma Voss");
        var witness = state.Crew.Single(npc => npc.Name == "David Hale");
        foreach (var npc in state.Crew.Where(npc => npc != aggressor && npc != victim && npc != witness))
        {
            npc.Health = 0;
        }

        victim.Health = 100;

        var room = WidestRoom(state);
        foreach (var npc in new[] { aggressor, victim, witness })
        {
            npc.CurrentRoomId = room.Id;
            npc.PositionY = 50;
        }

        aggressor.PositionX = 10;
        victim.PositionX = 10 + (2 / room.MapWidth * 100);
        witness.PositionX = 10 + (MidRange / room.MapWidth * 100);
        room.LightsOn = false;

        aggressor.Stress = 100;
        aggressor.Relationships[victim.Name].Resentment = 100;
        var trustBefore = witness.Relationships[aggressor.Name].Trust;
        var system = new SocialSimulationSystem();

        for (var minute = 1; minute <= 2000 && victim.Health == 100; minute++)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            aggressor.RoutineUntil = TimeSpan.Zero;
            victim.RoutineUntil = TimeSpan.Zero;
            aggressor.NextConversationAt = TimeSpan.Zero;
            victim.NextConversationAt = TimeSpan.Zero;
            system.Tick(state);
        }

        Assert.True(victim.Health < 100, "The attack never happened.");
        Assert.Contains(witness.Memories, memory => memory.Description.Contains("couldn't see who", StringComparison.Ordinal));
        Assert.DoesNotContain(witness.Memories, memory => memory.Description.Contains($"Witnessed {aggressor.Name}", StringComparison.Ordinal));
        Assert.Equal(trustBefore, witness.Relationships[aggressor.Name].Trust);
    }

    private static (GameState State, Room Room, Npc Observer, Npc Target) Pair(double separation)
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var room = WidestRoom(state);
        var observer = state.Crew[0];
        var target = state.Crew[1];

        observer.CurrentRoomId = room.Id;
        target.CurrentRoomId = room.Id;
        observer.PositionX = 10;
        observer.PositionY = 50;
        observer.FacingDegrees = 0;
        target.PositionY = 50;

        // Place the target the requested map distance to the observer's right.
        target.PositionX = observer.PositionX + (separation / room.MapWidth * 100);
        Assert.True(target.PositionX < 95, $"{room.Id} is too narrow for this separation.");
        return (state, room, observer, target);
    }

    // Sight is measured in station map units; use the longest compartment so
    // mid-range separations stay inside one room.
    private static Room WidestRoom(GameState state) =>
        state.Facility.Rooms.Values
            .OrderByDescending(room => room.MapWidth)
            .ThenBy(room => room.Id, StringComparer.Ordinal)
            .First();
}
