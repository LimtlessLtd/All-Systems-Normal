using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner report 2026-09-23: "Crew never seem to actually sleep." Root cause:
/// during their sleep window, crew were repeatedly pulled out of bed for mild
/// hunger (routine at 55), ordinary chores (provisioning/maintenance
/// assignment), round-robin fallback thoughts and ambient chat, so a
/// 12-person station spent ~18% of sleep-window minutes actually in bed.
/// </summary>
public sealed class ScheduledSleepContinuityTests
{
    // Station clock starts at 06:00; T+17h is 23:00, inside the day cohort's
    // 22:00-06:00 sleep window, and divisible by both the 5-minute routine
    // cadence and the 6-minute browser-mind cadence.
    private static readonly TimeSpan DayCohortNight = TimeSpan.FromHours(17);

    private static Npc DayCohortSleeper(GameState state)
    {
        var npc = state.Crew.First(candidate =>
            !candidate.IsPrisoner && !CrewDutySchedule.IsNightShift(candidate));
        Assert.True(CrewDutySchedule.IsSleepWindow(npc, DayCohortNight));
        npc.Hunger = 20;
        npc.Fatigue = 10;
        npc.BladderNeed = 10;
        npc.HygieneNeed = 10;
        npc.RecreationNeed = 10;
        npc.SocialNeed = 10;
        return npc;
    }

