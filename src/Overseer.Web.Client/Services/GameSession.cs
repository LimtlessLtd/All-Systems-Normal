using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Web.Client.Services;

/// <summary>
/// The static browser runtime: crew come from the seeded roster generator and
/// minds decide through the deterministic browser mind, so no server or model
/// is needed. Everything else is the shared <see cref="StationSession"/>.
/// </summary>
public sealed class GameSession : StationSession
{
    private readonly BrowserMindSystem _browserMind = new();

    public GameSession()
        : base(
            new RuleBasedOverseerMessageInterpreter(),
            CreateStateForScenario(
                new CampaignState(),
                ScenarioCatalog.SecureContinuity,
                Random.Shared.Next()))
    {
    }

    private static GameState CreateStateForScenario(
        CampaignState campaign,
        ScenarioDefinition scenario,
        int rosterSeed,
        int? stationSeed = null)
    {
        var continuingCrew = CampaignProgressionSystem.CreateCrewForScenario(campaign, scenario);
        var baseCrew = continuingCrew ?? SeededCrewRosterGenerator.Generate(rosterSeed);
        var crew = PrisonerRosterSystem.Compose(baseCrew, scenario);
        var state = FacilitySeeder.CreateDefault(
            crew,
            stationSeed: stationSeed,
            stationConstraints: scenario.StationConstraints);

        ScenarioCatalog.Apply(state, scenario);
        CampaignProgressionSystem.ApplyCarryOver(campaign, state);
        return state;
    }

    public override Task ResetAsync(CancellationToken cancellationToken = default)
    {
        PauseClock();
        Campaign = new CampaignState();
        State = CreateStateForScenario(
            Campaign,
            ScenarioCatalog.SecureContinuity,
            Random.Shared.Next());
        return Task.CompletedTask;
    }

    public override Task RegenerateStationAsync(
        int? seed = null,
        CancellationToken cancellationToken = default)
    {
        PauseClock();

        var scenario = State.Scenario ?? ScenarioCatalog.SecureContinuity;
        var rosterSeed = seed ?? Random.Shared.Next();
        State = CreateStateForScenario(
            Campaign,
            scenario,
            rosterSeed,
            stationSeed: seed);
        Campaign.CurrentScenarioId = scenario.Id;
        return Task.CompletedTask;
    }

    public override Task RestoreCampaignAsync(
        CampaignState campaign,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        PauseClock();
        Campaign = campaign;

        var next = CampaignProgressionSystem.NextScenario(Campaign);
        var last = Campaign.MissionHistory.LastOrDefault();
        var scenario = next
            ?? (last is null ? null : ScenarioCatalog.Find(last.ScenarioId))
            ?? throw new InvalidOperationException(
                "Campaign state does not identify a valid scenario roster policy.");

        State = CreateStateForScenario(
            Campaign,
            scenario,
            Random.Shared.Next());

        if (next is null)
        {
            State.ScenarioStatus = last?.Outcome ?? ScenarioStatus.Won;
            State.ScenarioOutcome = Campaign.Ending?.Summary
                ?? "All campaign assignments are recorded. Awaiting final Overseer decision.";
        }

        Campaign.CurrentScenarioId = scenario.Id;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts only the next unlocked campaign assignment on a fresh station.
    /// Arbitrary scenario selection is intentionally rejected by the campaign
    /// layer instead of relying on UI controls for progression integrity.
    /// </summary>
    public override Task LoadScenarioAsync(
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        PauseClock();
        CampaignProgressionSystem.CaptureCompletedMission(Campaign, State);

        if (!CampaignProgressionSystem.CanStartScenario(Campaign, scenarioId))
        {
            Log($"DIRECTIVE PACKAGE {scenarioId} is locked by campaign progression.");
            return Task.CompletedTask;
        }

        var scenario = ScenarioCatalog.Find(scenarioId);
        if (scenario is null)
        {
            return Task.CompletedTask;
        }

        State = CreateStateForScenario(
            Campaign,
            scenario,
            Random.Shared.Next());
        Campaign.CurrentScenarioId = scenario.Id;

        Log($"DIRECTIVE PACKAGE LOADED — {scenario.Title}.");
        return Task.CompletedTask;
    }

    protected override Task ThinkAsync(CancellationToken cancellationToken)
    {
        _browserMind.Tick(State);
        return Task.CompletedTask;
    }
}
