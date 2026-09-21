using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class IntentExecutionSystemTests
{
    [Fact]
    public void PersistentIntent_CannotReachARoomWithALockedHallwayDoor()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var door = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;

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
        Assert.Null(marcus.Movement);
        Assert.NotNull(marcus.Intent);
        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.Contains("sealed", marcus.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SocialIntent_WalksTowardTheTargetOneLegalSpaceAtATime()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        marcus.CurrentRoomId = "airlock";
        emma.CurrentRoomId = "reactor";
        var innerAirlockDoor = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;
        innerAirlockDoor.IsOpen = true;

        marcus.Intent = new NpcIntent(
            ActionKind.Talk,
            emma.Name,
            "Find Emma and talk.",
            "I need to ask Emma what she saw.",
            55,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal("airlock", marcus.CurrentRoomId);
        Assert.NotNull(marcus.Movement);
        Assert.Equal("hall-airlock", marcus.Movement.ToRoomId);
        Assert.NotNull(marcus.Intent);
        Assert.Contains("Emma Voss", marcus.CurrentAction.Reason);

        new LocalMovementSystem().Tick(
            state,
            TimeSpan.FromMinutes(2));

        Assert.Equal("hall-airlock", marcus.CurrentRoomId);
        Assert.Null(marcus.Movement);
        Assert.NotNull(marcus.Intent);
    }

    [Fact]
    public void HungerIntent_PersistsLongEnoughForPhysicalStationTraversal()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        marcus.CurrentRoomId = "storage";
        marcus.Intent = new NpcIntent(
            ActionKind.Eat,
            null,
            "Get a proper meal.",
            "I am very hungry.",
            75,
            "Test",
            state.Elapsed);

        state.Elapsed += TimeSpan.FromMinutes(30);

        new IntentExecutionSystem().Tick(state);

        Assert.NotNull(marcus.Intent);
        Assert.Equal(ActionKind.Eat, marcus.Intent!.Action);
        Assert.True(
            marcus.Movement is not null
            || marcus.CurrentRoomId.Equals("kitchen", StringComparison.OrdinalIgnoreCase));
    }

}
