using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class ConversationTopicSystemTests
{
    // Topic candidates are weighted in a fixed order with small talk first, so a
    // roll near 1 selects the last eligible topic.
    private const double LastTopicRoll = 0.99;

    [Fact]
    public void Gossip_ShiftsTheListenersViewOfTheSubjectByTrust()
    {
        var (state, david, emma, marcus) = Trio();
        david.Relationships[marcus.Name].Resentment = 70;
        emma.Relationships[david.Name].Trust = 80;
        var before = emma.Relationships[marcus.Name].Resentment;

        var exchange = ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);

        Assert.Equal(ConversationTopic.Gossip, exchange.Topic);
        Assert.Contains("Marcus", exchange.Opening, StringComparison.Ordinal);
        Assert.True(emma.Relationships[marcus.Name].Resentment > before);
        Assert.Contains(emma.Memories, memory => memory.Description.Contains("complained to me about Marcus Reed", StringComparison.Ordinal));
        Assert.Contains("complains to Emma Voss about Marcus Reed", exchange.LogLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Gossip_LandsHarderBetweenCliqueMates()
    {
        var (outsiderState, outsiderDavid, outsiderEmma, outsiderMarcus) = Trio();
        outsiderDavid.Relationships[outsiderMarcus.Name].Resentment = 70;
        outsiderEmma.Relationships[outsiderDavid.Name].Trust = 60;
        ConversationTopicSystem.Converse(outsiderState, outsiderDavid, outsiderEmma, LastTopicRoll);
        var outsiderShift = outsiderEmma.Relationships[outsiderMarcus.Name].Resentment;

        var (state, david, emma, marcus) = Trio();
        david.Relationships[marcus.Name].Resentment = 70;
        emma.Relationships[david.Name].Trust = 60;
        david.CliqueId = 0;
        emma.CliqueId = 0;
        ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);
        var cliqueShift = emma.Relationships[marcus.Name].Resentment;

        Assert.True(outsiderShift > 0);
        Assert.Equal(outsiderShift * ConversationTopicSystem.CliqueGossipMultiplier, cliqueShift, 6);
    }

    [Fact]
    public void Gossip_AboutAFriendIsResistedAndCostsTheGossiper()
    {
        var (state, david, emma, marcus) = Trio();
        david.Relationships[marcus.Name].Resentment = 70;
        emma.Relationships[marcus.Name].Affinity = 85;
        var resentmentOfSubject = emma.Relationships[marcus.Name].Resentment;
        var resentmentOfGossiper = emma.Relationships[david.Name].Resentment;

        var exchange = ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);

        Assert.Equal("That's not fair to Marcus.", exchange.Reply);
        Assert.Equal(resentmentOfSubject, emma.Relationships[marcus.Name].Resentment);
        Assert.True(emma.Relationships[david.Name].Resentment > resentmentOfGossiper);
    }

    [Fact]
    public void OverseerDoubts_AreVoicedButNeverEditTheListenersSuspicion()
    {
        var (state, david, emma, _) = Trio();
        david.OverseerSuspicion = 80;
        emma.OverseerSuspicion = 50;

        var exchange = ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);

        Assert.Equal(ConversationTopic.OverseerDoubt, exchange.Topic);
        Assert.Equal("I've noticed it too.", exchange.Reply);
        Assert.Equal(50, emma.OverseerSuspicion);
        Assert.Contains(emma.Memories, memory => memory.Description.Contains("don't trust Overseer", StringComparison.Ordinal));
    }

    [Fact]
    public void News_IsPassedOnButNeverAboutTheListener()
    {
        var (state, david, emma, _) = Trio();
        state.Elapsed = TimeSpan.FromHours(2);
        david.Memories.Add(new Memory($"Emma Voss has missed an expected check-in.", state.Elapsed, 0.9));

        var aboutListener = ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);
        Assert.NotEqual(ConversationTopic.News, aboutListener.Topic);

        david.Memories.Add(new Memory("Found blood near the Reactor hatch.", state.Elapsed, 0.8));
        var news = ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);

        Assert.Equal(ConversationTopic.News, news.Topic);
        Assert.Contains(emma.Memories, memory => memory.Description == "David Hale told me: Found blood near the Reactor hatch.");

        // Hearing it again adds nothing.
        var memoriesBefore = emma.Memories.Count;
        ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);
        Assert.Equal(memoriesBefore, emma.Memories.Count);
    }

    [Fact]
    public void News_NeverSelectsASensitiveMemoryAsAutomaticBackgroundChatter()
    {
        // Owner idea #14 (secrets): a sensitive memory must never leak
        // through C#'s own automatic News selection, however newsworthy it
        // would otherwise look — disclosure has to be a deliberate choice.
        var (state, david, emma, _) = Trio();
        state.Elapsed = TimeSpan.FromHours(2);
        david.Memories.Add(new Memory(
            "Witnessed Marcus Reed hide a personal multitool.",
            state.Elapsed,
            0.9,
            IsSensitive: true));

        var exchange = ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);

        Assert.NotEqual(ConversationTopic.News, exchange.Topic);
        Assert.DoesNotContain(emma.Memories, memory => memory.Description.Contains("hide a personal multitool", StringComparison.Ordinal));
    }

    [Fact]
    public void News_ARetoldMemoryIsMarkedHopOneAndRetellingItAgainDegradesIntoHedgedLanguage()
    {
        var (state, david, emma, _) = Trio();
        var listenerTwo = state.Crew.First(npc =>
            npc.Name != david.Name
            && npc.Name != emma.Name
            && !"Found blood near the Reactor hatch.".Contains(npc.Name, StringComparison.Ordinal));
        state.Elapsed = TimeSpan.FromHours(2);
        david.Memories.Add(new Memory("Found blood near the Reactor hatch.", state.Elapsed, 0.9));

        ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);
        Assert.Contains(
            emma.Memories,
            memory => memory.Description == "David Hale told me: Found blood near the Reactor hatch."
                && memory.RumourHopCount == 1);

        state.Elapsed += TimeSpan.FromMinutes(30);
        var secondHop = ConversationTopicSystem.Converse(state, emma, listenerTwo, LastTopicRoll);

        Assert.Equal(ConversationTopic.News, secondHop.Topic);
        Assert.Contains(
            listenerTwo.Memories,
            memory => memory.Description == "Emma Voss thinks: Found blood near the Reactor hatch."
                && memory.RumourHopCount == 2);
    }

    [Fact]
    public void News_RetoldAThirdTimeDropsAllSpecificContentInsteadOfNestingFurther()
    {
        var (state, david, emma, _) = Trio();
        state.Elapsed = TimeSpan.FromHours(2);
        david.Memories.Add(new Memory("Found blood near the Reactor hatch.", state.Elapsed, 0.9, RumourHopCount: 2));

        var exchange = ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);

        Assert.Equal(ConversationTopic.News, exchange.Topic);
        Assert.Contains(
            emma.Memories,
            memory => memory.Description
                    == "David Hale mentioned hearing some rumour about it, but couldn't say exactly what."
                && memory.RumourHopCount == 3);
        Assert.DoesNotContain("Reactor", exchange.LogLine, StringComparison.Ordinal);
    }

    [Fact]
    public void News_HopCountIsCappedRatherThanGrowingUnbounded()
    {
        var (state, david, emma, _) = Trio();
        state.Elapsed = TimeSpan.FromHours(2);
        david.Memories.Add(new Memory("Found blood near the Reactor hatch.", state.Elapsed, 0.9, RumourHopCount: 10));

        ConversationTopicSystem.Converse(state, david, emma, LastTopicRoll);

        var retold = Assert.Single(emma.Memories);
        Assert.Equal(3, retold.RumourHopCount);
    }

    [Fact]
    public void Arguments_NameTheirCause()
    {
        var (_, david, emma, _) = Trio();
        david.OverseerSuspicion = 70;
        emma.OverseerSuspicion = 10;

        var (opening, reply, topic) = ConversationTopicSystem.Argument(
            david,
            emma,
            david.Relationships[emma.Name],
            0.2);

        Assert.Equal("Overseer", topic);
        Assert.Equal("You're really defending that machine?", opening);
        Assert.Equal("And you're being paranoid.", reply);
    }

    [Fact]
    public void CrewSharingAMealInTheKitchen_StartTalking()
    {
        var (state, david, emma, _) = Trio();
        foreach (var npc in state.Crew.Where(npc => npc.Id != david.Id && npc.Id != emma.Id))
        {
            npc.Health = 0;
        }

        david.CurrentRoomId = "kitchen";
        emma.CurrentRoomId = "kitchen";
        david.SocialNeed = 10;
        emma.SocialNeed = 10;
        var system = new SocialSimulationSystem();

        for (var minute = 1; minute <= 240 && david.Relationships[emma.Name].Conversations == 0; minute++)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            david.RoutineUntil = TimeSpan.Zero;
            emma.RoutineUntil = TimeSpan.Zero;
            system.Tick(state);
        }

        Assert.True(david.Relationships[emma.Name].Conversations > 0);
    }

    private static (GameState State, Npc David, Npc Emma, Npc Marcus) Trio()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        foreach (var npc in state.Crew)
        {
            npc.Stress = 10;
            npc.Fatigue = 10;
            npc.Hunger = 10;
            npc.OverseerSuspicion = 0;
            npc.Memories.Clear();

            foreach (var relationship in npc.Relationships.Values)
            {
                relationship.Affinity = 50;
                relationship.Resentment = 0;
                relationship.Trust = 50;
            }
        }

        return (state, david, emma, marcus);
    }
}
