using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewTaskProgressTests
{
    [Fact]
    public void AuthoritativeTaskProgressOnlyCompletesAtOneHundredPercent()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];

        CrewTaskSystem.Start(
            state,
            npc,
            ActionKind.DisarmTurret,
            "st-1",
            "disarming security turret",
            TimeSpan.FromMinutes(10));

        Assert.Equal(0, CrewTaskSystem.Progress(state, npc), 6);
        Assert.False(CrewTaskSystem.IsComplete(state, npc));

        state.Elapsed += TimeSpan.FromMinutes(5);
        Assert.Equal(50, CrewTaskSystem.Progress(state, npc), 6);
        Assert.False(CrewTaskSystem.IsComplete(state, npc));

        state.Elapsed += TimeSpan.FromMinutes(5);
        Assert.True(CrewTaskSystem.IsComplete(state, npc));

        CrewTaskSystem.Succeed(state, npc, "Turret safed.");
        Assert.Equal(CrewTaskStatus.Succeeded, npc.ActiveTask!.Status);
        Assert.Equal(100, npc.ActiveTask.ProgressPercent(state.Elapsed), 6);
        Assert.Contains("Turret safed", npc.ActiveTask.Outcome);
    }

    [Fact]
    public void LowPriorityThoughtCannotCasuallyAbandonCommittedPhysicalWork()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];
        npc.CurrentAction = new NpcAction(ActionKind.DisarmTurret, "st-1", "Committed work.");

        CrewTaskSystem.Start(
            state,
            npc,
            ActionKind.DisarmTurret,
            "st-1",
            "disarming security turret",
            TimeSpan.FromMinutes(10));

        npc.Intent = new NpcIntent(
            ActionKind.Rest,
            null,
            "Take a break.",
            "A passing low-priority thought.",
            30,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Null(npc.Intent);
        Assert.Equal(CrewTaskStatus.InProgress, npc.ActiveTask!.Status);
        Assert.Equal(ActionKind.DisarmTurret, npc.CurrentAction.Kind);
    }

    [Fact]
    public void HighUrgencyAloneCannotInterruptCommittedPhysicalWork()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];
        npc.CurrentAction = new NpcAction(ActionKind.DisarmTurret, "st-1", "Committed work.");

        CrewTaskSystem.Start(
            state,
            npc,
            ActionKind.DisarmTurret,
            "st-1",
            "disarming security turret",
            TimeSpan.FromMinutes(10));

        npc.Intent = new NpcIntent(
            ActionKind.Rest,
            null,
            "Stop immediately.",
            "An ordinary but numerically urgent thought.",
            100,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(CrewTaskStatus.InProgress, npc.ActiveTask!.Status);
        Assert.Null(npc.Intent);
    }

    [Fact]
    public void GenuineLifeThreatCanInterruptCommittedPhysicalWork()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];
        npc.CurrentAction = new NpcAction(ActionKind.DisarmTurret, "st-1", "Committed work.");
        state.Facility.Rooms[npc.CurrentRoomId].FireIntensity = 25;

        CrewTaskSystem.Start(
            state,
            npc,
            ActionKind.DisarmTurret,
            "st-1",
            "disarming security turret",
            TimeSpan.FromMinutes(10));

        npc.Intent = new NpcIntent(
            ActionKind.SeekSafety,
            "medical",
            "Escape the fire.",
            "The compartment is actively burning.",
            90,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(CrewTaskStatus.Interrupted, npc.ActiveTask!.Status);
        Assert.Contains("Emergency interruption", npc.ActiveTask.Outcome, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoorOperationExposesAuthoritativeProgressBeforeChangingDoorState()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];
        var door = state.Facility.Doors.First(candidate =>
            candidate.RoomAId == npc.CurrentRoomId || candidate.RoomBId == npc.CurrentRoomId);

        door.IsPowered = true;
        door.IsLocked = false;
        door.IsOpen = false;
        npc.Intent = new NpcIntent(
            ActionKind.OpenDoor,
            door.Id,
            "Open the hatch.",
            "I need this hatch open.",
            50,
            "Test",
            state.Elapsed);

        var intents = new IntentExecutionSystem();
        intents.Tick(state);

        Assert.False(door.IsOpen);
        Assert.Equal(CrewTaskStatus.InProgress, npc.ActiveTask?.Status);
        Assert.Equal(ActionKind.OpenDoor, npc.ActiveTask?.Action);

        state.Elapsed += TimeSpan.FromSeconds(30);
        intents.Tick(state);
        Assert.InRange(CrewTaskSystem.Progress(state, npc), 49.9, 50.1);
        Assert.False(door.IsOpen);

        state.Elapsed += TimeSpan.FromSeconds(30);
        intents.Tick(state);

        Assert.True(door.IsOpen);
        Assert.Equal(CrewTaskStatus.Succeeded, npc.ActiveTask?.Status);
        Assert.Equal(100, npc.ActiveTask?.ProgressPercent(state.Elapsed), 6);
    }
}
