using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class LocalMovementSystemTests
{
    [Fact]
    public void Tick_MovesCrewTowardTheDoorBeforeCrossing()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "airlock",
                "Inspect the airlock."),
            out _);

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(1));

        Assert.Equal("corridor", marcus.CurrentRoomId);
        Assert.NotNull(marcus.Movement);
        Assert.True(marcus.PositionX < 50);
    }

    [Fact]
    public void Tick_RechecksDoorAtThresholdAndStopsIfPlayerSealsIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var door = state.Facility.FindDoorBetween("corridor", "airlock")!;

        new ActionResolver().TryApply(
            state,
            marcus.Id,
            new NpcAction(
                ActionKind.Move,
                "airlock",
                "Inspect the airlock."),
            out _);

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(1));

        door.IsOpen = false;
        door.IsLocked = true;

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(1));

        Assert.Equal("corridor", marcus.CurrentRoomId);
        Assert.Null(marcus.Movement);
        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.Contains(
            "sealed",
            marcus.CurrentAction.Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "cannot cross",
            state.EventLog[0],
            StringComparison.OrdinalIgnoreCase);
    }
}
