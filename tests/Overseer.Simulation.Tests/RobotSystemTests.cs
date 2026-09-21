using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class RobotSystemTests
{
    [Fact]
    public void DefaultStationSeedsExactlyOneAuthoritativeRobot()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);

        Assert.Equal("mr-1", robot.Id);
        Assert.Equal("engineering", robot.CurrentRoomId);
        Assert.Equal(RobotPolicy.Friendly, robot.Policy);
        Assert.True(robot.IsOperational);
    }

    [Fact]
    public void HostilePolicyCreatesPhysicalMovementWithoutTeleporting()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);
        var target = state.Crew.First();

        foreach (var npc in state.Crew)
        {
            npc.IsPresent = npc.Id == target.Id;
        }

        target.CurrentRoomId = "storage";
        robot.CurrentRoomId = "engineering";
        robot.PositionX = 50;
        robot.PositionY = 50;

        var system = new RobotSystem();
        Assert.True(system.TrySetPolicy(state, robot.Id, RobotPolicy.Hostile, out _));
        // A hostile robot may continue pursuing a previously acquired target
        // after LOS is broken, but it may not magically acquire one through walls.
        robot.TargetNpcId = target.Id;

        var healthBefore = target.Health;
        system.Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal("engineering", robot.CurrentRoomId);
        Assert.NotNull(robot.Movement);
        Assert.Equal(healthBefore, target.Health);
        Assert.Equal(target.Id, robot.TargetNpcId);
    }

    [Fact]
    public void RobotDoorCrossingRevalidatesPassabilityAtTheThreshold()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);
        var target = state.Crew.First();

        foreach (var npc in state.Crew)
        {
            npc.IsPresent = npc.Id == target.Id;
        }

        target.CurrentRoomId = "storage";
        robot.CurrentRoomId = "engineering";

        var robots = new RobotSystem();
        Assert.True(robots.TrySetPolicy(state, robot.Id, RobotPolicy.Hostile, out _));
        robot.TargetNpcId = target.Id;
        robots.Tick(state, TimeSpan.FromMinutes(1));

        var movement = Assert.IsType<NpcMovement>(robot.Movement);
        robot.PositionX = movement.ExitX;
        robot.PositionY = movement.ExitY;

        var door = state.Facility.Doors.Single(candidate => candidate.Id == movement.DoorId);
        door.IsOpen = false;
        door.IsLocked = true;

        new LocalMovementSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal("engineering", robot.CurrentRoomId);
        Assert.Null(robot.Movement);
        Assert.Contains("blocked", robot.CurrentTask, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostilePolicyCannotDamageUntilRobotIsPhysicallyInRange()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);
        var target = state.Crew.First();

        foreach (var npc in state.Crew)
        {
            npc.IsPresent = npc.Id == target.Id;
        }

        robot.CurrentRoomId = "engineering";
        target.CurrentRoomId = "engineering";
        robot.PositionX = 10;
        robot.PositionY = 10;
        target.PositionX = 90;
        target.PositionY = 90;

        var robots = new RobotSystem();
        Assert.True(robots.TrySetPolicy(state, robot.Id, RobotPolicy.Hostile, out _));
        var before = target.Health;

        robots.Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(before, target.Health);

        robot.PositionX = target.PositionX;
        robot.PositionY = target.PositionY;
        state.Elapsed += TimeSpan.FromMinutes(1);
        robots.Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(target.Health < before);
        Assert.Contains(
            target.OverseerEvidence,
            evidence => evidence.Description.Contains("physically attack", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FriendlyRobotCanRepairARealRoomFault()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);
        var generator = state.Facility.Rooms["generator"];

        robot.CurrentRoomId = "generator";
        robot.PositionX = 50;
        robot.PositionY = 50;
        generator.IsPowered = false;

        var robots = new RobotSystem();
        robots.Tick(state, TimeSpan.FromMinutes(1));

        Assert.NotNull(robot.ActionCompletesAt);
        Assert.False(generator.IsPowered);

        state.Elapsed += TimeSpan.FromMinutes(3);
        robots.Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(generator.IsPowered);
    }

    [Fact]
    public void CrewNetworkIsolationBlocksAllLaterRemoteRobotCommands()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);
        var engineer = state.Crew.First();
        engineer.CurrentRoomId = RobotCountermeasureSystem.ControlRoomId;
        engineer.Skills["Engineering"] = 100;

        var resolver = new ActionResolver();
        Assert.True(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.IsolateRobotNetwork, robot.Id, "Cut the remote link."),
            out _));

        var countermeasures = new RobotCountermeasureSystem();
        countermeasures.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(3);
        countermeasures.Tick(state);

        Assert.True(robot.IsNetworkIsolated);

        var robots = new RobotSystem();
        Assert.False(robots.TrySetPolicy(state, robot.Id, RobotPolicy.Hostile, out var policyMessage));
        Assert.False(robots.TryToggleRemoteShutdown(state, robot.Id, out var shutdownMessage));
        Assert.Contains("isolated", policyMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isolated", shutdownMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrewCanLocallyShutdownThenReprogramHostileRobot()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);
        var engineer = state.Crew.First();
        engineer.CurrentRoomId = robot.CurrentRoomId;
        engineer.Skills["Engineering"] = 100;

        robot.Policy = RobotPolicy.Hostile;

        var resolver = new ActionResolver();
        var countermeasures = new RobotCountermeasureSystem();

        Assert.True(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.ShutdownRobot, robot.Id, "Use the local cutoff."),
            out _));
        countermeasures.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(3);
        countermeasures.Tick(state);

        Assert.True(robot.IsLocallyShutdown);
        Assert.False(robot.IsOperational);

        Assert.True(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.ReprogramRobot, robot.Id, "Restore safe local policy."),
            out _));
        countermeasures.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(4);
        countermeasures.Tick(state);

        Assert.Equal(RobotPolicy.Friendly, robot.Policy);
        Assert.False(robot.IsLocallyShutdown);
        Assert.True(robot.IsOperational);
    }

    [Fact]
    public void CrewCanDenyChargingUntilRobotBatteryStopsIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);
        var engineer = state.Crew.First();
        engineer.CurrentRoomId = RobotCountermeasureSystem.ControlRoomId;
        engineer.Skills["Engineering"] = 100;

        var resolver = new ActionResolver();
        Assert.True(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.DisableRobotCharging, robot.Id, "Deny charging power."),
            out _));

        var countermeasures = new RobotCountermeasureSystem();
        countermeasures.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(2);
        countermeasures.Tick(state);

        Assert.False(robot.ChargingEnabled);

        robot.BatteryPercent = 0.1;
        robot.Policy = RobotPolicy.Neutral;
        new RobotSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(0, robot.BatteryPercent);
        Assert.False(robot.IsOperational);
    }

    [Fact]
    public void HostilePolicyChangeCreatesEvidenceOnlyForLocalWitnesses()
    {
        var state = FacilitySeeder.CreateDefault();
        var robot = Assert.Single(state.Robots);
        var witness = state.Crew[0];
        var remote = state.Crew[1];

        witness.CurrentRoomId = robot.CurrentRoomId;
        remote.CurrentRoomId = "medical";

        var robots = new RobotSystem();
        Assert.True(robots.TrySetPolicy(state, robot.Id, RobotPolicy.Hostile, out _));

        Assert.Contains(
            witness.OverseerEvidence,
            evidence => evidence.Description.Contains("hostile policy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            remote.OverseerEvidence,
            evidence => evidence.Description.Contains(robot.Name, StringComparison.OrdinalIgnoreCase));
    }
}
