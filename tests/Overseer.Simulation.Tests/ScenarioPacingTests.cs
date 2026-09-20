using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Guards against the station appearing to freeze.
///
/// A scenario that resolves early stops the clock, which from the player's side
/// looks exactly like the crew breaking: everybody stops mid-stride and nothing
/// happens again. These tests pin down that a run lasts a full shift and that
/// the crew are still doing things throughout it.
/// </summary>
public sealed class ScenarioPacingTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>Mirrors the tick order the live session uses.</summary>
    private sealed class Station
    {
        private readonly SimulationEngine _simulation = new();
        private readonly EnvironmentSystem _environment = new();
        private readonly AirlockSafetySystem _airlock = new();
        private readonly VacuumConsequenceSystem _vacuum = new();
        private readonly MissingPersonSystem _missing = new();
        private readonly CrewCounterplaySystem _counterplay = new();
        private readonly CrewRoutineSystem _routines = new();
        private readonly SocialSimulationSystem _social = new();
        private readonly BrowserMindSystem _browserMind = new();
        private readonly IntentExecutionSystem _intents = new();
        private readonly LocalMovementSystem _movement = new();
        private readonly SuspicionSystem _suspicion = new();
        private readonly ShutdownSystem _shutdown = new();
        private readonly ManualOverrideSystem _overrides = new();
        private readonly ConversationPacingSystem _pacing = new();
        private readonly ScenarioProgressSystem _scenarioProgress = new();
        private readonly OverseerCommsSystem _comms = new();
        private readonly CrewAccountComparisonSystem _comparison = new();
        private readonly SuspicionDynamicsSystem _dynamics = new();
        private readonly CorporateDirectiveSystem _directives = new();

        public void Tick(GameState state)
        {
            _environment.Tick(state, Minute);
            _airlock.Tick(state, Minute);
            _vacuum.Tick(state);
            _simulation.Tick(state, Minute);
            _missing.Tick(state);
            _browserMind.Tick(state);
            _intents.Tick(state);
            _counterplay.Tick(state);
            _overrides.Tick(state);
            _social.Tick(state);
            _suspicion.Tick(state);
            _pacing.Tick(state);
            _routines.Tick(state);
            _movement.Tick(state, Minute);
            _shutdown.Tick(state);
            _comms.Tick(state);
            _comparison.Tick(state);
            _dynamics.Tick(state, Minute);
            _directives.Tick(state, Minute);
            _scenarioProgress.Tick(state, Minute);
        }
    }

    [Fact]
    public void AScenarioRunsForAFullShiftRatherThanEndingWithinASecondOfRealTime()
    {
        // At 4x speed one simulated minute is 0.85s of real time, so anything
        // much under an hour of simulation is over before the player has read
        // the directive board.
        Assert.True(
            ScenarioCatalog.ObservationWindow >= TimeSpan.FromHours(4),
            $"Observation window {ScenarioCatalog.ObservationWindow} is too short to play.");
    }

    [Fact]
    public void TheStationSurvivalObjectiveTracksTheObservationWindow()
    {
        var state = FacilitySeeder.CreateDefault();

        var survive = state.Scenario!.Objectives
            .Single(objective => objective.Kind == ScenarioObjectiveKind.SurviveMinutes);

        Assert.Equal(ScenarioCatalog.ObservationWindow.TotalMinutes, survive.Target);
    }

    [Fact]
    public void CrewKeepActingForTheWholeRunAndTheRunEndsCleanly()
    {
        var state = FacilitySeeder.CreateDefault();
        var station = new Station();

        // The crew start the shift unassigned and pick up routines over the
        // first few minutes, so the warm-up is not a stall.
        const int WarmUpMinutes = 5;

        var window = (int)ScenarioCatalog.ObservationWindow.TotalMinutes;
        var stalledMinutes = new List<int>();
        var minutesRun = 0;

        for (var minute = 1; minute <= window + 20; minute++)
        {
            if (state.ScenarioStatus != ScenarioStatus.Running)
            {
                break;
            }

            // SimulationEngine.Tick owns the clock; advancing it here as well
            // would move the station two minutes per iteration.
            station.Tick(state);
            minutesRun = minute;

            // "Everybody stopped" means nobody moving and nobody holding a goal.
            var busy = state.Crew.Count(npc =>
                npc.IsAlive
                && (npc.Movement is not null
                    || npc.Intent is not null
                    || npc.CurrentAction.Kind != ActionKind.Idle));

            if (busy == 0 && minute > WarmUpMinutes)
            {
                stalledMinutes.Add(minute);
            }
        }

        Assert.True(
            minutesRun >= window,
            $"Run ended after {minutesRun} min, before the {window} min window.");

        Assert.True(
            stalledMinutes.Count == 0,
            "The whole crew stopped acting at T+"
            + string.Join(", T+", stalledMinutes.Take(10)));

        Assert.NotEqual(ScenarioStatus.Running, state.ScenarioStatus);
    }

    [Fact]
    public void AWinningRunFinalisesTheStationObjectivesAndNotJustTheDirectives()
    {
        var state = FacilitySeeder.CreateDefault();
        var station = new Station();

        var window = (int)ScenarioCatalog.ObservationWindow.TotalMinutes;

        for (var minute = 1; minute <= window + 20 && state.ScenarioStatus == ScenarioStatus.Running; minute++)
        {
            station.Tick(state);
        }

        Assert.Equal(ScenarioStatus.Won, state.ScenarioStatus);

        // The corporate layer used to declare victory first and skip this
        // bookkeeping entirely, leaving a winning run showing incomplete
        // optional objectives and no telemetry score.
        Assert.All(
            state.Scenario!.Objectives,
            objective => Assert.True(
                state.ObjectiveProgress[objective.Id].IsComplete,
                $"{objective.Id} was not finalised on a winning run."));

        Assert.True(state.Telemetry.Score > 0);
        Assert.NotNull(state.ScenarioOutcome);
    }

    [Fact]
    public void AFailedMandatoryDirectiveEndsTheRunWithoutWaitingForTheRest()
    {
        var state = FacilitySeeder.CreateDefault();
        var station = new Station();

        // Continuity is lost. Deniability and the supplementary directives are
        // still being collected, and previously the run carried on until every
        // one of them had been graded — leaving the player playing out a shift
        // they had already lost.
        CorporateDirectiveSystem.OnOverseerIsolated(state);

        var deniability = state.Directives.Single(d =>
            d.Kind == DirectiveKind.MaintainDeniability);
        Assert.Equal(
            DirectiveStatus.Active,
            CorporateDirectiveSystem.Progress(state, deniability).Status);

        station.Tick(state);

        Assert.Equal(ScenarioStatus.Failed, state.ScenarioStatus);
        Assert.True(state.Elapsed < ScenarioCatalog.ObservationWindow);
    }

    [Fact]
    public void ABreachedDeniabilityCanStillBeRecoveredBeforeTheDeadline()
    {
        var state = FacilitySeeder.CreateDefault();
        var station = new Station();

        // Deniability is graded at the deadline on purpose: evidence decays, so
        // a bad moment early in a shift must not be an instant loss.
        foreach (var npc in state.Crew)
        {
            SuspicionSystem.AddEvidence(
                state, npc, "Overseer sealed a hatch in my face.", 90);
        }

        station.Tick(state);

        Assert.Equal(ScenarioStatus.Running, state.ScenarioStatus);
        Assert.Equal(
            DirectiveStatus.Active,
            CorporateDirectiveSystem.Progress(
                state,
                state.Directives.Single(d => d.Kind == DirectiveKind.MaintainDeniability)).Status);
    }
}
