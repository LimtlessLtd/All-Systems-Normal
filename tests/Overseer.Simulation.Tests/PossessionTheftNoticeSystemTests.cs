using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #3: the owner's one-time "surprised realization" when a hidden
/// possession is gone. Grounded in presence: it fires only once the owner is
/// back in the room where they believe they hid it.
/// </summary>
public sealed class PossessionTheftNoticeSystemTests
{
    [Fact]
    public void OwnerNoticesOnlyOnceBackWhereTheyHidTheStolenItem()
    {
        var (state, owner, thief, possession) = StolenFromStorage();
        var system = new PossessionTheftNoticeSystem();

        system.Tick(state);
        Assert.DoesNotContain(owner.Memories, memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal));
        Assert.False(possession.OwnerAwareOfCurrentState);

        owner.CurrentRoomId = "storage";
        system.Tick(state);

        Assert.Contains(
            owner.Memories,
            memory => memory.Description == $"I noticed {possession.Name} is missing from where I hid it.");
        Assert.DoesNotContain(owner.Memories, memory => memory.Description.Contains(thief.Name, StringComparison.Ordinal));
        Assert.True(possession.OwnerAwareOfCurrentState);
        Assert.True(owner.NeedsMindReconsideration);
        Assert.False(owner.KnownPossessions.ContainsKey(possession.Id));
    }

    [Fact]
    public void TheNoticeFiresOnlyOnceEvenAcrossManyTicks()
    {
        var (state, owner, _, possession) = StolenFromStorage();
        owner.CurrentRoomId = "storage";
        var system = new PossessionTheftNoticeSystem();

        system.Tick(state);
        system.Tick(state);
        system.Tick(state);

        Assert.Single(owner.Memories, memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void NoNoticeWhenTheItemIsStillWhereTheOwnerLeftIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = owner.CurrentRoomId;
        possession.OwnerAwareOfCurrentState = false;
        owner.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, null, null, owner.CurrentRoomId, null, state.Elapsed);

        new PossessionTheftNoticeSystem().Tick(state);

        Assert.DoesNotContain(owner.Memories, memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal));
        Assert.True(possession.OwnerAwareOfCurrentState);
    }

    [Fact]
    public void NoNoticeWhenTheOwnerWasDirectlyPresentAsTheHolderTakenFrom()
    {
        // StealItem already grants the holder a direct memory, so
        // OwnerAwareOfCurrentState stays true and this system adds nothing.
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

    private static (GameState State, Npc Owner, Npc Thief, PersonalPossession Possession) StolenFromStorage()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var thief = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = "storage";
        owner.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, null, null, "storage", null, state.Elapsed);
        thief.CurrentRoomId = "storage";
        owner.CurrentRoomId = "control";
        thief.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, null, null, "storage", null, state.Elapsed);

        Assert.True(new ActionResolver().TryApply(
            state,
            thief.Id,
            new NpcAction(ActionKind.StealItem, possession.Id, "No one will know."),
            out _));
        Assert.False(possession.OwnerAwareOfCurrentState);
        return (state, owner, thief, possession);
    }
}
