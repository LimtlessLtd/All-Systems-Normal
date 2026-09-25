using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #26, slice 1: a hatch's access controller records who locked,
/// unlocked, forced or bypassed it, when and with which credential, and only
/// what it could actually know.
/// </summary>
public sealed class DoorAccessLogTests
{
    [Fact]
    public void TrainedAccess_RecordsThePersonThemself()
    {
        var (state, owner, _, _, door) = Setup();
        state.Elapsed = TimeSpan.FromMinutes(75);

        Assert.True(new CrewDoorInteractionSystem().TryOperate(
            state, owner, door, ActionKind.LockDoor, out var message), message);

        var record = Assert.Single(door.AccessLog);
        Assert.Equal(DoorAccessKind.Lock, record.Kind);
        Assert.Equal(DoorAccessCredential.TrainedAccess, record.Credential);
        Assert.Equal(owner.Id, record.RecordedId);
        Assert.Equal(owner.Id, record.ActorId);
        Assert.Equal(TimeSpan.FromMinutes(75), record.At);
        Assert.Equal(
            $"T+01:15 LOCKED (trained access: {owner.Name})",
            DoorAccessLogSystem.Describe(record));
    }

    [Fact]
    public void AStolenKeycard_LeavesItsOwnersNameInTheLog()
    {
        var (state, owner, thief, keycard, door) = Setup();
        keycard.CurrentHolderId = thief.Id;
        var doors = new CrewDoorInteractionSystem();

        Assert.True(doors.TryOperate(state, thief, door, ActionKind.LockDoor, out _));
        Assert.True(doors.TryOperate(state, thief, door, ActionKind.UnlockDoor, out _));

        Assert.Equal(2, door.AccessLog.Count);
        Assert.Equal(DoorAccessKind.Unlock, door.AccessLog[0].Kind);
        Assert.All(door.AccessLog, record =>
        {
            Assert.Equal(DoorAccessCredential.Keycard, record.Credential);
            Assert.Equal(owner.Id, record.RecordedId);
            Assert.Equal(owner.Name, record.RecordedName);
            Assert.Equal(thief.Id, record.ActorId);
        });
        Assert.Contains($"keycard issued to {owner.Name}", DoorAccessLogSystem.Describe(door.AccessLog[0]));
        Assert.DoesNotContain(thief.Name, DoorAccessLogSystem.Describe(door.AccessLog[0]));
    }

    [Fact]
    public void ARefusedAttempt_RecordsNothing()
    {
        var (state, _, other, _, door) = Setup();

        Assert.False(new CrewDoorInteractionSystem().TryOperate(
            state, other, door, ActionKind.LockDoor, out _));

        Assert.Empty(door.AccessLog);
    }

    [Fact]
    public void ForcedHatches_AreUnidentified_AndAnUnpoweredControllerRecordsNothing()
    {
        var (state, _, other, _, door) = Setup();

        DoorAccessLogSystem.RecordCrew(state, door, other, DoorAccessKind.ForcedOpen);
        var record = Assert.Single(door.AccessLog);
        Assert.Equal(DoorAccessCredential.None, record.Credential);
        Assert.Null(record.RecordedId);
        Assert.Null(record.RecordedName);
        Assert.Equal(other.Id, record.ActorId);
        Assert.EndsWith("FORCED OPEN (no credential, unidentified)", DoorAccessLogSystem.Describe(record));

        door.IsPowered = false;
        DoorAccessLogSystem.RecordCrew(state, door, other, DoorAccessKind.Bypassed);
        Assert.Single(door.AccessLog);
    }

    [Fact]
    public void ForcingALockedHatch_IsRecordedAgainstNobody()
    {
        var (state, _, other, _, door) = Setup();
        door.IsLocked = true;
        other.Skills["Athletics"] = 100;
        door.ForceDifficulty = 0;
        door.TechnicalDifficulty = 0;
        other.CurrentAction = new NpcAction(ActionKind.ForceDoor, door.Id, "Let me through.");
        other.RoutineUntil = TimeSpan.Zero;

        var counterplay = new CrewCounterplaySystem();
        for (var minute = 0; minute < 10 && !door.IsManuallyOverridden; minute++)
        {
            state.Elapsed += TimeSpan.FromMinutes(1);
            counterplay.Tick(state);
        }

        Assert.True(door.IsManuallyOverridden);
        var record = Assert.Single(door.AccessLog);
        Assert.Contains(record.Kind, new[] { DoorAccessKind.ForcedOpen, DoorAccessKind.Bypassed });
        Assert.Equal(DoorAccessCredential.None, record.Credential);
        Assert.Null(record.RecordedName);
        Assert.Equal(other.Id, record.ActorId);
    }

