using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #3, slice 5: DestroyItem and its relationship/memory
/// consequences. Mirrors StealItem's targeting/knowledge rules, plus the
/// "surprised realization" notice slice 4 introduced.
/// </summary>
public sealed class PersonalPossessionDestructionTests
{
    [Fact]
    public void OwnerCanDestroyTheirOwnHeldPossessionWithNoRelationshipConsequence()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);

        var success = new ActionResolver().TryApply(
            state,
            owner.Id,
            new NpcAction(ActionKind.DestroyItem, possession.Id, "I don't need this anymore."),
            out _);

        Assert.True(success);
        Assert.True(possession.IsDestroyed);
        Assert.Contains(owner.Memories, memory => memory.Description.Contains("destroyed", StringComparison.OrdinalIgnoreCase));
        Assert.True(possession.OwnerAwareOfCurrentState);
    }

    [Fact]
    public void DestroyingSomeoneElsesHeldPossessionRightInFrontOfThemCostsTrustAndNotifiesThemDirectly()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);
        var trustBefore = owner.Relationships[actor.Name].Trust;

        var success = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.DestroyItem, possession.Id, "Taking this from them."),
            out _);

        Assert.True(success);
        Assert.True(possession.IsDestroyed);
        Assert.True(owner.Relationships[actor.Name].Trust < trustBefore);
        Assert.Contains(
            owner.Memories,
            memory => memory.Description.Contains("destroyed", StringComparison.OrdinalIgnoreCase));
        Assert.True(possession.OwnerAwareOfCurrentState);
        Assert.True(owner.NeedsMindReconsideration);
    }

    [Fact]
    public void DestroyingAHeldPossessionOfAThirdPartyCostsThatHolderTrustNotTheAbsentOwner()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var holder = state.Crew[1];
        var actor = state.Crew[2];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = holder.Id;
        holder.CurrentRoomId = "storage";
        actor.CurrentRoomId = "storage";
        owner.CurrentRoomId = "control";
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, holder.Id, holder.Name, null, null, state.Elapsed);
        var holderTrustBefore = holder.Relationships[actor.Name].Trust;

        var success = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.DestroyItem, possession.Id, "Destroying it out of spite."),
            out _);

        Assert.True(success);
        Assert.True(possession.IsDestroyed);
        Assert.True(holder.Relationships[actor.Name].Trust < holderTrustBefore);
        Assert.Contains(
            holder.Memories,
            memory => memory.Description.Contains("destroyed", StringComparison.OrdinalIgnoreCase));
        // The owner wasn't there and wasn't the one holding it — nobody told them directly.
        Assert.False(possession.OwnerAwareOfCurrentState);
        Assert.DoesNotContain(owner.Memories, memory => memory.Description.Contains(possession.Name));
    }

    [Fact]
    public void DestroyingFromAKnownHiddenStashWhileTheOwnerIsAbsentMarksThemNotYetAware()
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

        var success = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.DestroyItem, possession.Id, "No one will ever know."),
            out _);

        Assert.True(success);
        Assert.True(possession.IsDestroyed);
        Assert.False(possession.OwnerAwareOfCurrentState);

        new PossessionTheftNoticeSystem().Tick(state);

        // The owner was genuinely absent (in "control" while this happened
        // in "storage") and has no sighting of their own identifying who
        // did it, so the realization memory must not name the actor.
        Assert.Contains(
            owner.Memories,
            memory => memory.Description.Contains(possession.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(
            owner.Memories,
            memory => memory.Description.Contains(actor.Name, StringComparison.Ordinal));
        Assert.True(possession.OwnerAwareOfCurrentState);
    }

    [Fact]
    public void DestroyingFromAHiddenStashRequiresTheActorsOwnBeliefNotJustLiveCoincidence()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);

        // Unwitnessed by the actor: hidden after the sighting above.
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = actor.CurrentRoomId;

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.DestroyItem, possession.Id, out _));

        var destroyed = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.DestroyItem, possession.Id, "Destroying it."),
            out _);

        Assert.False(destroyed);
        Assert.False(possession.IsDestroyed);
    }

    [Fact]
    public void TryNormalizeTarget_AllowsDestroyingAKnownHeldOrHiddenPossessionButBorrowItemStillCannotTargetHidden()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = "storage";
        actor.CurrentRoomId = "storage";
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, null, null, "storage", null, state.Elapsed);

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.DestroyItem, possession.Id, out _));
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.BorrowItem, possession.Id, out _));
    }

    [Fact]
    public void TryNormalizeTarget_AllowsDestroyingAPossessionTheActorAlreadyHolds()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, owner, ActionKind.DestroyItem, possession.Id, out var normalized));
        Assert.Equal(possession.Id, normalized);
    }

    [Fact]
    public void DestroyingAPossessionYouDoNotKnowAboutFailsGracefully()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;

        var success = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.DestroyItem, possession.Id, "Destroying it."),
            out var message);

        Assert.False(success);
        Assert.False(possession.IsDestroyed);
        Assert.Contains("nothing to destroy", message, StringComparison.OrdinalIgnoreCase);
    }

}
