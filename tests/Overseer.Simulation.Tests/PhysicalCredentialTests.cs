using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #25, slice 1: a hatch-lock keycard is a physical credential.
/// Crew trained to secure hatches carry one; anyone who holds it, by being
/// lent it or by stealing it, can lock and unlock hatches. Who to lend it to
/// or take it from is the mind's choice.
/// </summary>
public sealed class PhysicalCredentialTests
{
    [Fact]
    public void CrewTrainedToSecureHatches_EachCarryOneKeycard_AndNobodyElseDoes()
    {
        var crew = SeededCrewRosterGenerator.Generate(4242);
        var state = FacilitySeeder.CreateDefault(crew, stationSeed: 4242);

        foreach (var npc in state.Crew)
        {
            var keycards = state.Possessions
                .Where(p => p.OwnerId == npc.Id && p.Kind == PossessionKind.Keycard)
                .ToList();

            if (CrewDoorInteractionSystem.HasLockAuthority(npc))
            {
                var keycard = Assert.Single(keycards);
                Assert.Equal(npc.Id, keycard.CurrentHolderId);
                Assert.Contains(keycard.Id, npc.KnownPossessions);
            }
            else
            {
                Assert.Empty(keycards);
            }
        }

        var defaultState = FacilitySeeder.CreateDefault();
        Assert.Contains(defaultState.Possessions, p => p.Kind == PossessionKind.Keycard);
        Assert.Contains(defaultState.Crew, npc => !CrewDoorInteractionSystem.HasLockAuthority(npc));
    }

    [Fact]
    public void ALentKeycard_LetsItsHolderLockAHatch_AndGoesWithIt()
    {
        var (state, owner, borrower, keycard, door) = Setup();
        var doors = new CrewDoorInteractionSystem();

        Assert.False(CrewDoorInteractionSystem.CanLockOrUnlock(state, borrower, door));
        Assert.False(doors.TryOperate(state, borrower, door, ActionKind.LockDoor, out _));

        owner.Relationships[borrower.Name].Trust = 80;
        owner.Relationships[borrower.Name].Affinity = 80;
        borrower.KnownPossessions[keycard.Id] = new PossessionSighting(
            keycard.Id, owner.Id, owner.Name, null, null, state.Elapsed);
        Assert.True(new ActionResolver().TryApply(
            state,
            borrower.Id,
            new NpcAction(ActionKind.BorrowItem, keycard.Id, "Need to seal this."),
            out var borrowed), borrowed);
        Assert.Equal(borrower.Id, keycard.CurrentHolderId);

        Assert.Same(keycard, CrewDoorInteractionSystem.HeldLockCredential(state, borrower));
        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, borrower, ActionKind.LockDoor, door.Id, out _));
        Assert.True(doors.TryOperate(state, borrower, door, ActionKind.LockDoor, out var locked), locked);
        Assert.True(door.IsLocked);
        Assert.True(doors.TryOperate(state, borrower, door, ActionKind.UnlockDoor, out _));
        Assert.False(door.IsLocked);

        // Role-trained authority is kept by the owner without the card.
        Assert.True(CrewDoorInteractionSystem.CanLockOrUnlock(state, owner, door));

        // Destroyed, the card grants nothing.
        keycard.IsDestroyed = true;
        Assert.Null(CrewDoorInteractionSystem.HeldLockCredential(state, borrower));
        Assert.False(doors.TryOperate(state, borrower, door, ActionKind.LockDoor, out _));
    }

    [Fact]
    public void AStolenKeycard_WorksForTheThiefToo()
    {
        var (state, owner, thief, keycard, door) = Setup();
        owner.Relationships[thief.Name].Trust = 0;
        thief.KnownPossessions[keycard.Id] = new PossessionSighting(
            keycard.Id, owner.Id, owner.Name, null, null, state.Elapsed);

        Assert.True(new ActionResolver().TryApply(
            state,
            thief.Id,
            new NpcAction(ActionKind.StealItem, keycard.Id, "Mine now."),
            out var stolen), stolen);
        Assert.Equal(thief.Id, keycard.CurrentHolderId);
        Assert.True(CrewDoorInteractionSystem.CanLockOrUnlock(state, thief, door));
    }

    [Fact]
    public void ThePromptNamesTheKeycardAsTheHoldersWayToLockAHatch()
    {
        var (state, _, borrower, keycard, door) = Setup();

        Assert.DoesNotContain("keycard you hold can lock/unlock it", NpcPromptBuilder.Build(borrower, state));

        keycard.CurrentHolderId = borrower.Id;

        Assert.Contains(
            $"{door.Id} -> ",
            NpcPromptBuilder.Build(borrower, state));
        Assert.Contains(
            "a hatch-lock keycard you hold can lock/unlock it",
            NpcPromptBuilder.Build(borrower, state));
    }

    private static (GameState State, Npc Owner, Npc Other, PersonalPossession Keycard, Door Door) Setup()
    {
        var state = FacilitySeeder.CreateDefault();
        var keycard = state.Possessions.First(p => p.Kind == PossessionKind.Keycard);
        var owner = state.Crew.Single(npc => npc.Id == keycard.OwnerId);
        var other = state.Crew.First(npc => !CrewDoorInteractionSystem.HasLockAuthority(npc));
        var door = state.Facility.FindDoorBetween("engineering", "hall-engineering")!;

        foreach (var npc in new[] { owner, other })
        {
            npc.CurrentRoomId = "engineering";
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.Movement = null;
            npc.ActiveTask = null;
            npc.Intent = null;
        }

        door.IsPowered = true;
        door.IsLocked = false;
        door.IsOpen = false;
        return (state, owner, other, keycard, door);
    }
}
