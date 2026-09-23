using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #3: slice 2 (owner may hide/retrieve their own possessions)
/// plus slice 3 (borrow/steal a possession from another co-located crew
/// member, gated on already knowing about it).
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

    /// <summary>
    /// Owner idea #14 (secrets): hiding a possession is the paradigmatic
    /// secretive act, so both the hider's own memory and a witness's memory
    /// of it are flagged sensitive — genuine future blackmail leverage,
    /// never automatic background gossip.
    /// </summary>
    [Fact]
    public void HideItem_MarksBothTheHidersOwnMemoryAndAWitnessesMemoryAsSensitive()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var witness = state.Crew[1];
        witness.CurrentRoomId = npc.CurrentRoomId;
        var possession = state.Possessions.First(p => p.OwnerId == npc.Id);

        var success = new ActionResolver().TryApply(
            state,
            npc.Id,
            new NpcAction(ActionKind.HideItem, possession.Id, "I want this out of sight."),
            out _);

        Assert.True(success);
        Assert.Contains(npc.Memories, memory => memory.Description.Contains("hid", StringComparison.OrdinalIgnoreCase) && memory.IsSensitive);
        Assert.Contains(witness.Memories, memory => memory.Description.Contains("hide", StringComparison.OrdinalIgnoreCase) && memory.IsSensitive);
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

    /// <summary>
    /// Owner idea #11 (contraband): HideItem/ReturnItem are gated on holding
    /// the item or knowing where it's hidden, not on owning it, so a thief
    /// can stash something they stole instead of carrying it in plain sight.
    /// </summary>
    [Fact]
    public void HideItem_AllowsAThiefToHideAPossessionTheyStoleThatIsNotTheirOwn()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var thief = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = thief.Id;
        thief.CurrentRoomId = owner.CurrentRoomId;
        thief.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, thief.Id, thief.Name, null, null, state.Elapsed);

        var success = new ActionResolver().TryApply(
            state,
            thief.Id,
            new NpcAction(ActionKind.HideItem, possession.Id, "Better hide this before anyone notices."),
            out _);

        Assert.True(success);
        Assert.Null(possession.CurrentHolderId);
        Assert.Equal(thief.CurrentRoomId, possession.HiddenAtRoomId);
        Assert.Equal(owner.Id, possession.OwnerId);
    }

    [Fact]
    public void ReturnItem_AllowsAThiefToRetrieveContrabandTheyStashedThemselves()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var thief = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = thief.Id;
        thief.CurrentRoomId = owner.CurrentRoomId;
        thief.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, thief.Id, thief.Name, null, null, state.Elapsed);

        Assert.True(new ActionResolver().TryApply(
            state,
            thief.Id,
            new NpcAction(ActionKind.HideItem, possession.Id, "Stash it."),
            out _));

        var success = new ActionResolver().TryApply(
            state,
            thief.Id,
            new NpcAction(ActionKind.ReturnItem, possession.Id, "Get it back."),
            out _);

        Assert.True(success);
        Assert.Equal(thief.Id, possession.CurrentHolderId);
        Assert.Null(possession.HiddenAtRoomId);
    }

    [Fact]
    public void ReturnItem_FailsForSomeoneWhoNeverLearnedWhereAnUnownedPossessionIsHidden()
    {
        // Regression: unlike the owner (who always knows their own item's
        // live location), a non-owner must have their own genuine belief
        // placing the stash in this room — never an omniscient live-state
        // lookup, mirroring StealItem's existing hiding-spot rule.
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var bystander = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = owner.CurrentRoomId;
        bystander.CurrentRoomId = owner.CurrentRoomId;

        var success = new ActionResolver().TryApply(
            state,
            bystander.Id,
            new NpcAction(ActionKind.ReturnItem, possession.Id, "I'll just take that."),
            out var message);

        Assert.False(success);
        Assert.Equal(owner.CurrentRoomId, possession.HiddenAtRoomId);
        Assert.Contains("nothing hidden", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReturnItem_AllowsSomeoneWhoWitnessedTheHideToRetrieveSomeoneElsesStash()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var witness = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        witness.CurrentRoomId = owner.CurrentRoomId;

        Assert.True(new ActionResolver().TryApply(
            state,
            owner.Id,
            new NpcAction(ActionKind.HideItem, possession.Id, "Keep it safe."),
            out _));

        // NotifyPossessionWitnesses should have given the co-located witness
        // a real belief of where it's hidden.
        Assert.True(witness.KnownPossessions.TryGetValue(possession.Id, out var belief));
        Assert.Equal(owner.CurrentRoomId, belief.HiddenAtRoomId);

        var success = new ActionResolver().TryApply(
            state,
            witness.Id,
            new NpcAction(ActionKind.ReturnItem, possession.Id, "I know where that went."),
            out _);

        Assert.True(success);
        Assert.Equal(witness.Id, possession.CurrentHolderId);
    }

    [Fact]
    public void TryNormalizeTarget_HideItemAllowsAPossessionTheActorStoleButDoesNotOwn()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var thief = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = thief.Id;

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, thief, ActionKind.HideItem, possession.Id, out _));
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

    [Fact]
    public void TryNormalizeTarget_BorrowAndStealRequireKnowingAboutAPossessionHeldByACoLocatedCrewMember()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;

        // Not known to the actor yet: ambient noticing/witnessing hasn't happened.
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.BorrowItem, possession.Id, out _));
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.StealItem, possession.Id, out _));

        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.BorrowItem, possession.Id, out _));
        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.StealItem, possession.Id, out _));

        // Known, but the holder has since left the room.
        owner.CurrentRoomId = "control";
        actor.CurrentRoomId = "storage";

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.BorrowItem, possession.Id, out _));
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.StealItem, possession.Id, out _));
    }

    [Fact]
    public void TryNormalizeTarget_StealItemCanAlsoTargetAKnownHidingSpotButBorrowItemCannot()
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
            state, actor, ActionKind.StealItem, possession.Id, out _));
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.BorrowItem, possession.Id, out _));
    }

    [Fact]
    public void TryNormalizeTarget_StealItemFromAHiddenSpotRequiresTheActorsOwnBeliefNotJustLiveCoincidence()
    {
        // Regression: KnownPossessionIds used to be a flat permanent set, so
        // once an actor had ever perceived an item existed (e.g. seeing it
        // held, long before it was ever hidden), a later unwitnessed hide
        // that happened to land in the actor's current room was stealable —
        // knowledge the actor never actually had. The actor's own belief
        // must say it is hidden here, not just live global state.
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);

        // Unwitnessed by the actor: hidden in the actor's current room without
        // ever updating the actor's belief.
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = actor.CurrentRoomId;

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, actor, ActionKind.StealItem, possession.Id, out _));

        var stolen = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.StealItem, possession.Id, "No one will know."),
            out _);

        Assert.False(stolen);
        Assert.Equal(actor.CurrentRoomId, possession.HiddenAtRoomId);
    }

    [Fact]
    public void BorrowItem_TransfersHoldWhenTheHolderTrustsAndLikesTheBorrowerEnough()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);

        var success = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.BorrowItem, possession.Id, "Could I borrow that?"),
            out _);

        Assert.True(success);
        Assert.Equal(actor.Id, possession.CurrentHolderId);
        Assert.Contains(actor.Memories, memory => memory.Description.Contains("lent me", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(owner.Memories, memory => memory.Description.Contains("lent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BorrowItem_FailsWhenTheHolderDoesNotTrustOrLikeTheBorrowerEnough()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);
        owner.Relationships[actor.Name].Trust = 10;

        var success = new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.BorrowItem, possession.Id, "Could I borrow that?"),
            out var message);

        Assert.False(success);
        Assert.Equal(owner.Id, possession.CurrentHolderId);
        Assert.Contains("not willing to lend", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StealItem_TakesAHeldPossessionAndDamagesTheHoldersTrustInTheThief()
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
            new NpcAction(ActionKind.StealItem, possession.Id, "I'm taking that."),
            out _);

        Assert.True(success);
        Assert.Equal(actor.Id, possession.CurrentHolderId);
        Assert.True(owner.Relationships[actor.Name].Trust < trustBefore);
        Assert.Contains(
            owner.Memories,
            memory => memory.Description.Contains("without asking", StringComparison.OrdinalIgnoreCase));
        Assert.True(owner.NeedsMindReconsideration);
    }

    [Fact]
    public void StealItem_TakesFromAKnownHidingSpotWithoutConfrontingAnAbsentOwner()
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
            new NpcAction(ActionKind.StealItem, possession.Id, "No one will know."),
            out _);

        Assert.True(success);
        Assert.Equal(actor.Id, possession.CurrentHolderId);
        Assert.Null(possession.HiddenAtRoomId);
        Assert.DoesNotContain(owner.Memories, memory => memory.Description.Contains(possession.Name));
    }

    [Fact]
    public void StealItem_WitnessedByAThirdPartyGrantsThemKnowledgeAndAnIdentifiedMemory()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var witness = state.Crew[2];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;
        witness.CurrentRoomId = owner.CurrentRoomId;
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);

        new ActionResolver().TryApply(
            state,
            actor.Id,
            new NpcAction(ActionKind.StealItem, possession.Id, "Taking it."),
            out _);

        Assert.Contains(possession.Id, witness.KnownPossessions);
        Assert.Contains(
            witness.Memories,
            memory => memory.Description == $"Witnessed {actor.Name} takes {possession.Name} from {owner.Name}.");
    }

    [Fact]
    public void Intent_BorrowItem_FailsGracefullyWhenTheHolderRefuses()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var actor = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        actor.CurrentRoomId = owner.CurrentRoomId;
        actor.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);
        owner.Relationships[actor.Name].Trust = 10;

        actor.Intent = new NpcIntent(
            ActionKind.BorrowItem,
            possession.Id,
            "Ask to borrow it.",
            "I need this for a moment.",
            30,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Null(actor.Intent);
        Assert.Equal(ActionKind.Idle, actor.CurrentAction.Kind);
        Assert.Equal(owner.Id, possession.CurrentHolderId);
        Assert.Contains(actor.Memories, memory => memory.Description.Contains("not willing to lend", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Prompt_OwnersPossessionBlockShowsWhoElseIsCurrentlyHoldingIt()
    {
        // Regression: the status text only ever handled "with you" or a
        // hidden-location description. Once BorrowItem/StealItem could leave
        // CurrentHolderId pointing at a third party, that branch was reached
        // with a null HiddenAtRoomId and rendered a garbled "hidden in "
        // line instead of naming who has it. An owner always knows their own
        // possession's current state (same precedent as always knowing where
        // they hid it), so this is also how they'd notice a borrow or theft
        // without needing to physically go check first.
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var other = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = other.Id;

        var prompt = NpcPromptBuilder.Build(owner, state);

        Assert.Contains(
            $"{possession.Id}: {possession.Name} ({possession.Kind}) — with {other.Name}",
            prompt);
    }

    [Fact]
    public void Prompt_OtherPeoplesPossessionsBlockShowsTheObserversStaleBeliefNotLiveState()
    {
        // Regression: this block used to render the possession's live global
        // holder/hiding-room for anything ever in KnownPossessionIds, so a
        // single old sighting kept showing every later unwitnessed move
        // forever. It must show what this observer last actually perceived,
        // even once that has gone stale.
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var observer = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        observer.KnownPossessions[possession.Id] = new PossessionSighting(
            possession.Id, owner.Id, owner.Name, null, null, state.Elapsed);

        // Unwitnessed by the observer: moved to a hiding spot after the sighting.
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = "storage";
        possession.HiddenAtFixtureLabel = "Storage Crate";

        var prompt = NpcPromptBuilder.Build(observer, state);

        Assert.Contains($"{possession.Id}: {possession.Name} ({possession.Kind}), belongs to {owner.Name} — held by {owner.Name}", prompt);
        Assert.DoesNotContain("hidden in storage", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