    [Fact]
    public void Routine_OffShiftMildHunger_KeepsTheSleepPlan()
    {
        var state = FacilitySeeder.CreateDefault();
        state.Elapsed = DayCohortNight;
        var sleeper = DayCohortSleeper(state);
        sleeper.Hunger = 62;

        new CrewRoutineSystem().Tick(state);

        Assert.Contains("sleep", sleeper.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(ActionKind.Eat, sleeper.CurrentAction.Kind);
    }

    [Fact]
    public void Routine_OffShiftCriticalHunger_StillGetsUpToEat()
    {
        var state = FacilitySeeder.CreateDefault();
        state.Elapsed = DayCohortNight;
        var sleeper = DayCohortSleeper(state);
        sleeper.Hunger = CrewNeedThresholds.HungerCritical + 3;

        new CrewRoutineSystem().Tick(state);

        Assert.Contains("eat", sleeper.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Routine_OffShiftFullBladder_GoesToTheToiletBeforeBed()
    {
        var state = FacilitySeeder.CreateDefault();
        state.Elapsed = DayCohortNight;
        var sleeper = DayCohortSleeper(state);
        sleeper.BladderNeed = 80;

        new CrewRoutineSystem().Tick(state);

        Assert.Contains("washroom", sleeper.CurrentAction.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(62, 10, ActionKind.Sleep)]
    [InlineData(80, 10, ActionKind.Eat)]
    [InlineData(20, 85, ActionKind.UseToilet)]
    public void BrowserMind_OffShift_SleepsThroughMildNeedsButNotSeriousOnes(
        double hunger,
        double bladder,
        ActionKind expected)
    {
        var state = FacilitySeeder.CreateDefault();
        state.Elapsed = DayCohortNight;
        var sleeper = DayCohortSleeper(state);
        sleeper.Hunger = hunger;
        sleeper.BladderNeed = bladder;
        sleeper.NeedsMindReconsideration = true;

        new BrowserMindSystem().Tick(state);

        Assert.Equal(expected, sleeper.Intent?.Action);
    }

    [Theory]
    [InlineData(62, 10, ActionKind.Sleep)]
    [InlineData(80, 10, ActionKind.Eat)]
    [InlineData(20, 85, ActionKind.UseToilet)]
    public async Task RuleBasedFallback_MatchesBrowserMindOffShiftSleepChoice(
        double hunger,
        double bladder,
        ActionKind expected)
    {
        var state = FacilitySeeder.CreateDefault();
        state.Elapsed = DayCohortNight;
        var sleeper = DayCohortSleeper(state);
        sleeper.Hunger = hunger;
        sleeper.BladderNeed = bladder;

        var intent = await new RuleBasedAiDecisionService().DecideAsync(sleeper, state);

        Assert.Equal(expected, intent.Action);
    }

    [Fact]
    public void OnShiftMildHunger_IsUnchanged()
    {
        var state = FacilitySeeder.CreateDefault();
        state.Elapsed = TimeSpan.FromHours(4); // 10:00, day shift
        var worker = DayCohortSleeper(state);
        worker.Hunger = 62;
        worker.NeedsMindReconsideration = true;

        new BrowserMindSystem().Tick(state);

        Assert.Equal(ActionKind.Eat, worker.Intent?.Action);
    }

    [Fact]
    public void PhysicallyAsleepCrew_GetHungryAndNeedTheToiletMoreSlowly()
    {
        var state = FacilitySeeder.CreateDefault();
        state.Elapsed = DayCohortNight;
        var sleeper = DayCohortSleeper(state);
        var awake = state.Crew.First(npc => npc.Id != sleeper.Id && !npc.IsPrisoner);
        awake.Hunger = sleeper.Hunger;
        awake.BladderNeed = sleeper.BladderNeed;

        var quarters = state.Facility.Rooms["quarters"];
        var bed = quarters.Fixtures.First(fixture => fixture.Type == FixtureType.Bed);
        sleeper.CurrentRoomId = quarters.Id;
        sleeper.PositionX = bed.X;
        sleeper.PositionY = bed.Y;
        sleeper.CurrentAction = new NpcAction(ActionKind.Sleep, null, "Sleeping.");
        Assert.True(SimulationEngine.IsPhysicallyAsleep(state, sleeper));
        Assert.False(SimulationEngine.IsPhysicallyAsleep(state, awake));

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(60));

        Assert.Equal(20 + (60 * SimulationEngine.SleepingHungerPerMinute), sleeper.Hunger, 3);
        Assert.Equal(10 + (60 * SimulationEngine.SleepingBladderPerMinute), sleeper.BladderNeed, 3);
        Assert.True(awake.Hunger > sleeper.Hunger);
        Assert.True(awake.BladderNeed > sleeper.BladderNeed);
    }

    [Fact]
    public void RemoteSleepFlag_IsNotPhysicalSleep()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew.First(candidate => candidate.CurrentRoomId != "quarters");
        npc.CurrentAction = new NpcAction(ActionKind.Sleep, null, "Sleeping.");

        Assert.False(SimulationEngine.IsPhysicallyAsleep(state, npc));
    }

    [Fact]
    public void Maintenance_OffShiftCrewAreNotCalledOutForRoutineUpkeep()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Elapsed = DayCohortNight;
        foreach (var device in state.Devices.Values)
            device.Condition = 100;

        // Only off-shift crew remain present, so any assignment would wake one.
        foreach (var npc in state.Crew.Where(npc => !ScheduledSleepRules.IsOffShift(npc, state.Elapsed)))
            npc.IsPresent = false;

        var worn = state.Devices["lighting:medical"];
        worn.Condition = worn.DegradedAt - 20; // routine wear, below call-out urgency
        Assert.True(worn.ServiceUrgency < ScheduledSleepRules.OffShiftCallOutUrgency);

        new CrewMaintenanceSystem().Tick(state);
        Assert.DoesNotContain(state.Crew, npc => npc.ServicingDeviceId == worn.Id);

        worn.Condition = 0; // failed: a genuine call-out
        new CrewMaintenanceSystem().Tick(state);
        Assert.Contains(state.Crew, npc => npc.ServicingDeviceId == worn.Id);
    }

    [Fact]
    public void Provisioning_OffShiftCrewStayInBedUnlessTheGalleyIsEmpty()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        state.Elapsed = DayCohortNight;
        foreach (var device in state.Devices.Values)
            device.Condition = 100;
        foreach (var npc in state.Crew.Where(npc => !ScheduledSleepRules.IsOffShift(npc, state.Elapsed)))
            npc.IsPresent = false;

        state.Stores.Meals = 10;
        new CrewProvisioningSystem().Tick(state, TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(state.Crew, npc => npc.ProvisioningJob is not null);

        state.Stores.Meals = 0;
        new CrewProvisioningSystem().Tick(state, TimeSpan.FromMinutes(1));
        Assert.Contains(state.Crew, npc => npc.ProvisioningJob is not null);
    }
}
