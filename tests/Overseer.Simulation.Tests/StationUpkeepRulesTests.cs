using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationUpkeepRulesTests
{
    private static (Npc Npc, StationDevice Device) QualifiedWorkerAndDevice()
    {
        var state = FacilitySeeder.CreateDefault();
        var device = state.Devices["lighting:medical"]; // Electrical, ServiceDifficulty 30
        var npc = state.Crew.First();
        npc.Skills["Electrical"] = 100;
        npc.Skills["Engineering"] = 0;
        npc.Fatigue = 0;
        npc.SleepDebtMinutes = 0;
        Assert.True(StationUpkeepRules.CanService(npc, device));
        return (npc, device);
    }

    private static (Npc Npc, StationDevice Device) UnderqualifiedButAttemptingWorkerAndDevice()
    {
        var state = FacilitySeeder.CreateDefault();
        var device = state.Devices["lighting:medical"]; // CanAttempt threshold 16.5, CanService 30
        var npc = state.Crew.First();
        npc.Skills["Electrical"] = 20;
        npc.Skills["Engineering"] = 0;
        npc.Fatigue = 0;
        npc.SleepDebtMinutes = 0;
        Assert.True(StationUpkeepRules.CanAttempt(npc, device));
        Assert.False(StationUpkeepRules.CanService(npc, device));
        return (npc, device);
    }

    [Fact]
    public void AStressedButQualifiedWorkerRestoresLessConditionThanACalmOne()
    {
        var (npc, device) = QualifiedWorkerAndDevice();

        npc.Stress = 10;
        var calm = StationUpkeepRules.RestorationBy(npc, device);

        npc.Stress = 100;
        var stressed = StationUpkeepRules.RestorationBy(npc, device);

        Assert.True(stressed < calm);
        Assert.Equal(StationUpkeepRules.ServiceRestoration, calm);
        Assert.Equal(StationUpkeepRules.ServiceRestoration * 0.7, stressed, precision: 6);
    }

    [Fact]
    public void StressBelowFiftyCostsNothing()
    {
        var (npc, device) = QualifiedWorkerAndDevice();

        npc.Stress = 0;
        var atZero = StationUpkeepRules.RestorationBy(npc, device);

        npc.Stress = 50;
        var atThreshold = StationUpkeepRules.RestorationBy(npc, device);

        Assert.Equal(StationUpkeepRules.ServiceRestoration, atZero);
        Assert.Equal(StationUpkeepRules.ServiceRestoration, atThreshold);
    }

    [Fact]
    public void StressStillPenalisesAnUnderqualifiedAttempt()
    {
        var (npc, device) = UnderqualifiedButAttemptingWorkerAndDevice();

        npc.Stress = 10;
        var calm = StationUpkeepRules.RestorationBy(npc, device);

        npc.Stress = 100;
        var stressed = StationUpkeepRules.RestorationBy(npc, device);

        Assert.Equal(StationUpkeepRules.ServiceRestoration * 0.5, calm);
        Assert.Equal(StationUpkeepRules.ServiceRestoration * 0.5 * 0.7, stressed, precision: 6);
    }
}