    [Fact]
    public void TheLogKeepsOnlyTheNewestEntries()
    {
        var (state, owner, _, _, door) = Setup();

        for (var i = 0; i < DoorAccessLogSystem.MaxEntries + 5; i++)
        {
            state.Elapsed = TimeSpan.FromMinutes(i);
            DoorAccessLogSystem.RecordCrew(state, door, owner, DoorAccessKind.Lock);
        }

        Assert.Equal(DoorAccessLogSystem.MaxEntries, door.AccessLog.Count);
        Assert.Equal(TimeSpan.FromMinutes(DoorAccessLogSystem.MaxEntries + 4), door.AccessLog[0].At);
        Assert.Equal(TimeSpan.FromMinutes(5), door.AccessLog[^1].At);
    }

    [Fact]
    public void ReadingTheLog_RemembersWhatTheControllerRecorded_NotWhoReallyDidIt()
    {
        var (state, owner, thief, keycard, door) = Setup();
        keycard.CurrentHolderId = thief.Id;
        Assert.True(new CrewDoorInteractionSystem().TryOperate(
            state, thief, door, ActionKind.LockDoor, out _));
        var reader = state.Crew.First(npc => npc.Id != owner.Id && npc.Id != thief.Id);
        reader.CurrentRoomId = "engineering";
        var wasLocked = door.IsLocked;
        var lastOperator = door.LastCrewOperatorId;

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, reader, ActionKind.ReadAccessLog, door.Id, out var target));
        Assert.Equal(door.Id, target);
        Assert.True(new ActionResolver().TryApply(
            state, reader.Id, new NpcAction(ActionKind.ReadAccessLog, door.Id, "Who locked this?"), out var message), message);

        var memory = reader.Memories[^1];
        Assert.StartsWith($"I read {door.Id}'s access log: ", memory.Description);
        Assert.Contains($"LOCKED (keycard issued to {owner.Name})", memory.Description);
        Assert.DoesNotContain(thief.Name, memory.Description);
        Assert.Equal(wasLocked, door.IsLocked);
        Assert.Equal(lastOperator, door.LastCrewOperatorId);
        Assert.Single(door.AccessLog);
    }

    [Fact]
    public void AnEmptyLog_IsRememberedAsEmpty_AndReadingOnlyShowsTheLatestEntries()
    {
        var (state, owner, _, _, door) = Setup();

        Assert.EndsWith(
            "no lock activity recorded.",
            DoorAccessLogSystem.RememberReading(state, owner, door).Description);

        for (var i = 0; i < 8; i++)
        {
            state.Elapsed = TimeSpan.FromMinutes(i);
            DoorAccessLogSystem.RecordCrew(state, door, owner, DoorAccessKind.Lock);
        }

        var memory = DoorAccessLogSystem.RememberReading(state, owner, door);
        Assert.Equal(DoorAccessLogSystem.EntriesRead, memory.Description.Split("; ").Length);
        Assert.Contains("T+00:07", memory.Description);
        Assert.DoesNotContain("T+00:02", memory.Description);
    }

    [Fact]
    public void ADarkOrDistantPanel_CannotBeRead()
    {
        var (state, owner, _, _, door) = Setup();
        var memories = owner.Memories.Count;

        door.IsPowered = false;
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, owner, ActionKind.ReadAccessLog, door.Id, out _));
        Assert.False(new CrewDoorInteractionSystem().TryOperate(
            state, owner, door, ActionKind.ReadAccessLog, out _));

        door.IsPowered = true;
        owner.CurrentRoomId = "medical";
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, owner, ActionKind.ReadAccessLog, door.Id, out _));
        Assert.Equal(memories, owner.Memories.Count);
    }

    [Fact]
    public void AReadAccessLogIntent_IsCarriedOutAtTheHatch()
    {
        var (state, owner, _, _, door) = Setup();
        DoorAccessLogSystem.RecordOverseer(state, door, DoorAccessKind.Lock);
        owner.Intent = new NpcIntent(ActionKind.ReadAccessLog, door.Id, "Check the log", "Suspicious", 60, "test", state.Elapsed);

        var intents = new IntentExecutionSystem();
        for (var minute = 0; minute < 5 && owner.Intent is not null; minute++)
        {
            intents.Tick(state);
            state.Elapsed += TimeSpan.FromMinutes(1);
        }

        Assert.Null(owner.Intent);
        Assert.Contains(owner.Memories, memory =>
            memory.Description.Contains("LOCKED (Overseer network command)"));
    }

    [Fact]
    public void ThePromptOffersReadingTheLog()
    {
        var (state, owner, _, _, _) = Setup();

        Assert.True(CrewAffordanceSystem.IsCognitionAction(ActionKind.ReadAccessLog));
        var prompt = NpcPromptBuilder.Build(owner, state);
        Assert.Contains("ReadAccessLog", prompt);
        Assert.Contains("a keycard entry names the card's owner", prompt);
    }

    [Fact]
    public void ASkilledWipe_ErasesTheLog_ButLeavesATrace_AndIsASensitiveMemory()
    {
        var (state, owner, other, _, door) = Setup();
        DoorAccessLogSystem.RecordCrew(state, door, owner, DoorAccessKind.Lock);
        DoorAccessLogSystem.RecordCrew(state, door, owner, DoorAccessKind.Unlock);
        SetTechnical(other, 90);
        var witness = state.Crew.First(npc => npc.Id != owner.Id && npc.Id != other.Id);
        witness.CurrentRoomId = "engineering";
        witness.PositionX = 52;
        witness.PositionY = 50;
        Assert.True(PerceptionSystem.CanMakeOut(state, witness, other));
        state.Elapsed = TimeSpan.FromMinutes(130);

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state, other, ActionKind.WipeAccessLog, door.Id, out _));
        Assert.True(new ActionResolver().TryApply(
            state, other.Id, new NpcAction(ActionKind.WipeAccessLog, door.Id, "Cover it up."), out var message), message);

        var record = Assert.Single(door.AccessLog);
        Assert.Equal(DoorAccessKind.LogWiped, record.Kind);
        Assert.Null(record.RecordedName);
        Assert.Equal(other.Id, record.ActorId);
        Assert.Equal(
            "T+02:10 LOG WIPED (earlier entries erased at the panel)",
            DoorAccessLogSystem.Describe(record));

        Assert.Contains(other.Memories, memory =>
            memory.IsSensitive && memory.Description == $"I wiped {door.Id}'s access log.");
        Assert.Contains(witness.Memories, memory =>
            memory.IsSensitive
            && memory.Description == $"Witnessed {other.Name} erase {door.Id}'s access log at its panel.");
        Assert.True(witness.NeedsMindReconsideration);
        Assert.Contains(state.EventLog, line => line.EndsWith($"{other.Name} wipes {door.Id}'s access log."));

        Assert.Contains(
            "LOG WIPED",
            DoorAccessLogSystem.RememberReading(state, owner, door).Description);
    }

    [Fact]
    public void AnUnskilledOrDarkWipe_IsRefused_AndTheLogSurvives()
    {
        var (state, owner, other, _, door) = Setup();
        DoorAccessLogSystem.RecordCrew(state, door, owner, DoorAccessKind.Lock);
        SetTechnical(other, 10);

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state, other, ActionKind.WipeAccessLog, door.Id, out _));
        Assert.False(new CrewDoorInteractionSystem().TryOperate(
            state, other, door, ActionKind.WipeAccessLog, out var unskilled));
        Assert.Contains("technical skill", unskilled);

        SetTechnical(other, 90);
        door.IsPowered = false;
        Assert.False(new CrewDoorInteractionSystem().TryOperate(
            state, other, door, ActionKind.WipeAccessLog, out _));

        Assert.Equal(DoorAccessKind.Lock, Assert.Single(door.AccessLog).Kind);
        Assert.DoesNotContain(other.Memories, memory => memory.Description.Contains("wiped"));
    }

    [Fact]
    public void AWipeInABlindRoom_ReachesOverseerOnlyAsAControllerLine()
    {
        var (state, _, other, _, door) = Setup();
        SetTechnical(other, 90);
        state.Facility.Rooms["engineering"].CameraOnline = false;

        Assert.True(new CrewDoorInteractionSystem().TryOperate(
            state, other, door, ActionKind.WipeAccessLog, out _));

        Assert.Contains(state.EventLog, line =>
            OverseerSightSystem.IsUnseen(line) && line.Contains(other.Name));
        Assert.Contains(state.EventLog, line =>
            !OverseerSightSystem.IsUnseen(line)
            && line.EndsWith($"ACCESS LOG: {door.Id}'s log was wiped at its panel.")
            && !line.Contains(other.Name));
    }

    private static void SetTechnical(Npc npc, int value)
    {
        foreach (var skill in new[] { "Engineering", "Electrical", "Operations", "Reactor" })
            npc.Skills[skill] = value;
        npc.Traits.Clear();
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
        door.AccessLog.Clear();
        return (state, owner, other, keycard, door);
    }
}
