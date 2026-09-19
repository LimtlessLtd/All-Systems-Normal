using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class ActionResolverTests
{
    [Fact]
    public void Move_SucceedsThroughAnOpenUnlockedDoor()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");

        var success = new ActionResolver().TryApply(
            state,
            sarah.Id,
            new NpcAction(
                ActionKind.Move,
                "corridor",
                "I need to reach the central corridor."),
            out _);

        Assert.True(success);
        Assert.Equal("corridor", sarah.CurrentRoomId);
    }

    [Fact]
    public void Move_FailsWhenTheDoorIsLocked()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var door = state.Facility.FindDoorBetween("engineering", "corridor")!;

        door.IsOpen = false;
        door.IsLocked = true;

        var success = new ActionResolver().TryApply(
            state,
            sarah.Id,
            new NpcAction(
                ActionKind.Move,
                "corridor",
                "I need to reach the central corridor."),
            out var message);

        Assert.False(success);
        Assert.Equal("engineering", sarah.CurrentRoomId);
        Assert.Contains("blocked", message, StringComparison.OrdinalIgnoreCase);
    }
}
