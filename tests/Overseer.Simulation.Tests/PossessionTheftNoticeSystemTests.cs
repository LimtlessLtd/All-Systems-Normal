using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #3, slice 4: the owner's one-time "surprised realization"
/// memory when a possession was stolen from a hidden stash while they were
/// absent — the only case where nobody else already tells them directly.
/// </summary>
public sealed class PossessionTheftNoticeSystemTests
{
    [Fact]
    public void OwnerGetsAOneTimeMemoryWhenAHiddenPossessionIsStolenWhileTheyAreAbsentWithNoCulpritNamed()
    {
        // The owner was genuinely absent and has no sighting of their own
        // establishing who did it, so the realization memory must not name
        // the thief — naming them from live global state would be
        // omniscient knowledge nobody actually gave the owner.
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var thief = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = thief.Id;
        possession.OwnerAwareOfCurrentState = false;

        new PossessionTheftNoticeSystem().Tick(state);

        Assert.Contains(
            owner.Memories,
            memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(
            owner.Memories,
            memory => memory.Description.Contains(thief.Name, StringComparison.Ordinal));
        Assert.True(possession.OwnerAwareOfCurrentState);
        Assert.True(owner.NeedsMindReconsideration);
    }

    [Fact]
    public void OwnerNamesTheCulpritOnlyWhenTheirOwnSightingIndependentlyIdentifiesTheCurrentHolder()
    {
        // If the owner's own belief (from ambient perception or witnessing a
        // later act) already places this exact person as the current
        // holder, that is genuine observer-specific evidence, so the
        // realization memory may name them.
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var thief = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = thief.Id;
        possession.OwnerAwareOfCurrentState = false;
        owner.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, thief.Id, thief.Name, null, null, state.Elapsed);

        new PossessionTheftNoticeSystem().Tick(state);

        Assert.Contains(
            owner.Memories,
            memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal)
                && memory.Description.Contains(thief.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void TheNoticeFiresOnlyOnceEvenAcrossManyTicksWhileTheItemStaysElsewhere()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var thief = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = thief.Id;
        possession.OwnerAwareOfCurrentState = false;

        var system = new PossessionTheftNoticeSystem();
        system.Tick(state);
        var noticeCountAfterFirstTick = owner.Memories.Count(
            memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal));

        system.Tick(state);
        system.Tick(state);

        Assert.Equal(
            noticeCountAfterFirstTick,
            owner.Memories.Count(memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal)));
    }

    [Fact]
    public void NoNoticeWhenTheOwnerWasDirectlyPresentAsTheHolderTakenFrom()
    {
        // TryStealPossession/TryBorrowPossession already grant a direct
        // memory in this case (see PersonalPossessionInteractionTests), so
        // OwnerAwareOfCurrentState stays true and this system must not
        // double up on it.
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);

        var stolen = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.StealItem, possession.Id, "I'm taking that."),
            out _);

        Assert.True(stolen);
        Assert.True(possession.OwnerAwareOfCurrentState);

        var memoryCountBefore = owner.Memories.Count;
        new PossessionTheftNoticeSystem().Tick(state);

        Assert.Equal(memoryCountBefore, owner.Memories.Count);
    }

    [Fact]
    public void StealingFromAHiddenStashMarksTheOwnerAsNotYetNoticed()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = "storage";
        actor.CurrentRoomId = "storage";
        owner.CurrentRoomId = "control";
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, null, null, "storage", null, state.Elapsed);

        var stolen = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.StealItem, possession.Id, "No one will know."),
            out _);

        Assert.True(stolen);
        Assert.False(possession.OwnerAwareOfCurrentState);

        new PossessionTheftNoticeSystem().Tick(state);

        Assert.Contains(
            owner.Memories,
            memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal));
        Assert.True(possession.OwnerAwareOfCurrentState);
    }

    [Fact]
    public void AHiddenPossessionNotYetStolenNeverTriggersANotice()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = owner.CurrentRoomId;

        new PossessionTheftNoticeSystem().Tick(state);

        Assert.DoesNotContain(
            owner.Memories,
            memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal));
    }
}
