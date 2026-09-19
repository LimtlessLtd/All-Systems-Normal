using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewRoutineSystemTests
{
    [Fact]
    public void Tick_SchedulesCrewMovementInsteadOfTeleporting()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        state.Elapsed = TimeSpan.FromMinutes(3);

        new CrewRoutineSystem().Tick(state);

        Assert.Equal("control", david.CurrentRoomId);
        Assert.NotNull(david.Movement);
        Assert.Equal("corridor", david.Movement.ToRoomId);
        Assert.Equal(ActionKind.Move, david.CurrentAction.Kind);

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(2));

        Assert.Equal("corridor", david.CurrentRoomId);
    }

    [Fact]
    public void Tick_DoesNotMoveThroughASealedRoute()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var reactorDoor = state.Facility.FindDoorBetween("engineering", "reactor")!;

        reactorDoor.IsOpen = false;
        reactorDoor.IsLocked = true;
        state.Elapsed = TimeSpan.FromMinutes(2);

        new CrewRoutineSystem().Tick(state);

        Assert.Equal("engineering", sarah.CurrentRoomId);
        Assert.Null(sarah.Movement);
        Assert.Equal(ActionKind.Idle, sarah.CurrentAction.Kind);
        Assert.Contains("sealed", sarah.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
