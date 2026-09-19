using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class IntentExecutionSystemTests
{
    [Fact]
    public void PersistentIntent_CannotCrossALockedDoor()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var door = state.Facility.FindDoorBetween("corridor", "airlock")!;

        door.IsOpen = false;
        door.IsLocked = true;

        marcus.Intent = new NpcIntent(
            ActionKind.Move,
            "airlock",
            "Inspect the airlock.",
            "I want to verify the outer hatch is secure.",
            70,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal("corridor", marcus.CurrentRoomId);
        Assert.NotNull(marcus.Intent);
        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.Contains("sealed", marcus.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SocialIntent_WalksTowardTheTargetOneLegalRoomAtATime()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        marcus.CurrentRoomId = "airlock";
        emma.CurrentRoomId = "reactor";

        marcus.Intent = new NpcIntent(
            ActionKind.Talk,
            emma.Name,
            "Find Emma and talk.",
            "I need to ask Emma what she saw.",
            55,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal("corridor", marcus.CurrentRoomId);
        Assert.NotNull(marcus.Intent);
        Assert.Contains("Emma Voss", marcus.CurrentAction.Reason);
    }
}
