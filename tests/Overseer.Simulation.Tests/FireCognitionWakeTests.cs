using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;
using Overseer.Web.Services;

namespace Overseer.Simulation.Tests;

public sealed class FireCognitionWakeTests
{
    [Fact]
    public void FirstHandFireObservation_IsGroundedAndWakesTheMindOncePerFireEpisode()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 4242);
        var observer = state.Crew.First(npc => npc.IsAlive && npc.IsPresent);
        foreach (var other in state.Crew.Where(npc => npc.Id != observer.Id))
        {
            other.IsPresent = false;
        }

        observer.CurrentRoomId = "reactor";
        observer.PositionX = 50;
        observer.PositionY = 50;
        observer.ObservedFaults.Clear();
        observer.Memories.Clear();
        observer.NeedsMindReconsideration = false;

        var reactor = state.Facility.Rooms["reactor"];
        FireFrontRules.Ignite(reactor, 50, 50, intensity: 1);

        var perception = new PerceptionSystem();
        perception.Tick(state);

        Assert.True(observer.NeedsMindReconsideration);
        Assert.Contains("reactor:fire", observer.ObservedFaults);
        Assert.Contains(
            observer.Memories,
            memory => memory.Description.Contains("active fire", StringComparison.OrdinalIgnoreCase)
                && memory.Description.Contains("[reactor]", StringComparison.OrdinalIgnoreCase));

        observer.NeedsMindReconsideration = false;
        perception.Tick(state);
        Assert.False(observer.NeedsMindReconsideration);

        reactor.FireIntensity = 0;
        perception.Tick(state);
        Assert.DoesNotContain("reactor:fire", observer.ObservedFaults);

        FireFrontRules.Ignite(reactor, 50, 50, intensity: 1);
        perception.Tick(state);
        Assert.True(observer.NeedsMindReconsideration);
    }

    [Fact]
    public async Task SeeingASmallFire_ReopensCognitionEvenDuringAHighUrgencyGoal()
    {
        var (session, decisions) = await CreateSessionAsync();
        var observer = KeepOnlyFirstCrewPresent(session);
        observer.CurrentRoomId = "reactor";
        observer.PositionX = 50;
        observer.PositionY = 50;
        observer.Intent = BusyIntent(session.State, urgency: 95);
        observer.NeedsMindReconsideration = false;

        // Below CrewEnvironmentSafety's emergency fire threshold: this test is
        // specifically about first-hand observation, not the dangerous-room
        // emergency branch.
        FireFrontRules.Ignite(session.State.Facility.Rooms["reactor"], 50, 50, intensity: 1);

        await session.AdvanceOneMinuteAsync();

        Assert.Contains(observer.Id, decisions.Calls);
        Assert.Contains("reactor:fire", observer.ObservedFaults);
    }

    [Fact]
    public async Task FreshFireAlarm_ReopensCognitionEvenDuringAHighUrgencyGoal()
    {
        var (session, decisions) = await CreateSessionAsync();
        var listener = KeepOnlyFirstCrewPresent(session);
        listener.CurrentRoomId = "control";
        listener.Intent = BusyIntent(session.State, urgency: 95);
        listener.NeedsMindReconsideration = false;

        FireFrontRules.Ignite(session.State.Facility.Rooms["engineering"], 50, 50, intensity: 20);
        Assert.True(session.SoundFireAlarm("engineering"));
        Assert.Equal(OverseerClaimKind.FireAlarm, listener.ReceivedMessages.First().Claim);

        await session.AdvanceOneMinuteAsync();

        Assert.Contains(listener.Id, decisions.Calls);
        Assert.Contains(
            "FIRE ALARM",
            NpcPromptBuilder.Build(listener, session.State),
            StringComparison.OrdinalIgnoreCase);
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
        return (session, decisions);
    }

    private static Npc KeepOnlyFirstCrewPresent(GameSession session)
    {
        var target = session.State.Crew
            .Where(npc => npc.IsAlive)
            .OrderBy(npc => npc.Name, StringComparer.Ordinal)
            .First();

        foreach (var npc in session.State.Crew)
        {
            npc.IsPresent = npc.Id == target.Id;
            npc.NeedsMindReconsideration = false;
            npc.Hunger = 10;
            npc.Fatigue = 10;
        }

        return target;
    }

    private static NpcIntent BusyIntent(GameState state, int urgency) =>
        new(
            ActionKind.Work,
            "control",
            "Finish the urgent job already in progress.",
            "This represents an existing high-urgency commitment.",
            urgency,
            "Test",
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
                "Reconsider the new information.",
                "The test mind was given a fresh decision opportunity.",
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
