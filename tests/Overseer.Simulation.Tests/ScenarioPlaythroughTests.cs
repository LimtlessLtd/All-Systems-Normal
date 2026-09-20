using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Drives the full deterministic system stack for a complete observation
/// window. These are the end-to-end guards that a session can actually reach a
/// terminal state rather than running forever.
/// </summary>
public sealed class ScenarioPlaythroughTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private sealed class Harness
    {
        private readonly SimulationEngine _simulation = new();
        private readonly EnvironmentSystem _environment = new();
        private readonly AirlockSafetySystem _airlockSafety = new();
        private readonly VacuumConsequenceSystem _vacuum = new();
        private readonly MissingPersonSystem _missingPeople = new();
        private readonly CrewCounterplaySystem _counterplay = new();
        private readonly CrewRoutineSystem _crewRoutines = new();
        private readonly SocialSimulationSystem _social = new();
        private readonly BrowserMindSystem _browserMind = new();
        private readonly IntentExecutionSystem _intentExecution = new();
        private readonly LocalMovementSystem _movement = new();
        private readonly SuspicionSystem _suspicion = new();
        private readonly ShutdownSystem _shutdown = new();
        private readonly ManualOverrideSystem _manualOverrides = new();
        private readonly ConversationPacingSystem _conversationPacing = new();
        private readonly ScenarioProgressSystem _scenarioProgress = new();
        private readonly CorporateDirectiveSystem _directives = new();
        private readonly SuspicionDynamicsSystem _suspicionDynamics = new();

        public void Tick(GameState state)
        {
            if (state.ScenarioStatus != ScenarioStatus.Running)
            {
                return;
            }

            _environment.Tick(state, Minute);
            _airlockSafety.Tick(state, Minute);
            _vacuum.Tick(state);
            _simulation.Tick(state, Minute);
            _missingPeople.Tick(state);
            _browserMind.Tick(state);
            _intentExecution.Tick(state);
            _counterplay.Tick(state);
            _manualOverrides.Tick(state);
            _social.Tick(state);
            _suspicion.Tick(state);
            _conversationPacing.Tick(state);
            _crewRoutines.Tick(state);
            _movement.Tick(state, Minute);
            _shutdown.Tick(state);
            _scenarioProgress.Tick(state, Minute);
            _suspicionDynamics.Tick(state, Minute);
            _directives.Tick(state, Minute);
        }

        public void Run(GameState state, int minutes)
        {
            for (var i = 0; i < minutes && state.ScenarioStatus == ScenarioStatus.Running; i++)
            {
                Tick(state);
            }
        }
    }

    [Fact]
    public void AnUninterferedStationCompletesTheObservationWindow()
    {
        var state = FacilitySeeder.CreateDefault();
        var harness = new Harness();

        // The player does nothing at all. A passive Overseer should satisfy the
        // mandatory directives: it stays online and nobody has cause to suspect it.
        harness.Run(state, (int)ScenarioCatalog.ObservationWindow.TotalMinutes + 5);

        Assert.Equal(ScenarioStatus.Won, state.ScenarioStatus);
        Assert.NotNull(state.ScenarioOutcome);

        var continuity = state.Directives.Single(d => d.Kind == DirectiveKind.MaintainContinuity);
        Assert.Equal(
            DirectiveStatus.Completed,
            CorporateDirectiveSystem.Progress(state, continuity).Status);
    }

    [Fact]
    public void TheScenarioAlwaysReachesATerminalStateWithinTheWindow()
    {
        var state = FacilitySeeder.CreateDefault();
        var harness = new Harness();

        harness.Run(state, (int)ScenarioCatalog.ObservationWindow.TotalMinutes + 30);

        Assert.NotEqual(ScenarioStatus.Running, state.ScenarioStatus);
    }

    [Fact]
    public void BlatantHostilityCostsDeniabilityAndLosesTheScenario()
    {
        var state = FacilitySeeder.CreateDefault();
        var harness = new Harness();

        // Sustained hostility across the whole shift. Evidence decays, so a
        // player who is blatant once and then behaves is meant to recover —
        // testing the loss condition means keeping it up.
        var window = (int)ScenarioCatalog.ObservationWindow.TotalMinutes;

        for (var incident = 0; incident * 15 < window + 15; incident++)
        {
            if (state.ScenarioStatus != ScenarioStatus.Running)
            {
                break;
            }

            foreach (var npc in state.Crew.Where(n => n.IsAlive))
            {
                SuspicionSystem.AddEvidence(
                    state,
                    npc,
                    $"I watched Overseer seal a hatch in my face. (incident {incident})",
                    20);
            }

            harness.Run(state, 15);
        }

        Assert.Equal(ScenarioStatus.Failed, state.ScenarioStatus);
        Assert.True(state.ComplianceScore < 100);
    }
}
