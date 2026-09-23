using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Coverage for owner idea #6 (emergent leadership via trust): a Suggest
/// CurrentAction becomes a perceivable <see cref="NpcSuggestion"/> on the
/// target, exposed to cognition alongside the target's own
/// <see cref="Relationship.Trust"/> in the suggester. No new leader role and
/// no compliance mechanic — C# never decides whether the target complies.
/// </summary>
public sealed class SuggestionCoordinationSystemTests
{
    [Fact]
    public void Suggest_CreatesAPendingSuggestionOnTheCoLocatedTarget()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];
        target.CurrentRoomId = suggester.CurrentRoomId;

        suggester.CurrentAction = new NpcAction(
            ActionKind.Suggest,
            target.Name,
            "Everyone should get to Medical.");

        new SuggestionCoordinationSystem().Tick(state);

        var suggestion = target.PendingSuggestion;
        Assert.NotNull(suggestion);
        Assert.Equal(suggester.Name, suggestion!.FromNpcName);
        Assert.Equal("Everyone should get to Medical.", suggestion.SuggestionText);
        Assert.Equal(ActionKind.Idle, suggester.CurrentAction.Kind);
        Assert.True(target.NeedsMindReconsideration);
        Assert.Contains(target.Memories, memory => memory.Description.Contains("suggested", StringComparison.Ordinal));
        Assert.Contains(suggester.Memories, memory => memory.Description.Contains("suggested", StringComparison.Ordinal));
    }

    [Fact]
    public void Suggest_ClearsActionWithoutASuggestionWhenTargetIsNotCoLocated()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];
        target.CurrentRoomId = "storage";
        suggester.CurrentRoomId = "control";

        suggester.CurrentAction = new NpcAction(
            ActionKind.Suggest,
            target.Name,
            "Everyone should get to Medical.");

        new SuggestionCoordinationSystem().Tick(state);

        Assert.Null(target.PendingSuggestion);
        Assert.Equal(ActionKind.Idle, suggester.CurrentAction.Kind);
    }

    [Fact]
    public void Suggest_GivesACoLocatedWitnessAGossipableMemory()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];
        var witness = state.Crew[2];
        target.CurrentRoomId = suggester.CurrentRoomId;
        witness.CurrentRoomId = suggester.CurrentRoomId;

        suggester.CurrentAction = new NpcAction(
            ActionKind.Suggest,
            target.Name,
            "Everyone should get to Medical.");

        new SuggestionCoordinationSystem().Tick(state);

        Assert.Contains(witness.Memories, memory =>
            memory.Description == $"Overheard {suggester.Name} suggest to {target.Name}: Everyone should get to Medical.");
    }

    [Fact]
    public void Suggest_WitnessInTheDarkCannotIdentifyTheSuggester()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];
        var witness = state.Crew[2];

        // Sight is measured in station map units; use the widest compartment so
        // a mid-range (beyond dark sight, inside lit sight) separation fits.
        var room = state.Facility.Rooms.Values
            .OrderByDescending(candidate => candidate.MapWidth)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .First();
        suggester.CurrentRoomId = room.Id;
        suggester.PositionX = 10;
        suggester.PositionY = 50;
        target.CurrentRoomId = room.Id;
        target.PositionX = 10;
        target.PositionY = 40;
        witness.CurrentRoomId = room.Id;
        witness.PositionY = 50;
        witness.PositionX = suggester.PositionX + (12 / room.MapWidth * 100);
        Assert.True(witness.PositionX < 95, $"{room.Id} is too narrow for this separation.");
        room.LightsOn = false;

        suggester.CurrentAction = new NpcAction(
            ActionKind.Suggest,
            target.Name,
            "Everyone should get to Medical.");

        new SuggestionCoordinationSystem().Tick(state);

        Assert.Contains(witness.Memories, memory =>
            memory.Description == $"Overheard someone suggest something to {target.Name}: Everyone should get to Medical.");
        Assert.DoesNotContain(witness.Memories, memory => memory.Description.Contains(suggester.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void Suggest_DoesNotGiveAMemoryToCrewInAnotherRoom()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];
        var elsewhere = state.Crew[2];
        target.CurrentRoomId = suggester.CurrentRoomId;
        elsewhere.CurrentRoomId = state.Facility.Rooms.Keys.First(id => id != suggester.CurrentRoomId);

        suggester.CurrentAction = new NpcAction(
            ActionKind.Suggest,
            target.Name,
            "Everyone should get to Medical.");

        new SuggestionCoordinationSystem().Tick(state);

        Assert.Empty(elsewhere.Memories);
    }

    [Fact]
    public void PendingSuggestion_ExpiresAfterTenMinutesUnacted()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];

        target.PendingSuggestion = new NpcSuggestion(
            suggester.Name,
            "Everyone should get to Medical.",
            state.Elapsed);

        state.Elapsed += TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1);
        new SuggestionCoordinationSystem().Tick(state);

        Assert.Null(target.PendingSuggestion);
    }

    [Fact]
    public void PendingSuggestion_DoesNotExpireBeforeTenMinutes()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];

        target.PendingSuggestion = new NpcSuggestion(
            suggester.Name,
            "Everyone should get to Medical.",
            state.Elapsed);

        state.Elapsed += TimeSpan.FromMinutes(9);
        new SuggestionCoordinationSystem().Tick(state);

        Assert.NotNull(target.PendingSuggestion);
    }

    [Fact]
    public void FullPipeline_ASuggestIntentBecomesAPendingSuggestionOnTheTarget()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];
        target.CurrentRoomId = suggester.CurrentRoomId;

        suggester.Intent = new NpcIntent(
            ActionKind.Suggest,
            target.Name,
            "Rally at Medical",
            "Everyone should get to Medical.",
            70,
            "Cognition",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);
        Assert.Equal(ActionKind.Suggest, suggester.CurrentAction.Kind);

        new SuggestionCoordinationSystem().Tick(state);

        var suggestion = target.PendingSuggestion;
        Assert.NotNull(suggestion);
        Assert.Equal(suggester.Name, suggestion!.FromNpcName);
        Assert.Equal("Everyone should get to Medical.", suggestion.SuggestionText);
    }

    [Fact]
    public void Prompt_ExposesThePendingSuggestionAlongsideTheListenersOwnTrustInTheSuggester()
    {
        var state = FacilitySeeder.CreateDefault();
        var suggester = state.Crew[0];
        var target = state.Crew[1];
        target.Relationships[suggester.Name] = new Relationship
        {
            PersonName = suggester.Name,
            Trust = 87
        };
        target.PendingSuggestion = new NpcSuggestion(
            suggester.Name,
            "Everyone should get to Medical.",
            state.Elapsed);

        var prompt = NpcPromptBuilder.Build(target, state);

        Assert.Contains("PENDING SUGGESTION ADDRESSED TO YOU:", prompt);
        Assert.Contains($"from {suggester.Name} (your trust in them: 87/100)", prompt);
        Assert.Contains("Everyone should get to Medical.", prompt);
    }
}
