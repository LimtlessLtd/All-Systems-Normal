using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class NavigationSystemTests
{
    [Fact]
    public void FindPath_CannotEnterRoomWhenItsHallwayDoorIsLocked()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlockDoor = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;

        airlockDoor.IsOpen = false;
        airlockDoor.IsLocked = true;

        var path = new NavigationSystem().FindPath(
            state.Facility,
            "corridor",
            "airlock");

        Assert.Empty(path);
    }

    [Fact]
    public void FindPath_UsesRoomHallwaySpineHallwayRoom()
    {
        var state = FacilitySeeder.CreateDefault();

        var path = new NavigationSystem().FindPath(
            state.Facility,
            "control",
            "reactor");

        Assert.Equal(
            new[]
            {
                "control",
                "hall-control",
                "corridor",
                "hall-reactor",
                "reactor"
            },
            path);
    }

    [Fact]
    public void LocalMovement_RecordsTheExactHallwayDoorCrossed()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        var success = new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "hall-airlock",
                "Heading toward the airlock."),
            out _);

        Assert.True(success);
        Assert.Equal("corridor", marcus.CurrentRoomId);

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(2));

        Assert.Equal("hall-airlock", marcus.CurrentRoomId);
        Assert.Contains(
            "door-hall-airlock-corridor",
            state.EventLog[0],
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "crosses",
            state.EventLog[0],
            StringComparison.OrdinalIgnoreCase);
    }
}
