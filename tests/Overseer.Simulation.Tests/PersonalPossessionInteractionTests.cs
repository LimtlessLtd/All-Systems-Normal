using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #3, slice 2: an owner may hide or retrieve one of their own
/// personal possessions. Owner-only; no borrow/steal/discovery yet.
/// </summary>
public sealed class PersonalPossessionInteractionTests
{
    [Fact]
    public void HideItem_MovesAHeldPossessionToTheOwnersCurrentRoom()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == npc.Id);

        var success = new ActionResolver().TryApply(
            state,
            npc.Id,
            new NpcAction(ActionKind.HideItem, possession.Id, "I want this out of sight."),
            out _);

        Assert.True(success);
        Assert.Null(possession.CurrentHolderId);
        Assert.Equal(npc.CurrentRoomId, possession.HiddenAtRoomId);
        Assert.Contains(npc.Memories, memory => memory.Description.Contains("hid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HideItem_FailsWhenTheItemIsAlreadyHidden()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == npc.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = npc.CurrentRoomId;

        var success = new ActionResolver().TryApply(
            state,
            npc.Id,
            new NpcAction(ActionKind.HideItem, possession.Id, "I want this out of sight."),
            out var message);

        Assert.False(success);
        Assert.Contains("not holding", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HideItem_FailsForAnotherCrewMembersPossession()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var other = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);

        var success = new ActionResolver().TryApply(
            state,
            other.Id,
            new NpcAction(ActionKind.HideItem, possession.Id, "Not mine, but let's try."),
            out _);

        Assert.False(success);
        Assert.Equal(owner.Id, possession.CurrentHolderId);
    }

    [Fact]
    public void ReturnItem_PicksBackUpAPossessionHiddenInTheOwnersCurrentRoom()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == npc.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = npc.CurrentRoomId;
        possession.HiddenAtFixtureLabel = "Personal Lockers";

        var success = new ActionResolver().TryApply(
            state,
            npc.Id,
            new NpcAction(ActionKind.ReturnItem, possession.Id, "I need it back."),
            out _);

        Assert.True(success);
        Assert.Equal(npc.Id, possession.CurrentHolderId);
        Assert.Null(possession.HiddenAtRoomId);
        Assert.Null(possession.HiddenAtFixtureLabel);
        Assert.Contains(npc.Memories, memory => memory.Description.Contains("retrieved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReturnItem_FailsWhenTheOwnerIsNotInTheHidingRoom()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == npc.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = "storage";
        npc.CurrentRoomId = "control";

        var success = new ActionResolver().TryApply(
            state,
            npc.Id,
            new NpcAction(ActionKind.ReturnItem, possession.Id, "I need it back."),
            out var message);

        Assert.False(success);
        Assert.Equal("storage", possession.HiddenAtRoomId);
        Assert.Contains("nothing hidden", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryNormalizeTarget_OnlyAllowsHideItemWhenTheOwnerCurrentlyHoldsIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var held = state.Possessions.First(p => p.OwnerId == npc.Id);

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, npc, ActionKind.HideItem, held.Id, out _));

        held.CurrentHolderId = null;
        held.HiddenAtRoomId = npc.CurrentRoomId;

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, npc, ActionKind.HideItem, held.Id, out _));
    }

    [Fact]
    public void TryNormalizeTarget_OnlyAllowsReturnItemWhenCoLocatedWithTheHidingRoom()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == npc.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = "storage";
        npc.CurrentRoomId = "control";

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, npc, ActionKind.ReturnItem, possession.Id, out _));

        npc.CurrentRoomId = "storage";

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, npc, ActionKind.ReturnItem, possession.Id, out _));
    }

    [Fact]
    public void Intent_HideItem_ResolvesInstantlyAndClearsTheIntent()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var possession = state.Possessions.First(p => p.OwnerId == npc.Id);

        npc.Intent = new NpcIntent(
            ActionKind.HideItem,
            possession.Id,
            "Keep it safe.",
            "I don't want anyone finding this.",
            40,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Null(npc.Intent);
        Assert.Null(possession.CurrentHolderId);
        Assert.Equal(npc.CurrentRoomId, possession.HiddenAtRoomId);
    }

    [Fact]
    public void Intent_HideItem_FailsGracefullyForAPossessionThatIsNotTheirOwn()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var other = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);

        other.Intent = new NpcIntent(
            ActionKind.HideItem,
            possession.Id,
            "Keep it safe.",
            "Not mine, but let's try.",
            40,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Null(other.Intent);
        Assert.Equal(ActionKind.Idle, other.CurrentAction.Kind);
        Assert.Equal(owner.Id, possession.CurrentHolderId);
    }
}
