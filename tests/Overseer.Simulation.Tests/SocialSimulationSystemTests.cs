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
        var pacing = new ConversationPacingSystem();
        pacing.Tick(state);

        var immediate = new[] { sarah, felix }.Count(npc => npc.Bubble is not null);
        var queued = sarah.PendingBubbles.Count + felix.PendingBubbles.Count;

        Assert.Equal(1, immediate);
        Assert.Equal(1, queued);
        Assert.Contains(state.AudioCues, cue => cue.Kind == AudioCueKind.Speech);
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

        var pacing = new ConversationPacingSystem();
        pacing.Tick(state);

        Assert.Equal(1, new[] { nadia, david }.Count(npc => npc.Bubble is not null));
        Assert.Equal(1, nadia.PendingBubbles.Count + david.PendingBubbles.Count);
        Assert.True(nadia.NextConversationAt > state.Elapsed);
        Assert.True(david.NextConversationAt > state.Elapsed);
    }

    [Fact]
    public void ScheduledReply_AppearsOnLaterTurnInsteadOfAtTheSameTime()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var felix = state.Crew.Single(npc => npc.Name == "Felix Ward");

        ConversationPacingSystem.Schedule(
            sarah,
            "First line.",
            NpcBubbleKind.Speech,
            TimeSpan.FromMinutes(10),
            1);
        ConversationPacingSystem.Schedule(
            felix,
            "Reply.",
            NpcBubbleKind.Speech,
            TimeSpan.FromMinutes(12),
            1);

        var pacing = new ConversationPacingSystem();

        state.Elapsed = TimeSpan.FromMinutes(10);
        pacing.Tick(state);

        Assert.Equal("First line.", sarah.Bubble?.Text);
        Assert.Null(felix.Bubble);

        state.Elapsed = TimeSpan.FromMinutes(12);
        pacing.Tick(state);

        Assert.Equal("Reply.", felix.Bubble?.Text);
        Assert.Equal(
            2,
            state.AudioCues.Count(cue => cue.Kind == AudioCueKind.Speech));
    }
}
