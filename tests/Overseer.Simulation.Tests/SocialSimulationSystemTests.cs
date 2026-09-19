using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class SocialSimulationSystemTests
{
    [Fact]
    public void ExtremeConflict_CanEscalateToADeathWithoutScriptedQuestLogic()
    {
        var state = FacilitySeeder.CreateDefault();

        foreach (var npc in state.Crew)
        {
            if (npc.Name is not ("Marcus Reed" or "Emma Voss"))
            {
                npc.Health = 0;
            }
        }

        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        marcus.CurrentRoomId = "storage";
        emma.CurrentRoomId = "storage";
        marcus.Stress = 100;
        marcus.Relationships[emma.Name].Resentment = 100;
        emma.Health = 40;

        var system = new SocialSimulationSystem();

        for (var minute = 5; minute <= 2000 && emma.IsAlive; minute += 5)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            system.Tick(state);
        }

        Assert.False(emma.IsAlive);
        Assert.NotNull(emma.CauseOfDeath);
        Assert.Contains("Marcus Reed", emma.CauseOfDeath);
        Assert.Contains(
            state.EventLog,
            entry => entry.Contains("killed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeceasedCrewDoNotParticipateInSocialInteractions()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        marcus.CurrentRoomId = "reactor";
        emma.CurrentRoomId = "reactor";
        marcus.Health = 0;
        var before = emma.Relationships[marcus.Name].Conversations;

        state.Elapsed = TimeSpan.FromMinutes(5);
        new SocialSimulationSystem().Tick(state);

        Assert.Equal(before, emma.Relationships[marcus.Name].Conversations);
    }
}
