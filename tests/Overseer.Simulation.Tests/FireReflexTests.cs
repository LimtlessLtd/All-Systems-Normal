using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;
using Overseer.Web.Services;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner rule (2026-09-24): on the local model runtime, someone who meets a
/// fire reacts at once through the deterministic model-citizen ladder while
/// they wait for their model turn (one mind per tick), and the model's
/// decision takes priority once it arrives.
/// </summary>
public sealed class FireReflexTests
{
    [Fact]
    public async Task CrewSeeingAFire_ActAtOnce_AndTheModelOverridesTheReflexOnItsTurn()
    {
        var (session, decisions) = await CreateSessionAsync();
        var (first, second) = KeepFirstTwoCrewPresent(session);
        foreach (var (npc, x) in new[] { (first, 48d), (second, 52d) })
        {
            npc.CurrentRoomId = "reactor";
            npc.PositionX = x;
            npc.PositionY = 50;
            npc.Intent = BusyIntent(session.State, urgency: 95);
            MakeCapableFirefighter(npc);
        }

        // Small enough that the room is not yet "dangerous": this is the
        // case where the crew used to see the fire and walk on.
        FireFrontRules.Ignite(session.State.Facility.Rooms["reactor"], 50, 50, intensity: 5);

        await session.AdvanceOneMinuteAsync();

        Assert.Equal([first.Id], decisions.Calls);
        AssertModelDecided(first);
        Assert.NotNull(second.Intent);
        Assert.Equal(ActionKind.FightFire, second.Intent!.Action);
        Assert.Equal("reactor", second.Intent.TargetId);
        Assert.Equal(GameSession.ReflexSource, second.Intent.Source);
        Assert.Equal(GameSession.ReflexSource, second.MindMode);

        await session.AdvanceOneMinuteAsync();

        Assert.Equal([first.Id, second.Id], decisions.Calls);
        AssertModelDecided(second);

        // The model chose something else; the same fire must not put the
        // reflex back over its decision.
        await session.AdvanceOneMinuteAsync();

        Assert.NotEqual(GameSession.ReflexSource, second.Intent?.Source);
        Assert.NotEqual(GameSession.ReflexSource, second.MindMode);
    }

    [Fact]
    public async Task CrewHearingAFireAlarm_HeadForTheFireWhileWaitingForTheModel()
    {
        var (session, decisions) = await CreateSessionAsync();
        var (first, second) = KeepFirstTwoCrewPresent(session);
        foreach (var npc in new[] { first, second })
        {
            npc.CurrentRoomId = "control";
            npc.Intent = BusyIntent(session.State, urgency: 95);
            npc.OverseerCredibility = 90;
            npc.OverseerSuspicion = 0;
            MakeCapableFirefighter(npc);
        }

        FireFrontRules.Ignite(session.State.Facility.Rooms["engineering"], 50, 50, intensity: 20);
        Assert.True(session.SoundFireAlarm("engineering"));

        await session.AdvanceOneMinuteAsync();

        Assert.Equal([first.Id], decisions.Calls);
        Assert.NotNull(second.Intent);
        Assert.Equal(ActionKind.FightFire, second.Intent!.Action);
        Assert.Equal("engineering", second.Intent.TargetId);
        Assert.Equal(GameSession.ReflexSource, second.Intent.Source);

        await session.AdvanceOneMinuteAsync();

        Assert.Contains(second.Id, decisions.Calls);
        AssertModelDecided(second);
    }

    [Fact]
    public async Task NoFire_NoReflex()
    {
        var (session, decisions) = await CreateSessionAsync();
        var (first, second) = KeepFirstTwoCrewPresent(session);
        second.Intent = BusyIntent(session.State, urgency: 95);
        first.NeedsMindReconsideration = true;
        second.NeedsMindReconsideration = true;

        await session.AdvanceOneMinuteAsync();

        Assert.Equal([first.Id], decisions.Calls);
        Assert.NotEqual(GameSession.ReflexSource, second.Intent?.Source);
        Assert.NotEqual(GameSession.ReflexSource, second.MindMode);
    }

    // Provisioning may later swap an idle model choice for galley duty, so
    // check what the model's decision left behind rather than the intent.
    private static void AssertModelDecided(Npc npc)
    {
        Assert.Equal(ModelReason, npc.LastThought);
        Assert.NotEqual(GameSession.ReflexSource, npc.Intent?.Source);
    }

    private const string ModelReason = "The test model chose something other than the reflex.";

    private static void MakeCapableFirefighter(Npc npc)
    {
        npc.Skills["Engineering"] = 80;
        npc.Stress = 10;
        npc.Health = 100;
    }

    private static async Task<(GameSession Session, RecordingDecisionService Decisions)> CreateSessionAsync()
    {
        var decisions = new RecordingDecisionService();
        var generatedCrew = FacilitySeeder.CreateDefault(stationSeed: 480043).Crew;
        var session = new GameSession(
            decisions,
            new StaticCrewGenerator(generatedCrew),
            new RuleBasedOverseerMessageInterpreter());

        await session.InitializeAsync();
        // InitializeAsync picks a random layout; routes to the fire must not
        // depend on it.
        await session.RegenerateStationAsync(seed: 480043);
        return (session, decisions);
    }

    private static (Npc First, Npc Second) KeepFirstTwoCrewPresent(GameSession session)
    {
        var chosen = session.State.Crew
            .Where(npc => npc.IsAlive)
            .OrderBy(npc => npc.Name, StringComparer.Ordinal)
            .Take(2)
            .ToList();

        foreach (var npc in session.State.Crew)
        {
            npc.CurrentRoomId = "control";
            npc.IsPresent = chosen.Contains(npc);
            npc.NeedsMindReconsideration = false;
            npc.Hunger = 10;
            npc.Fatigue = 10;
        }

        return (chosen[0], chosen[1]);
    }

    private static NpcIntent BusyIntent(GameState state, int urgency) =>
        new(
            ActionKind.Work,
            "control",
            "Finish the urgent job already in progress.",
            "This represents an existing high-urgency commitment.",
            urgency,
            "Busy",
            state.Elapsed);

    private sealed class RecordingDecisionService : IAiDecisionService
    {
        public List<Guid> Calls { get; } = [];

        public Task<NpcIntent> DecideAsync(
            Npc npc,
            GameState state,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(npc.Id);
            return Task.FromResult(new NpcIntent(
                ActionKind.Idle,
                null,
                "Weigh the situation.",
                ModelReason,
                20,
                "Test",
                state.Elapsed));
        }
    }

    private sealed class StaticCrewGenerator(IReadOnlyList<Npc> crew) : IAiCrewGenerator
    {
        private readonly IReadOnlyList<Npc> _crew = crew;

        public Task<IReadOnlyList<Npc>> GenerateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_crew);
    }
}
