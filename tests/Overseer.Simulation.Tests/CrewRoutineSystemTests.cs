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
    public void UrgentBladderNeedChoosesTheWashroom()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        david.BladderNeed = 90;
        david.Hunger = 0;
        david.Fatigue = 0;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new CrewRoutineSystem().Tick(state);

        Assert.NotNull(david.Movement);
        Assert.Equal("hall-control", david.Movement.ToRoomId);
        Assert.Contains("washroom", david.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoutineDutyRotation_MovesCrewBeyondTheirHomeRooms()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        david.Hunger = 0;
        david.Fatigue = 0;
        david.BladderNeed = 0;
        david.HygieneNeed = 0;
        david.RecreationNeed = 0;
        david.SocialNeed = 0;
        david.IntimacyNeed = 0;
        david.RoutineUntil = TimeSpan.Zero;
        state.Elapsed = TimeSpan.FromMinutes(30);

        new CrewRoutineSystem().Tick(state);

        Assert.NotNull(david.Movement);
        Assert.Equal("hall-control", david.Movement!.ToRoomId);
        Assert.Contains("Medical", david.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlreadyWorkingInScheduledDutyRoom_IsNotReissuedOrGivenAnotherBubble()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        state.Elapsed = TimeSpan.FromMinutes(30);
        david.Hunger = 0;
        david.Fatigue = 0;
        david.BladderNeed = 0;
        david.HygieneNeed = 0;
        david.RecreationNeed = 0;
        david.SocialNeed = 0;
        david.IntimacyNeed = 0;
        david.RoutineUntil = TimeSpan.Zero;
        david.CurrentRoomId = CrewDutySchedule.ExpectedDutyRoomId(david.Role, state.Elapsed);
        david.CurrentAction = new NpcAction(ActionKind.Work, david.CurrentRoomId, "Already on duty.");
        david.Bubble = null;

        new CrewRoutineSystem().Tick(state);

        Assert.Equal(ActionKind.Work, david.CurrentAction.Kind);
        Assert.Equal("Already on duty.", david.CurrentAction.Reason);
        Assert.Null(david.Bubble);
        Assert.Null(david.Movement);
    }

    [Fact]
    public void UrgentHunger_InterruptsOrdinaryDutyWork()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        state.Elapsed = TimeSpan.FromMinutes(30);
        david.Hunger = 60;
        david.Fatigue = 0;
        david.BladderNeed = 0;
        david.HygieneNeed = 0;
        david.RecreationNeed = 0;
        david.SocialNeed = 0;
        david.RoutineUntil = TimeSpan.Zero;
        david.CurrentRoomId = CrewDutySchedule.ExpectedDutyRoomId(david.Role, state.Elapsed);
        david.CurrentAction = new NpcAction(ActionKind.Work, david.CurrentRoomId, "Already on duty.");

        new CrewRoutineSystem().Tick(state);

        Assert.NotEqual(ActionKind.Work, david.CurrentAction.Kind);
        Assert.Equal("kitchen", david.PlannedDestinationRoomId);
    }

    [Fact]
    public void UrgentHunger_InterruptsAnActiveTimedRoutineHold()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        state.Elapsed = TimeSpan.FromMinutes(30);
        david.Hunger = 70;
        david.Fatigue = 0;
        david.BladderNeed = 0;
        david.HygieneNeed = 0;
        david.RecreationNeed = 0;
        david.SocialNeed = 0;
        david.RoutineUntil = state.Elapsed + TimeSpan.FromHours(2);
        david.CurrentAction = new NpcAction(ActionKind.Work, david.CurrentRoomId, "Routine task in progress.");

        new CrewRoutineSystem().Tick(state);

        Assert.Equal("kitchen", david.PlannedDestinationRoomId);
        Assert.NotEqual(ActionKind.Work, david.CurrentAction.Kind);
    }

    [Fact]
    public void RoutineDutyTravel_UsesContextualChatterInsteadOfBackToWork()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        david.Hunger = 0;
        david.Fatigue = 0;
        david.BladderNeed = 0;
        david.HygieneNeed = 0;
        david.RecreationNeed = 0;
        david.SocialNeed = 0;
        david.IntimacyNeed = 0;
        david.RoutineUntil = TimeSpan.Zero;
        state.Elapsed = TimeSpan.FromMinutes(30);

        new CrewRoutineSystem().Tick(state);

        Assert.NotNull(david.Bubble);
        Assert.DoesNotContain("back to work", david.Bubble!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MutualIntimacyNeedCoordinatesBothPeopleTowardPrivacy()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var felix = state.Crew.Single(npc => npc.Name == "Felix Ward");

        sarah.IntimacyNeed = 80;
        felix.IntimacyNeed = 80;
        sarah.Hunger = felix.Hunger = 0;
        sarah.Fatigue = felix.Fatigue = 0;
        sarah.BladderNeed = felix.BladderNeed = 0;
        sarah.HygieneNeed = felix.HygieneNeed = 0;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new CrewRoutineSystem().Tick(state);

        Assert.NotNull(sarah.Intent);
        Assert.NotNull(felix.Intent);
        Assert.Equal(ActionKind.Intimacy, sarah.Intent!.Action);
        Assert.Equal(ActionKind.Intimacy, felix.Intent!.Action);
        Assert.Equal(felix.Name, sarah.Intent.TargetId);
        Assert.Equal(sarah.Name, felix.Intent.TargetId);
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
