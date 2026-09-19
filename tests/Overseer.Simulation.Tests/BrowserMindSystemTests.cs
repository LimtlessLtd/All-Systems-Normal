using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class BrowserMindSystemTests
{
    [Fact]
    public void DangerousRoom_ImmediatelyPreemptsRoutineAndChoosesSaferDestination()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var control = state.Facility.Rooms["control"];

        control.OxygenPercent = 18.7;
        david.Intent = new NpcIntent(
            ActionKind.Work,
            "control",
            "Keep working.",
            "I am in the middle of routine duties.",
            35,
            "Test",
            state.Elapsed);
        david.RoutineUntil = TimeSpan.FromMinutes(60);
        state.Elapsed = TimeSpan.FromMinutes(1);

        new BrowserMindSystem().Tick(state);

        Assert.NotNull(david.Intent);
        Assert.Equal(ActionKind.Move, david.Intent!.Action);
        Assert.NotEqual("control", david.Intent.TargetId);
        Assert.Equal(100, david.Intent.Urgency);
        Assert.Equal(TimeSpan.Zero, david.RoutineUntil);
        Assert.NotNull(david.Bubble);
        Assert.Equal(NpcBubbleKind.Alert, david.Bubble!.Kind);

        var destination = state.Facility.Rooms[david.Intent.TargetId!];
        Assert.True(
            CrewEnvironmentSafety.RiskScore(destination)
            < CrewEnvironmentSafety.RiskScore(control));
    }

    [Fact]
    public void MultipleCrewInDanger_ReconsiderOnTheSameMinute()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");

        state.Facility.Rooms[david.CurrentRoomId].TemperatureC = 31;
        state.Facility.Rooms[sarah.CurrentRoomId].CarbonDioxidePercent = 1.4;
        state.Elapsed = TimeSpan.FromMinutes(1);

        new BrowserMindSystem().Tick(state);

        Assert.NotNull(david.Intent);
        Assert.NotNull(sarah.Intent);
        Assert.Equal(ActionKind.Move, david.Intent!.Action);
        Assert.Equal(ActionKind.Move, sarah.Intent!.Action);
        Assert.Equal(100, david.Intent.Urgency);
        Assert.Equal(100, sarah.Intent.Urgency);
    }

    [Fact]
    public void EmergencyDestinationMustBeReachableThroughPassableDoors()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var control = state.Facility.Rooms["control"];
        var exitDoor = state.Facility.FindDoorBetween("control", "hall-control")!;

        control.OxygenPercent = 18;
        exitDoor.IsOpen = false;
        exitDoor.IsLocked = true;
        state.Elapsed = TimeSpan.FromMinutes(1);

        new BrowserMindSystem().Tick(state);

        Assert.NotNull(david.Intent);
        Assert.Equal(ActionKind.Idle, david.Intent!.Action);
        Assert.Null(david.Intent.TargetId);
        Assert.Contains(
            "cannot identify a safer room",
            david.Intent.Reason,
            StringComparison.OrdinalIgnoreCase);
    }
}
