using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class V010EInteractionTests
{
    [Fact]
    public void StationSelection_RoutesEveryInspectableEntity()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 10010);
        var crew = state.Crew[0];
        var door = state.Facility.Doors[0];
        var robot = Assert.Single(state.Robots);
        var turret = Assert.Single(state.Turrets);

        Assert.NotNull(StationInspectionSystem.Room(
            state,
            new StationSelection(StationSelectionKind.Room, "control")));
        Assert.Same(
            crew,
            StationInspectionSystem.Crew(
                state,
                new StationSelection(
                    StationSelectionKind.Crew,
                    crew.Id.ToString())));
        Assert.Same(
            door,
            StationInspectionSystem.Door(
                state,
                new StationSelection(
                    StationSelectionKind.Door,
                    door.Id)));
        Assert.Same(
            robot,
            StationInspectionSystem.Robot(
                state,
                new StationSelection(
                    StationSelectionKind.Robot,
                    robot.Id)));
        Assert.Same(
            turret,
            StationInspectionSystem.Turret(
                state,
                new StationSelection(
                    StationSelectionKind.Turret,
                    turret.Id)));

        Assert.False(StationInspectionSystem.Exists(
            state,
            new StationSelection(StationSelectionKind.Door, "missing-door")));
    }

    [Fact]
    public void DebugTelemetry_IsDetachedAndNonGameplay()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        CognitionTelemetrySystem.Record(
            state,
            npc,
            "test",
            new NpcIntent(
                ActionKind.Work,
                "control",
                "Keep working.",
                "Routine test.",
                20,
                "test",
                state.Elapsed));

        state.EventLog.Insert(0, "test event");

        var snapshot = DebugTelemetrySystem.Capture(state);

        Assert.False(DebugTelemetrySystem.IsGameplayCritical);
        Assert.Single(snapshot.Cognition);
        Assert.NotEmpty(snapshot.Events);

        state.CognitionTelemetry.Clear();
        state.EventLog.Clear();

        Assert.Single(snapshot.Cognition);
        Assert.NotEmpty(snapshot.Events);
        Assert.True(npc.IsAlive);
    }

    [Fact]
    public void ClosedUnlockedDoor_IsOpenedForCrewTraversalThenClosesAfterTraffic()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 10011);
        var npc = state.Crew.Single(candidate => candidate.Name == "Sarah Chen");
        var door = state.Facility.FindDoorBetween(
            "engineering",
            "hall-engineering")!;

        door.IsPowered = true;
        door.IsLocked = false;
        door.IsOpen = false;

        var resolver = new ActionResolver();
        Assert.True(
            resolver.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.Move,
                    "hall-engineering",
                    "Walk through the ordinary hatch."),
                out var message),
            message);

        var movement = Assert.IsType<NpcMovement>(npc.Movement);
        npc.PositionX = movement.ExitX;
        npc.PositionY = movement.ExitY;

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(1));

        Assert.Equal("hall-engineering", npc.CurrentRoomId);
        Assert.True(door.IsOpen);
        Assert.Equal(npc.Id, door.LastCrewOperatorId);
        Assert.NotNull(door.CrewAutoCloseAt);

        state.Elapsed = door.CrewAutoCloseAt!.Value;
        new CrewDoorInteractionSystem().Tick(state);

        Assert.False(door.IsOpen);
        Assert.Null(door.CrewAutoCloseAt);
    }

    [Fact]
    public void DoorPermissions_OrdinaryCrewOperateButOnlyAuthorisedCrewLock()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 10012);
        var engineer = state.Crew.Single(candidate => candidate.Name == "Sarah Chen");
        var scientist = state.Crew.Single(candidate => candidate.Name == "Emma Voss");
        var door = state.Facility.FindDoorBetween(
            "engineering",
            "hall-engineering")!;

        scientist.CurrentRoomId = "engineering";
        scientist.PositionX = 50;
        scientist.PositionY = 50;

        door.IsPowered = true;
        door.IsLocked = false;
        door.IsOpen = false;

        var interactions = new CrewDoorInteractionSystem();

        Assert.True(interactions.TryOperate(
            state,
            scientist,
            door,
            ActionKind.OpenDoor,
            out _));
        Assert.True(interactions.TryOperate(
            state,
            scientist,
            door,
            ActionKind.CloseDoor,
            out _));
        Assert.False(interactions.TryOperate(
            state,
            scientist,
            door,
            ActionKind.LockDoor,
            out _));

        Assert.True(CrewDoorInteractionSystem.HasLockAuthority(engineer));
        Assert.True(interactions.TryOperate(
            state,
            engineer,
            door,
            ActionKind.LockDoor,
            out _));
        Assert.True(door.IsLocked);
        Assert.True(interactions.TryOperate(
            state,
            engineer,
            door,
            ActionKind.UnlockDoor,
            out _));
        Assert.False(door.IsLocked);
    }

    [Fact]
    public void ExpandedAffordances_NormalizeTargetsAndRejectInvalidAuthority()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 10013);
        var scientist = state.Crew.Single(candidate => candidate.Name == "Emma Voss");
        var engineer = state.Crew.Single(candidate => candidate.Name == "Sarah Chen");
        var door = state.Facility.FindDoorBetween(
            "engineering",
            "hall-engineering")!;

        scientist.CurrentRoomId = "engineering";
        door.IsPowered = true;
        door.IsOpen = false;
        door.IsLocked = false;

        Assert.Contains(
            CrewAffordanceSystem.Catalog,
            item => item.Action == ActionKind.MisleadCrew);
        Assert.Contains(
            CrewAffordanceSystem.Catalog,
            item => item.Action == ActionKind.VerifyClaim);
        Assert.Contains(
            CrewAffordanceSystem.Catalog,
            item => item.Action == ActionKind.AssistCrew);

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state,
            scientist,
            ActionKind.CheckOnCrew,
            engineer.Name,
            out var personTarget));
        Assert.Equal(engineer.Name, personTarget);

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state,
            scientist,
            ActionKind.OpenDoor,
            door.Id,
            out var doorTarget));
        Assert.Equal(door.Id, doorTarget);

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state,
            scientist,
            ActionKind.LockDoor,
            door.Id,
            out _));

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state,
            scientist,
            ActionKind.CheckOnCrew,
            "Nobody Here",
            out _));
    }
}
