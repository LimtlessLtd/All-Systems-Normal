using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewTaskProgressTests
{
    [Fact]
    public void AuthoritativeTaskProgressOnlyCompletesAtOneHundredPercent()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];

        CrewTaskSystem.Start(
            state,
            npc,
            ActionKind.DisarmTurret,
            "st-1",
            "disarming security turret",
            TimeSpan.FromMinutes(10));

        Assert.Equal(0, CrewTaskSystem.Progress(state, npc), 6);
        Assert.False(CrewTaskSystem.IsComplete(state, npc));

        state.Elapsed += TimeSpan.FromMinutes(5);
        Assert.Equal(50, CrewTaskSystem.Progress(state, npc), 6);
        Assert.False(CrewTaskSystem.IsComplete(state, npc));

        state.Elapsed += TimeSpan.FromMinutes(5);
        Assert.True(CrewTaskSystem.IsComplete(state, npc));

        CrewTaskSystem.Succeed(state, npc, "Turret safed.");
        Assert.Equal(CrewTaskStatus.Succeeded, npc.ActiveTask!.Status);
        Assert.Equal(100, npc.ActiveTask.ProgressPercent(state.Elapsed), 6);
        Assert.Contains("Turret safed", npc.ActiveTask.Outcome);
    }

    [Fact]
    public void LowPriorityThoughtCannotCasuallyAbandonCommittedPhysicalWork()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];
        npc.CurrentAction = new NpcAction(ActionKind.DisarmTurret, "st-1", "Committed work.");

        CrewTaskSystem.Start(
            state,
            npc,
            ActionKind.DisarmTurret,
            "st-1",
            "disarming security turret",
            TimeSpan.FromMinutes(10));

        npc.Intent = new NpcIntent(
            ActionKind.Rest,
            null,
            "Take a break.",
            "A passing low-priority thought.",
            30,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Null(npc.Intent);
        Assert.Equal(CrewTaskStatus.InProgress, npc.ActiveTask!.Status);
        Assert.Equal(ActionKind.DisarmTurret, npc.CurrentAction.Kind);
    }

    [Fact]
    public void LegitimateUrgentInterruptionRecordsWhyTheTaskStopped()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];
        npc.CurrentAction = new NpcAction(ActionKind.DisarmTurret, "st-1", "Committed work.");

        CrewTaskSystem.Start(
            state,
            npc,
            ActionKind.DisarmTurret,
            "st-1",
            "disarming security turret",
            TimeSpan.FromMinutes(10));

        npc.Intent = new NpcIntent(
            ActionKind.Move,
            "medical",
            "Get clear.",
            "Immediate emergency movement.",
            100,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(CrewTaskStatus.Interrupted, npc.ActiveTask!.Status);
        Assert.Contains("Pre-empted", npc.ActiveTask.Outcome, StringComparison.OrdinalIgnoreCase);
    }
}
