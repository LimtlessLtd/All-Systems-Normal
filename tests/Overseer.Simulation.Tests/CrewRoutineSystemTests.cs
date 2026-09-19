using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewRoutineSystemTests
{
    [Fact]
    public void HungryCrewBeginWalkingTowardFoodThroughTheirHallway()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        david.Hunger = 70;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new CrewRoutineSystem().Tick(state);

        Assert.Equal("control", david.CurrentRoomId);
        Assert.NotNull(david.Movement);
        Assert.Equal("hall-control", david.Movement.ToRoomId);
        Assert.Equal(ActionKind.Move, david.CurrentAction.Kind);
        Assert.NotNull(david.Bubble);
        Assert.Contains("food", david.Bubble!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoutineCannotLeaveARoomWhenItsHallwayDoorIsSealed()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var door = state.Facility.FindDoorBetween("engineering", "hall-engineering")!;

        door.IsOpen = false;
        door.IsLocked = true;
        sarah.Hunger = 70;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new CrewRoutineSystem().Tick(state);

        Assert.Equal("engineering", sarah.CurrentRoomId);
        Assert.Null(sarah.Movement);
        Assert.Equal(ActionKind.Idle, sarah.CurrentAction.Kind);
        Assert.Contains("sealed", sarah.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HygieneNeedChoosesTheWashroom()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");

        nadia.HygieneNeed = 80;
        nadia.Hunger = 0;
        nadia.Fatigue = 0;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new CrewRoutineSystem().Tick(state);

        Assert.NotNull(nadia.Movement);
        Assert.Equal("hall-medical", nadia.Movement.ToRoomId);
        Assert.Contains("shower", nadia.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
