using Overseer.Domain;
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
            marcus.RoutineUntil = TimeSpan.Zero;
            emma.RoutineUntil = TimeSpan.Zero;
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

    [Fact]
    public void MutualRelationshipAndNeedCanProduceConsensualPrivateTime()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var felix = state.Crew.Single(npc => npc.Name == "Felix Ward");

        sarah.CurrentRoomId = "quarters";
        felix.CurrentRoomId = "quarters";
        sarah.IntimacyNeed = 80;
        felix.IntimacyNeed = 80;
        sarah.RoutineUntil = TimeSpan.Zero;
        felix.RoutineUntil = TimeSpan.Zero;

        state.Elapsed = TimeSpan.FromMinutes(5);
        new SocialSimulationSystem().Tick(state);

        Assert.Equal(ActionKind.Intimacy, sarah.CurrentAction.Kind);
        Assert.Equal(ActionKind.Intimacy, felix.CurrentAction.Kind);
        Assert.Equal(felix.Name, sarah.CurrentAction.TargetId);
        Assert.Equal(sarah.Name, felix.CurrentAction.TargetId);
        Assert.NotNull(sarah.Bubble);
        Assert.NotNull(felix.Bubble);
        Assert.Equal(NpcBubbleKind.Speech, sarah.Bubble!.Kind);
        Assert.Equal(NpcBubbleKind.Speech, felix.Bubble!.Kind);
    }

    [Fact]
    public void ConversationCreatesVisibleSpeechBubbles()
    {
        var state = FacilitySeeder.CreateDefault();

        foreach (var npc in state.Crew)
        {
            if (npc.Name is not ("Nadia Okafor" or "David Hale"))
            {
                npc.Health = 0;
            }
        }

        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        nadia.CurrentRoomId = "lounge";
        david.CurrentRoomId = "lounge";
        nadia.SocialNeed = 100;
        david.SocialNeed = 100;

        var system = new SocialSimulationSystem();

        for (var minute = 5; minute <= 500 && nadia.Relationships[david.Name].Conversations == 0; minute += 5)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            nadia.RoutineUntil = TimeSpan.Zero;
            david.RoutineUntil = TimeSpan.Zero;
            system.Tick(state);
        }

        Assert.True(nadia.Relationships[david.Name].Conversations > 0);
        Assert.NotNull(nadia.Bubble);
        Assert.NotNull(david.Bubble);
        Assert.Equal(NpcBubbleKind.Speech, nadia.Bubble!.Kind);
        Assert.Equal(NpcBubbleKind.Speech, david.Bubble!.Kind);
    }
}
