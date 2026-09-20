using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// The station layer (ScenarioProgressSystem) and the corporate layer
/// (CorporateDirectiveSystem) both grade a scenario. Neither may declare
/// victory on its own, or a player who has already failed a sponsor directive
/// would still be told the scenario was complete.
/// </summary>
public sealed class ScenarioOutcomeGateTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static void Advance(
        GameState state,
        ScenarioProgressSystem progress,
        CorporateDirectiveSystem directives,
        int minutes)
    {
        for (var i = 0; i < minutes && state.ScenarioStatus == ScenarioStatus.Running; i++)
        {
            state.Elapsed += Minute;
            progress.Tick(state, Minute);
            directives.Tick(state, Minute);
        }
    }

    [Fact]
    public void StationObjectivesAloneDoNotWinWhileADirectiveHasFailed()
    {
        var state = FacilitySeeder.CreateDefault();

        // Deniability is already lost: somebody is certain Overseer is hostile.
        state.Crew[0].OverseerSuspicion = 95;

        Advance(
            state,
            new ScenarioProgressSystem(),
            new CorporateDirectiveSystem(),
            (int)ScenarioCatalog.ObservationWindow.TotalMinutes + 5);

        Assert.Equal(ScenarioStatus.Failed, state.ScenarioStatus);
    }

    [Fact]
    public void AQuietStationSatisfiesBothLayersAndWins()
    {
        var state = FacilitySeeder.CreateDefault();

        Advance(
            state,
            new ScenarioProgressSystem(),
            new CorporateDirectiveSystem(),
            (int)ScenarioCatalog.ObservationWindow.TotalMinutes + 5);

        Assert.Equal(ScenarioStatus.Won, state.ScenarioStatus);
        Assert.True(CorporateDirectiveSystem.MandatoryDirectivesSatisfied(state));
    }

    [Fact]
    public void MandatoryDirectivesAreNotSatisfiedWhileStillBeingGraded()
    {
        var state = FacilitySeeder.CreateDefault();

        Assert.False(CorporateDirectiveSystem.MandatoryDirectivesSatisfied(state));
    }

    [Fact]
    public void AScenarioWithNoDirectivesStillResolvesOnItsStationObjectives()
    {
        var state = FacilitySeeder.CreateDefault();
        state.Directives.Clear();
        state.DirectiveProgress.Clear();

        Assert.True(CorporateDirectiveSystem.MandatoryDirectivesSatisfied(state));

        Advance(
            state,
            new ScenarioProgressSystem(),
            new CorporateDirectiveSystem(),
            (int)ScenarioCatalog.ObservationWindow.TotalMinutes + 5);

        Assert.Equal(ScenarioStatus.Won, state.ScenarioStatus);
    }

    [Fact]
    public void WitnessingAFaultIsNotAlsoChargedAsDiscoveringIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.CurrentRoomId = "medical";
        npc.OverseerEvidence.Clear();
        npc.OverseerSuspicion = 0;
        npc.ObservedFaults.Clear();

        var medical = state.Facility.Rooms["medical"];
        medical.IsPowered = false;

        // The player cut the power while this person was standing there.
        new SuspicionSystem().ObservePlayerRoomSystemChange(
            state, medical, "power", becameDisruptive: true, weight: 12);

        var afterWitnessing = npc.OverseerEvidence.Count;
        Assert.Equal(1, afterWitnessing);

        // The dynamics pass must not charge them a second time for the same act.
        new SuspicionDynamicsSystem().Tick(state, Minute);

        Assert.Equal(afterWitnessing, npc.OverseerEvidence.Count);
    }
}
