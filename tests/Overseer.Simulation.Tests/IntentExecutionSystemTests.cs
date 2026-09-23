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
    public void PersistentIntent_NamesOverseerAndRecordsSuspicionWhenOverseerLockedTheOnlyRoute()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var suspicionBefore = marcus.OverseerSuspicion;
        var door = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;
        var airlockName = state.Facility.Rooms["airlock"].Name;

        door.IsOpen = false;
        door.IsLocked = true;
        door.LockedByOverseer = true;

        marcus.Intent = new NpcIntent(
            ActionKind.Move,
            "airlock",
            "Inspect the airlock.",
            "I want to verify the outer hatch is secure.",
            70,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.Contains($"Overseer sealed {airlockName}", marcus.CurrentAction.Reason);
        Assert.True(marcus.OverseerSuspicion > suspicionBefore);
        Assert.Contains(
            marcus.OverseerEvidence,
            evidence => evidence.Claim == EvidenceClaim.AccessRestricted);
    }

    [Fact]
    public void PersistentIntent_KeepsTheGenericMessageWhenALockWasNotOverseerCaused()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var suspicionBefore = marcus.OverseerSuspicion;
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

        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.DoesNotContain("Overseer sealed", marcus.CurrentAction.Reason);
        Assert.Contains("every known route is sealed", marcus.CurrentAction.Reason);
        Assert.Equal(suspicionBefore, marcus.OverseerSuspicion);
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

    [Fact]
    public void DoorOperationIntent_InterruptsAStaleInProgressTaskWhenNoLongerAdjacent()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var door = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;

        marcus.CurrentRoomId = "airlock";
        marcus.Intent = new NpcIntent(
            ActionKind.OpenDoor,
            door.Id,
            "Open the airlock hatch.",
            "I need this hatch open.",
            60,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.NotNull(marcus.ActiveTask);
        Assert.Equal(CrewTaskStatus.InProgress, marcus.ActiveTask!.Status);
        Assert.Equal(ActionKind.OpenDoor, marcus.ActiveTask.Action);

        // Simulate a physical invalidation mid-task (e.g. PrisonerContainmentSystem
        // teleporting a captured NPC's CurrentRoomId away) rather than completing
        // or abandoning the door operation normally.
        marcus.CurrentRoomId = "reactor";

        new IntentExecutionSystem().Tick(state);

        Assert.NotEqual(CrewTaskStatus.InProgress, marcus.ActiveTask!.Status);
        Assert.False(CrewTaskSystem.IsWorking(marcus));
        Assert.Null(marcus.Intent);
    }

    [Fact]
    public void FailedIntent_LeavesAMemoryAndRaisesStress()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var stressBefore = marcus.Stress;
        var memoriesBefore = marcus.Memories.Count;

        marcus.Intent = new NpcIntent(
            ActionKind.Investigate,
            "no-such-room",
            "Look into it.",
            "I want to check that out.",
            60,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Null(marcus.Intent);
        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.Equal(memoriesBefore + 1, marcus.Memories.Count);
        Assert.Equal(marcus.CurrentAction.Reason, marcus.Memories[^1].Description);
        Assert.Equal(state.Elapsed, marcus.Memories[^1].OccurredAt);
        Assert.True(marcus.Stress > stressBefore);
    }

    [Fact]
    public void ProposePactIntent_SetsCurrentActionImmediatelyWhenAlreadyCoLocated()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");
        emma.CurrentRoomId = marcus.CurrentRoomId;

        marcus.Intent = new NpcIntent(
            ActionKind.ProposePact,
            emma.Name,
            "Make a deal with Emma.",
            "I'll cover your shift if you keep quiet about this.",
            50,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(ActionKind.ProposePact, marcus.CurrentAction.Kind);
        Assert.Equal(emma.Name, marcus.CurrentAction.TargetId);
        Assert.Equal("I'll cover your shift if you keep quiet about this.", marcus.CurrentAction.Reason);
        Assert.Null(marcus.Intent);
    }

    [Fact]
    public void AcceptPactIntent_RequiresAMatchingPendingProposal()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        marcus.Intent = new NpcIntent(
            ActionKind.AcceptPact,
            emma.Name,
            "Agree to Emma's offer.",
            "Sounds fair.",
            50,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.Null(marcus.Intent);

        marcus.PendingPactProposal = new PactProposal(
            emma.Id,
            emma.Name,
            CrewPactKind.Other,
            "I'll cover your shift.",
            TriggerAt: null,
            Deadline: null,
            OfferedAt: state.Elapsed);
        marcus.Intent = new NpcIntent(
            ActionKind.AcceptPact,
            emma.Name,
            "Agree to Emma's offer.",
            "Sounds fair.",
            50,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(ActionKind.AcceptPact, marcus.CurrentAction.Kind);
        Assert.Equal(emma.Name, marcus.CurrentAction.TargetId);
        Assert.Null(marcus.Intent);
    }

    [Fact]
    public void FulfillPactIntent_RequiresAMatchingActivePactWhereThisNpcIsThePromisor()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        marcus.Intent = new NpcIntent(
            ActionKind.FulfillPact,
            "pact-0001",
            "Keep my promise to Emma.",
            "I said I would.",
            50,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(ActionKind.Idle, marcus.CurrentAction.Kind);
        Assert.Null(marcus.Intent);

        Assert.True(CrewPactSystem.TryCreate(
            state, marcus.Id, emma.Id, CrewPactKind.Other,
            "I'll cover your shift.", null, null, out var pact, out _));

        marcus.Intent = new NpcIntent(
            ActionKind.FulfillPact,
            pact!.Id,
            "Keep my promise to Emma.",
            "I said I would.",
            50,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(ActionKind.FulfillPact, marcus.CurrentAction.Kind);
        Assert.Equal(pact.Id, marcus.CurrentAction.TargetId);
        Assert.Null(marcus.Intent);
    }

    [Fact]
    public void BreakPactIntent_RejectsAPactWhereThisNpcIsNotThePromisor()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        Assert.True(CrewPactSystem.TryCreate(
            state, marcus.Id, emma.Id, CrewPactKind.Other,
            "I'll cover your shift.", null, null, out var pact, out _));

        emma.Intent = new NpcIntent(
            ActionKind.BreakPact,
            pact!.Id,
            "Cancel the deal.",
            "I never agreed to owe them anything.",
            50,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(ActionKind.Idle, emma.CurrentAction.Kind);
        Assert.Null(emma.Intent);
        Assert.Equal(CrewPactStatus.Active, pact.Status);
    }
}
