using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Both runtimes share one StationSession; a runtime supplies only crew
/// creation and how minds decide. These pin that seam.
/// </summary>
public sealed class StationSessionTests
{
    [Fact]
    public async Task EachTurn_AsksTheRuntimeMindsExactlyOnce()
    {
        var session = new RecordingSession();

        await session.AdvanceMinutesAsync(5);

        Assert.Equal(5, session.ThinkCalls);
        Assert.Equal(TimeSpan.FromMinutes(5), session.State.Elapsed);
    }

    [Theory]
    [InlineData(4242)]
    [InlineData(480043)]
    public async Task FreshStation_FirstMinuteDoesNotKillTheRoster(int stationSeed)
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: stationSeed);
        var initialCrew = state.Crew.Count;
        var session = new RecordingSession(state);

        await session.AdvanceOneMinuteAsync();

        Assert.Equal(initialCrew, session.State.Crew.Count(npc => npc.IsAlive && npc.IsPresent));
        Assert.All(session.State.Crew, npc => Assert.True(npc.Health > 0));
    }

    [Fact]
    public async Task EndedScenario_StopsAdvancingAndThinking()
    {
        var session = new RecordingSession();
        session.State.ScenarioStatus = ScenarioStatus.Failed;

        await session.AdvanceOneMinuteAsync();

        Assert.Equal(0, session.ThinkCalls);
        Assert.Equal(TimeSpan.Zero, session.State.Elapsed);
    }

    [Fact]
    public async Task RunningClock_PausesWhenTheScenarioEnds()
    {
        var session = new RecordingSession();
        var (started, generation) = session.StartClock();
        Assert.True(started);

        Assert.True(await session.TryAdvanceRunningAsync(generation));

        session.State.ScenarioStatus = ScenarioStatus.Failed;
        Assert.False(await session.TryAdvanceRunningAsync(generation));
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void ToggleLock_MarksAndClearsWhetherOverseerCausedTheLock()
    {
        var session = new RecordingSession();
        var door = session.State.Facility.Doors.First(d => d.IsAiControllable && !d.IsManuallyOverridden);

        session.ToggleLock(door.Id);
        Assert.True(door.IsLocked);
        Assert.True(door.LockedByOverseer);

        session.ToggleLock(door.Id);
        Assert.False(door.IsLocked);
        Assert.False(door.LockedByOverseer);

        // Owner idea #26: the hatch's access log records both commands as Overseer's.
        Assert.Equal(2, door.AccessLog.Count);
        Assert.All(door.AccessLog, record =>
        {
            Assert.Equal(DoorAccessCredential.OverseerNetwork, record.Credential);
            Assert.Null(record.ActorId);
        });
        Assert.Equal(DoorAccessKind.Unlock, door.AccessLog[0].Kind);
        Assert.Equal(DoorAccessKind.Lock, door.AccessLog[1].Kind);
    }

    [Fact]
    public void HostsDoNotKeepTheirOwnCopyOfTheConsole()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in new[]
        {
            "src/Overseer.Web/Components/Pages/Home.razor",
            "src/Overseer.Web/Components/Pages/Debug.razor",
            "src/Overseer.Web/wwwroot/layout.js",
            "src/Overseer.Web.Client/Pages/Home.razor",
            "src/Overseer.Web.Client/Pages/Debug.razor",
            "src/Overseer.Web.Client/wwwroot/layout.js"
        })
        {
            Assert.False(
                File.Exists(Path.Combine(root, relative)),
                $"{relative} duplicates the shared console in src/Overseer.Web.UI.");
        }
    }

    [Fact]
    public void BothHostsRouteToTheSharedConsole()
    {
        var root = FindRepositoryRoot();
        const string sharedAssembly = "typeof(Overseer.Web.UI.Pages.Home).Assembly";

        // Without the endpoint registration the server answers "/" with a 404
        // and only the interactive router finds the page afterwards.
        Assert.Contains(
            $".AddAdditionalAssemblies({sharedAssembly})",
            File.ReadAllText(Path.Combine(root, "src/Overseer.Web/Program.cs")));

        foreach (var router in new[]
        {
            "src/Overseer.Web/Components/Routes.razor",
            "src/Overseer.Web.Client/App.razor"
        })
        {
            Assert.Contains(sharedAssembly, File.ReadAllText(Path.Combine(root, router)));
        }

        Assert.Contains(
            "_content/Overseer.Web.UI/layout.js",
            File.ReadAllText(Path.Combine(root, "src/Overseer.Web.Client/wwwroot/index.html")));
        Assert.Contains(
            "_content/Overseer.Web.UI/layout.js",
            File.ReadAllText(Path.Combine(root, "src/Overseer.Web/Components/App.razor")));
    }

    [Fact]
    public void ServerHostDoesNotPrerenderTheInteractiveRoute()
    {
        var root = FindRepositoryRoot();

        // GameSession (StationSession) is registered scoped-per-circuit. A
        // prerendered pass and the real interactive circuit each get their
        // own DI scope and their own fresh session, so InitializeAsync (and
        // its crew-generating Ollama call) would run twice on first load if
        // prerendering were re-enabled here.
        Assert.Contains(
            "new InteractiveServerRenderMode(prerender: false)",
            File.ReadAllText(Path.Combine(root, "src/Overseer.Web/Components/App.razor")));
    }

    private sealed class RecordingSession
        : StationSession
    {
        public RecordingSession(GameState? initialState = null)
            : base(
                new RuleBasedOverseerMessageInterpreter(),
                initialState ?? FacilitySeeder.CreateDefault(stationSeed: 4242))
        {
        }

        public int ThinkCalls { get; private set; }

        public override Task ResetAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task RegenerateStationAsync(int? seed = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task RestoreCampaignAsync(CampaignState campaign, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task LoadScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task LoadStandaloneScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        protected override Task ThinkAsync(CancellationToken cancellationToken)
        {
            ThinkCalls++;
            return Task.CompletedTask;
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Overseer.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Overseer.slnx from test output.");
    }
}
