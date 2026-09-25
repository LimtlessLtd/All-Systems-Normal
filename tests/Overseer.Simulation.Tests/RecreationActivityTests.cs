using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #92: the lounge has a real television, and recreation is a
/// choice between physical activities (TV, console games, reading) with their
/// own deterministic consequences.
/// </summary>
public sealed class RecreationActivityTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);

    [Fact]
    public void TheLoungeHasATelevisionAConsoleAndAReadingChair()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var lounge = state.Facility.Rooms["lounge"];

        Assert.Contains(lounge.Fixtures, fixture => fixture.Type == FixtureType.Television && fixture.Label == "Television");
        foreach (var activity in RecreationActivityRules.All.Where(activity => activity.Room == RoomType.Recreation))
        {
            Assert.NotNull(RecreationActivityRules.FixtureFor(lounge, activity));
            Assert.True(RecreationActivityRules.IsAvailable(lounge, activity));
        }
    }

    [Fact]
    public void AChosenActivity_WalksToItsFixture_AndIsWorthItsOwnRate()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = Idle(state, "control");
        npc.RecreationNeed = 80;
        npc.Intent = new NpcIntent(ActionKind.Recreate, RecreationActivityRules.PlayGames, "Play.", "Bored.", 50, "Test", state.Elapsed);

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();
        var lounge = state.Facility.Rooms["lounge"];
        var console = lounge.Fixtures.Single(fixture => fixture.Type == FixtureType.RecreationConsole);
        for (var i = 0; i < 3 * 30; i++)
        {
            intents.Tick(state);
            movement.Tick(state, Tick);
        }

        Assert.Equal("lounge", npc.CurrentRoomId);
        Assert.Equal(ActionKind.Recreate, npc.CurrentAction.Kind);
        Assert.Equal(RecreationActivityRules.PlayGames, npc.CurrentAction.TargetId);
        // Beside the console, wherever the packer put it against a wall.
        Assert.InRange(Math.Abs(npc.PositionX - console.X), 0, (console.Width / 2) + 12);
        Assert.InRange(Math.Abs(npc.PositionY - console.Y), 0, (console.Height / 2) + 12);

        var before = npc.RecreationNeed;
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(before - 1.8, npc.RecreationNeed, 3);
    }

    [Fact]
    public void WithoutPower_TheTvAndConsoleDoNothing_ButABookStillWorks()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var lounge = state.Facility.Rooms["lounge"];
        lounge.IsPowered = false;
        var watcher = Idle(state, "lounge");
        var reader = Idle(state, "lounge", skip: 1);
        watcher.RecreationNeed = 60;
        reader.RecreationNeed = 60;
        reader.Stress = 40;
        watcher.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.WatchTv, "TV.");
        reader.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.Read, "Book.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(watcher.RecreationNeed > 60, "a dead TV is no break at all");
        Assert.Equal(60 - 1.1, reader.RecreationNeed, 3);
        Assert.Contains(reader.StatLog, entry => entry.Cause == "reading" && entry.Delta < 0);
        Assert.DoesNotContain(watcher.StatLog, entry => entry.Cause == "watching TV");
    }

    [Fact]
    public void WatchingTogether_EasesTheSocialNeed_WatchingAloneDoesNot()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var first = Idle(state, "lounge");
        var second = Idle(state, "lounge", skip: 1);
        first.SocialNeed = 50;
        first.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.WatchTv, "TV.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));
        Assert.True(first.SocialNeed > 50);

        second.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.WatchTv, "TV.");
        var social = first.SocialNeed;
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(social - RecreationActivityRules.SharedViewingSocialReliefPerMinute, first.SocialNeed, 3);

        // They spread over the sofas rather than stacking on one.
        var lounge = state.Facility.Rooms["lounge"];
        Assert.NotSame(
            RecreationActivityRules.PlaceFor(state, lounge, first, RecreationActivityRules.All[0]),
            RecreationActivityRules.PlaceFor(state, lounge, second, RecreationActivityRules.All[0]));
    }

    [Fact]
    public void AnUnnamedBreak_KeepsTheGenericRate()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = Idle(state, "lounge");
        npc.RecreationNeed = 60;
        npc.CurrentAction = new NpcAction(ActionKind.Recreate, "lounge", "Unwinding.");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(60 - RecreationActivityRules.GenericReliefPerMinute, npc.RecreationNeed, 3);
    }

    [Fact]
    public void TheSharedFallbackPick_FollowsWhatTheRoomOffersAndHowThePersonFeels()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = Idle(state, "control");
        npc.Stress = 10;
        npc.SocialNeed = 10;

        var calmPick = RecreationActivityRules.FallbackChoice(state, npc, "lounge");
        Assert.Contains(calmPick, new[] { RecreationActivityRules.WatchTv, RecreationActivityRules.PlayGames });
        Assert.Equal(calmPick, RecreationActivityRules.FallbackChoice(state, npc, "lounge"));

        npc.Stress = 60;
        Assert.Equal(RecreationActivityRules.Read, RecreationActivityRules.FallbackChoice(state, npc, "lounge"));

        npc.Stress = 10;
        npc.SocialNeed = 60;
        var watcher = Idle(state, "lounge", skip: 1);
        watcher.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.WatchTv, "TV.");
        Assert.Equal(RecreationActivityRules.WatchTv, RecreationActivityRules.FallbackChoice(state, npc, "lounge"));

        state.Facility.Rooms["lounge"].IsPowered = false;
        Assert.Equal(RecreationActivityRules.Read, RecreationActivityRules.FallbackChoice(state, npc, "lounge"));
    }

    [Fact]
    public async Task BothFallbackMinds_AndTheRoutine_UseTheSharedPick()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Elapsed = TimeSpan.FromHours(4);
        var npc = Idle(state, "lounge");
        npc.Hunger = 5;
        npc.Fatigue = 5;
        npc.HygieneNeed = 5;
        npc.BladderNeed = 5;
        npc.SocialNeed = 5;
        npc.RecreationNeed = 90;
        npc.Stress = 60;

        var server = await new RuleBasedAiDecisionService().DecideAsync(npc, state);
        if (server.Action == ActionKind.Recreate)
        {
            Assert.Equal(RecreationActivityRules.Read, server.TargetId);
        }

        npc.NeedsMindReconsideration = true;
        new BrowserMindSystem().Tick(state);
        if (npc.Intent is { Action: ActionKind.Recreate } browser)
        {
            Assert.Equal(RecreationActivityRules.Read, browser.TargetId);
        }

        Assert.True(
            server.Action == ActionKind.Recreate || npc.Intent?.Action == ActionKind.Recreate,
            $"neither fallback chose to recreate (server {server.Action}, browser {npc.Intent?.Action})");

        npc.Intent = null;
        npc.RoutineUntil = TimeSpan.Zero;
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        new CrewRoutineSystem().Tick(state);
        if (npc.CurrentAction.Kind == ActionKind.Recreate)
        {
            Assert.Equal(RecreationActivityRules.Read, npc.CurrentAction.TargetId);
        }
    }

    [Fact]
    public void CognitionSeesWhatTheLoungeOffers()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = Idle(state, "lounge");

        var prompt = NpcPromptBuilder.Build(npc, state);
        Assert.Contains("RECREATION in ", prompt);
        Assert.Contains("watch-tv (watch TV: free)", prompt);
        Assert.Contains("play-games (play games on the console: free)", prompt);
        Assert.Contains("read (read in the reading chair: free)", prompt);
        Assert.Contains("For Recreate, TargetId is null for a plain break", prompt);

        state.Facility.Rooms["lounge"].IsPowered = false;
        var other = Idle(state, "lounge", skip: 1);
        other.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.Read, "Book.");
        prompt = NpcPromptBuilder.Build(npc, state);
        Assert.Contains("watch-tv (watch TV: no power, so it does nothing)", prompt);
        Assert.Contains("read (read in the reading chair: 1 other already reading)", prompt);

        // Elsewhere, and not wanting a break: no line.
        npc.CurrentRoomId = "control";
        npc.RecreationNeed = 5;
        Assert.DoesNotContain("RECREATION in ", NpcPromptBuilder.Build(npc, state));
    }

    [Fact]
    public void ACardGameNeedsAPartner_ThenEasesRecreationAndSocialNeeds_AndWarmsThePlayers()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var first = Idle(state, "lounge");
        var second = Idle(state, "lounge", skip: 1);
        first.RecreationNeed = 60;
        first.SocialNeed = 50;
        first.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.PlayCards, "Cards.");

        // Alone at the table: no game, so no break at all.
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));
        Assert.True(first.RecreationNeed > 60, "a card game alone is no break");
        Assert.True(first.SocialNeed > 50);

        second.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.PlayCards, "Cards.");
        var recreation = first.RecreationNeed;
        var social = first.SocialNeed;
        var firstAffinity = first.Relationships[second.Name].Affinity;
        var secondAffinity = second.Relationships[first.Name].Affinity;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(recreation - 1.6, first.RecreationNeed, 3);
        Assert.Equal(social - RecreationActivityRules.CardGameSocialReliefPerMinute, first.SocialNeed, 3);
        Assert.Equal(firstAffinity + RecreationActivityRules.CardGameAffinityPerMinute, first.Relationships[second.Name].Affinity, 3);
        Assert.Equal(secondAffinity + RecreationActivityRules.CardGameAffinityPerMinute, second.Relationships[first.Name].Affinity, 3);

        // Someone watching TV in the same room is not at the game.
        var watcher = Idle(state, "lounge", skip: 2);
        watcher.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.WatchTv, "TV.");
        Assert.DoesNotContain(watcher, RecreationActivityRules.CardPartners(state, first));
        Assert.Empty(RecreationActivityRules.CardPartners(state, watcher));
    }

    [Fact]
    public void CardPlayersSitAtTheTable_OnSeparateSeats()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var lounge = state.Facility.Rooms["lounge"];
        var cards = RecreationActivityRules.Find(RecreationActivityRules.PlayCards)!;
        var table = RecreationActivityRules.FixtureFor(lounge, cards)!;
        Assert.Equal(FixtureType.Table, table.Type);

        var seats = RecreationActivityRules.SeatsAt(lounge, table);
        Assert.True(seats.Count >= 2, "the layout pass keeps the lounge sofas at the low table");

        var first = Idle(state, "lounge");
        var second = Idle(state, "lounge", skip: 1);
        first.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.PlayCards, "Cards.");
        second.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.PlayCards, "Cards.");

        var firstSeat = RecreationActivityRules.PlaceFor(state, lounge, first, cards);
        var secondSeat = RecreationActivityRules.PlaceFor(state, lounge, second, cards);
        Assert.Contains(firstSeat, seats);
        Assert.Contains(secondSeat, seats);
        Assert.NotSame(firstSeat, secondSeat);
    }

    [Fact]
    public void TheFallbackStartsOrJoinsACardGameOnlyWithAPartner()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = Idle(state, "control");
        npc.Stress = 10;
        npc.SocialNeed = 60;

        Assert.NotEqual(RecreationActivityRules.PlayCards, RecreationActivityRules.FallbackChoice(state, npc, "lounge"));

        // Another lonely person in the lounge: a game worth starting.
        var player = Idle(state, "lounge", skip: 1);
        player.SocialNeed = 60;
        Assert.Equal(RecreationActivityRules.PlayCards, RecreationActivityRules.FallbackChoice(state, npc, "lounge"));

        // Already under way: a lonely person joins it.
        player.SocialNeed = 10;
        player.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.PlayCards, "Cards.");
        Assert.Equal(RecreationActivityRules.PlayCards, RecreationActivityRules.FallbackChoice(state, npc, "lounge"));

        // Not lonely: their own taste decides, as before.
        npc.SocialNeed = 10;
        Assert.Contains(
            RecreationActivityRules.FallbackChoice(state, npc, "lounge"),
            new[] { RecreationActivityRules.WatchTv, RecreationActivityRules.PlayGames });
    }

    [Fact]
    public void CognitionSeesWhoIsPlayingCards()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = Idle(state, "lounge");

        Assert.Contains(
            "play-cards (play cards at the table: nobody playing; it needs 2 players)",
            NpcPromptBuilder.Build(npc, state));

        var player = Idle(state, "lounge", skip: 1);
        player.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.PlayCards, "Cards.");
        Assert.Contains(
            $"play-cards (play cards at the table: {player.Name} already playing cards)",
            NpcPromptBuilder.Build(npc, state));
    }

    [Fact]
    public void WritingIsDoneAtTheQuartersDesk_AndIsTheMostCalmingBreak()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var write = RecreationActivityRules.Find(RecreationActivityRules.Write)!;
        Assert.Equal(RoomType.CrewQuarters, write.Room);
        Assert.Equal("quarters", write.RoomId);
        Assert.Equal(
            RecreationActivityRules.All.Max(activity => activity.StressReliefPerMinute),
            write.StressReliefPerMinute);

        var npc = Idle(state, "control");
        npc.RecreationNeed = 70;
        npc.Stress = 50;
        npc.Intent = new NpcIntent(ActionKind.Recreate, RecreationActivityRules.Write, "Write it down.", "Tense.", 50, "Test", state.Elapsed);

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();
        var quarters = state.Facility.Rooms["quarters"];
        for (var i = 0; i < 3 * 30; i++)
        {
            intents.Tick(state);
            movement.Tick(state, Tick);
        }

        Assert.Equal("quarters", npc.CurrentRoomId);
        Assert.Equal(ActionKind.Recreate, npc.CurrentAction.Kind);
        Assert.Equal(RecreationActivityRules.Write, npc.CurrentAction.TargetId);

        // Sitting in the chair pulled up to the writing desk.
        var desk = RecreationActivityRules.FixtureFor(quarters, write)!;
        var place = RecreationActivityRules.PlaceFor(state, quarters, npc, write)!;
        Assert.Equal(FixtureType.Chair, place.Type);
        Assert.Contains(place, RecreationActivityRules.SeatsAt(quarters, desk));

        // Works in the dark: a pen needs no power.
        quarters.IsPowered = false;
        var recreation = npc.RecreationNeed;
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(recreation - write.RecreationReliefPerMinute, npc.RecreationNeed, 3);
        Assert.Contains(npc.StatLog, entry => entry.Cause == "writing" && entry.Delta < 0);
    }

    [Fact]
    public void EachActivityIsDoneOnlyInItsOwnRoom()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var resolver = new ActionResolver();

        var inLounge = Idle(state, "lounge");
        Assert.False(resolver.TryApply(state, inLounge.Id, new NpcAction(ActionKind.Recreate, RecreationActivityRules.Write, "Write."), out _));
        Assert.True(resolver.TryApply(state, inLounge.Id, new NpcAction(ActionKind.Recreate, RecreationActivityRules.WatchTv, "TV."), out _));

        var inQuarters = Idle(state, "quarters", skip: 1);
        Assert.True(resolver.TryApply(state, inQuarters.Id, new NpcAction(ActionKind.Recreate, RecreationActivityRules.Write, "Write."), out _));
        Assert.False(resolver.TryApply(state, inQuarters.Id, new NpcAction(ActionKind.Recreate, RecreationActivityRules.Read, "Book."), out _));

        // The lounge's table and reading chair are not a writing desk, and the
        // quarters desk is not the reading chair: standing in the wrong room
        // the activity is no break at all.
        var lounge = state.Facility.Rooms["lounge"];
        var quarters = state.Facility.Rooms["quarters"];
        Assert.False(RecreationActivityRules.IsAvailable(lounge, RecreationActivityRules.Find(RecreationActivityRules.Write)!));
        Assert.False(RecreationActivityRules.IsAvailable(quarters, RecreationActivityRules.Find(RecreationActivityRules.Read)!));
        inLounge.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.Write, "Write.");
        Assert.Equal(0, RecreationActivityRules.ReliefPerMinute(state, inLounge));

        // A plain break, or an unknown ID, still means the lounge.
        Assert.Equal("lounge", RecreationActivityRules.RoomIdFor(null));
        Assert.Equal("lounge", RecreationActivityRules.RoomIdFor("lounge"));
        Assert.Equal("quarters", RecreationActivityRules.RoomIdFor("WRITE"));
    }

    [Fact]
    public void TheLoungeFallbackPickNeverSendsAnyoneToWrite()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = Idle(state, "control");
        state.Facility.Rooms["lounge"].IsPowered = false;

        foreach (var stress in new[] { 0.0, 30, 60, 95 })
        {
            foreach (var social in new[] { 0.0, 60 })
            {
                npc.Stress = stress;
                npc.SocialNeed = social;
                Assert.NotEqual(RecreationActivityRules.Write, RecreationActivityRules.FallbackChoice(state, npc, "lounge"));
            }
        }
    }

    [Fact]
    public void CognitionSeesTheQuartersDeskAlongsideTheLounge()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var npc = Idle(state, "lounge");

        var prompt = NpcPromptBuilder.Build(npc, state);
        var line = prompt.Split('\n').Single(candidate => candidate.StartsWith("RECREATION in ", StringComparison.Ordinal));
        Assert.StartsWith("RECREATION in Recreation Lounge [lounge]: watch-tv", line);
        Assert.Contains("In Crew Quarters [quarters]: write (write a journal or letters at the writing desk: free).", line);
        Assert.DoesNotContain("write (", line[..line.IndexOf("In Crew Quarters", StringComparison.Ordinal)]);

        var writer = Idle(state, "quarters", skip: 1);
        writer.CurrentAction = new NpcAction(ActionKind.Recreate, RecreationActivityRules.Write, "Write.");
        Assert.Contains(
            "write (write a journal or letters at the writing desk: 1 other already writing)",
            NpcPromptBuilder.Build(npc, state));
    }

    private static Npc Idle(GameState state, string roomId, int skip = 0)
    {
        var npc = state.Crew.Where(candidate => candidate.IsAlive).Skip(skip).First();
        npc.CurrentRoomId = roomId;
        npc.PositionX = 50;
        npc.PositionY = 50;
        npc.Movement = null;
        npc.Intent = null;
        npc.ActiveTask = null;
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        return npc;
    }
}
