using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Deterministic foundation for owner idea #5 (crew-generated multi-step
/// plans): <see cref="NpcPlan"/>/<see cref="NpcPlanStep"/> plus
/// <see cref="PlanExecutionSystem"/>, which promotes queued steps into
/// <see cref="Npc.Intent"/> one at a time and is abandoned (not blindly
/// continued) if a step fails, expires or is pre-empted. Nothing produces an
/// <see cref="NpcPlan"/> yet; these tests exercise the mechanism directly.
/// </summary>
public sealed class PlanExecutionSystemTests
{
    private static GameState BuildState()
    {
        var state = new GameState { Facility = new Facility() };
        state.Facility.Rooms["room"] = new Room
        {
            Id = "room",
            Name = "Room",
            Type = RoomType.CrewQuarters
        };
        return state;
    }

    private static Npc BuildNpc() => new()
    {
        Name = "Observer",
        Role = CrewRole.Technician,
        Personality = new Personality(50, 50, 50, 50),
        CurrentRoomId = "room"
    };

    [Fact]
    public void Create_RejectsAnEmptyStepList()
    {
        Assert.Throws<ArgumentException>(() =>
            NpcPlan.Create([], "Test plan", 50, "Test"));
    }

    [Fact]
    public void Create_RejectsMoreThanMaxSteps()
    {
        var steps = Enumerable.Range(0, NpcPlan.MaxSteps + 1)
            .Select(_ => new NpcPlanStep(ActionKind.Idle, null, "Step"))
            .ToList();

        Assert.Throws<ArgumentException>(() =>
            NpcPlan.Create(steps, "Test plan", 50, "Test"));
    }

    [Fact]
    public void WithoutFirstStep_ReturnsNullOnceTheLastStepIsConsumed()
    {
        var single = NpcPlan.Create(
            [new NpcPlanStep(ActionKind.Idle, null, "Only step")],
            "Test plan",
            50,
            "Test");

        Assert.Null(single.WithoutFirstStep());
    }

    [Fact]
    public void WithoutFirstStep_KeepsTheRemainingStepsInOrder()
    {
        var plan = NpcPlan.Create(
            [
                new NpcPlanStep(ActionKind.Idle, null, "First"),
                new NpcPlanStep(ActionKind.Idle, null, "Second")
            ],
            "Test plan",
            50,
            "Test");

        var remaining = plan.WithoutFirstStep();

        Assert.NotNull(remaining);
        Assert.Single(remaining!.Steps);
        Assert.Equal("Second", remaining.Steps[0].Goal);
    }

    [Fact]
    public void Tick_PromotesTheFirstStepWhenTheNpcHasNoActiveIntent()
    {
        var state = BuildState();
        var npc = BuildNpc();
        state.Crew.Add(npc);
        npc.Plan = NpcPlan.Create(
            [
                new NpcPlanStep(ActionKind.Idle, null, "First step"),
                new NpcPlanStep(ActionKind.Idle, null, "Second step")
            ],
            "Pursuing a plan",
            77,
            "TestPlan");
        state.Elapsed = TimeSpan.FromMinutes(10);

        new PlanExecutionSystem().Tick(state);

        Assert.NotNull(npc.Intent);
        Assert.Equal("First step", npc.Intent!.Goal);
        Assert.Equal(ActionKind.Idle, npc.Intent.Action);
        Assert.Equal(77, npc.Intent.Urgency);
        Assert.Equal("Pursuing a plan", npc.Intent.Reason);
        Assert.Equal("TestPlan", npc.Intent.Source);
        Assert.Equal(state.Elapsed, npc.Intent.CreatedAt);
        Assert.NotNull(npc.Plan);
        Assert.Single(npc.Plan!.Steps);
        Assert.Equal("Second step", npc.Plan.Steps[0].Goal);
    }

    [Fact]
    public void Tick_DoesNotPromoteWhileAnIntentIsAlreadyActive()
    {
        var state = BuildState();
        var npc = BuildNpc();
        state.Crew.Add(npc);
        var existingIntent = new NpcIntent(
            ActionKind.Eat,
            null,
            "Already pursuing something",
            "Hungry",
            60,
            "Cognition",
            TimeSpan.Zero);
        npc.Intent = existingIntent;
        npc.Plan = NpcPlan.Create(
            [new NpcPlanStep(ActionKind.Idle, null, "Queued step")],
            "Pursuing a plan",
            77,
            "TestPlan");

        new PlanExecutionSystem().Tick(state);

        Assert.Same(existingIntent, npc.Intent);
        Assert.NotNull(npc.Plan);
        Assert.Single(npc.Plan!.Steps);
    }

    [Fact]
    public void PlanAndIntentExecutionTogether_WalkThroughEveryStepThenClearThePlan()
    {
        var state = BuildState();
        var npc = BuildNpc();
        state.Crew.Add(npc);
        npc.Plan = NpcPlan.Create(
            [
                new NpcPlanStep(ActionKind.Idle, null, "First step"),
                new NpcPlanStep(ActionKind.Idle, null, "Second step")
            ],
            "Pursuing a plan",
            77,
            "TestPlan");

        var planExecution = new PlanExecutionSystem();
        var intentExecution = new IntentExecutionSystem();

        // Tick 1: promote step 1, then execute it (Idle completes immediately).
        planExecution.Tick(state);
        Assert.Equal("First step", npc.Intent!.Goal);
        intentExecution.Tick(state);
        Assert.Null(npc.Intent);
        Assert.NotNull(npc.Plan);
        Assert.Single(npc.Plan!.Steps);

        // Tick 2: promote step 2, then execute it; the plan is now exhausted.
        planExecution.Tick(state);
        Assert.Equal("Second step", npc.Intent!.Goal);
        intentExecution.Tick(state);
        Assert.Null(npc.Intent);
        Assert.Null(npc.Plan);
    }

    [Fact]
    public void FailedStep_AbandonsTheRestOfThePlanInsteadOfContinuingIt()
    {
        var state = BuildState();
        var npc = BuildNpc();
        state.Crew.Add(npc);
        npc.Plan = NpcPlan.Create(
            [
                new NpcPlanStep(ActionKind.WeldDoor, "no-such-door", "Weld a door that does not exist"),
                new NpcPlanStep(ActionKind.Idle, null, "Would-be second step")
            ],
            "Pursuing a plan",
            77,
            "TestPlan");

        new PlanExecutionSystem().Tick(state);
        Assert.Equal(ActionKind.WeldDoor, npc.Intent!.Action);

        new IntentExecutionSystem().Tick(state);

        Assert.Null(npc.Intent);
        Assert.Null(npc.Plan);
        Assert.Contains(npc.Memories, memory => memory.IsFailedAttempt);
    }

    [Fact]
    public void ExpiredStep_AbandonsTheRestOfThePlan()
    {
        var state = BuildState();
        var npc = BuildNpc();
        state.Crew.Add(npc);
        npc.Intent = new NpcIntent(
            ActionKind.Idle,
            null,
            "Stale step",
            "Pursuing a plan",
            40,
            "TestPlan",
            TimeSpan.Zero);
        npc.Plan = NpcPlan.Create(
            [new NpcPlanStep(ActionKind.Idle, null, "Would-be next step")],
            "Pursuing a plan",
            40,
            "TestPlan");
        state.Elapsed = TimeSpan.FromMinutes(25);

        new IntentExecutionSystem().Tick(state);

        Assert.Null(npc.Intent);
        Assert.Null(npc.Plan);
    }
}
