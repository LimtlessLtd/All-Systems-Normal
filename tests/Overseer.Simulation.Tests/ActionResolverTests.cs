using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class ActionResolverTests
{
    [Fact]
    public void Move_SchedulesPhysicalMovementThroughAnOpenUnlockedDoor()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");

        var success = new ActionResolver().TryApply(
            state,
            sarah.Id,
            new NpcAction(
                ActionKind.Move,
                "hall-engineering",
                "I need to leave Engineering."),
            out _);

        Assert.True(success);
        Assert.Equal("engineering", sarah.CurrentRoomId);
        Assert.NotNull(sarah.Movement);
        Assert.Equal("hall-engineering", sarah.Movement.ToRoomId);
        Assert.Equal(ActionKind.Move, sarah.CurrentAction.Kind);
    }

    [Fact]
    public void Move_FailsWhenTheRoomEndOfAHallwayIsLocked()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var door = state.Facility.FindDoorBetween("engineering", "hall-engineering")!;

        door.IsOpen = false;
        door.IsLocked = true;

        var success = new ActionResolver().TryApply(
            state,
            sarah.Id,
            new NpcAction(
                ActionKind.Move,
                "hall-engineering",
                "I need to leave Engineering."),
            out var message);

        Assert.False(success);
        Assert.Equal("engineering", sarah.CurrentRoomId);
        Assert.Null(sarah.Movement);
        Assert.Contains("blocked", message, StringComparison.OrdinalIgnoreCase);
    }
}
