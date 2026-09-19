using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class NavigationSystemTests
{
    [Fact]
    public void FindPath_CannotEnterRoomWhoseOnlyDoorIsLocked()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlockDoor = state.Facility.FindDoorBetween("corridor", "airlock")!;

        airlockDoor.IsOpen = false;
        airlockDoor.IsLocked = true;

        var path = new NavigationSystem().FindPath(
            state.Facility,
            "corridor",
            "airlock");

        Assert.Empty(path);
    }

    [Fact]
    public void ActionResolver_RecordsTheExactDoorCrossed()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        var success = new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "airlock",
                "Inspecting the airlock."),
            out var message);

        Assert.True(success);
        Assert.Contains("door-airlock-corridor", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("door-airlock-corridor", state.EventLog[0], StringComparison.OrdinalIgnoreCase);
    }
}
