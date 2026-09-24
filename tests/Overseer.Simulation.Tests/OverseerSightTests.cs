using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #23: Overseer knows a fire only through its cameras. A room
/// without a visual feed shows the last camera sighting, never live truth.
/// </summary>
public sealed class OverseerSightTests
{
    [Fact]
    public void ARoomWithAFeedShowsTheLiveFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.FireIntensity = 30;

        var fire = OverseerSightSystem.Fire(kitchen);

        Assert.True(fire.IsLive);
        Assert.Equal(30, fire.Intensity);
    }

    [Fact]
    public void ABlindRoomShowsTheFireItWasLastSeenWithNotTheLiveOne()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.FireIntensity = 20;
        state.Elapsed = TimeSpan.FromMinutes(95);
        OverseerSightSystem.Tick(state);

        kitchen.CameraOnline = false;
        kitchen.FireIntensity = 70;
        state.Elapsed = TimeSpan.FromMinutes(110);
        OverseerSightSystem.Tick(state);

        var fire = OverseerSightSystem.Fire(kitchen);
        Assert.False(fire.IsLive);
        Assert.Equal(20, fire.Intensity);
        Assert.Equal(TimeSpan.FromMinutes(95), fire.SeenAt);
    }

    [Fact]
    public void AFireThatStartsWhereNoCameraCanSeeIsUnknown()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.IsPowered = false;
        kitchen.FireIntensity = 45;
        OverseerSightSystem.Tick(state);

        var fire = OverseerSightSystem.Fire(kitchen);

        Assert.False(fire.IsBurning);
        Assert.Null(fire.SeenAt);
    }

    [Fact]
    public void AnUnreachableCameraIsAsBlindAsADeadOne()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.CameraNetworkReachable = false;
        kitchen.FireIntensity = 45;

        Assert.False(OverseerSightSystem.Fire(kitchen).IsBurning);
    }

    [Fact]
    public void RestoringTheFeedShowsTheLiveFireAtOnce()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.CameraOnline = false;
        kitchen.FireIntensity = 45;

        kitchen.CameraOnline = true;

        var fire = OverseerSightSystem.Fire(kitchen);
        Assert.True(fire.IsLive);
        Assert.Equal(45, fire.Intensity);
    }

    [Fact]
    public void AlertsReportOnlyTheFireTheCamerasHaveSeen()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];

        kitchen.CameraOnline = false;
        kitchen.FireIntensity = 45;
        Assert.DoesNotContain(StationAlertSystem.Build(state), alert => alert.Message.Contains("FIRE", StringComparison.Ordinal));

        kitchen.CameraOnline = true;
        state.Elapsed = TimeSpan.FromMinutes(61);
        OverseerSightSystem.Tick(state);
        Assert.Contains(StationAlertSystem.Build(state), alert => alert.Message == $"{kitchen.Name}: FIRE 45% intensity, smoke 0%.");

        kitchen.CameraOnline = false;
        kitchen.FireIntensity = 90;
        var lastSeen = Assert.Single(StationAlertSystem.Build(state), alert => alert.Message.Contains("FIRE", StringComparison.Ordinal));
        Assert.Equal($"{kitchen.Name}: FIRE last seen at 45% (T+01:01); no camera view now, smoke 0%.", lastSeen.Message);
        Assert.Equal(StationAlertSeverity.Critical, lastSeen.Severity);
    }

    [Fact]
    public void AlertsReadTheReportedSensorChannelsNotTheTrueAtmosphere()
    {
        // #22: a spoofed sensor fools the alert list as well as the console.
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.OxygenPercent = 15;
        kitchen.OxygenSensorReadingOverridePercent = 20.9;
        kitchen.TemperatureC = 45;
        kitchen.TemperatureSensorReadingOverrideC = 21;

        var spoofed = StationAlertSystem.Build(state);
        Assert.DoesNotContain(spoofed, alert => alert.Message.StartsWith($"{kitchen.Name}: low oxygen", StringComparison.Ordinal));
        Assert.DoesNotContain(spoofed, alert => alert.Message.StartsWith($"{kitchen.Name}: temperature", StringComparison.Ordinal));

        kitchen.OxygenSensorReadingOverridePercent = null;
        kitchen.TemperatureSensorReadingOverrideC = null;

        var truthful = StationAlertSystem.Build(state);
        Assert.Contains(truthful, alert => alert.Message == $"{kitchen.Name}: low oxygen (15.0%).");
        Assert.Contains(truthful, alert => alert.Message == $"{kitchen.Name}: temperature 45°C.");
    }

    [Fact]
    public async Task EachTurnRecordsWhatTheCamerasSaw()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var session = new SightSession(state);

        await session.AdvanceOneMinuteAsync();

        Assert.All(
            state.Facility.Rooms.Values.Where(room => room.HasVisualFeed),
            room => Assert.Equal(state.Elapsed, room.FireObservedAt));
    }

    [Fact]
    public void TheMapDrawsFlamesOnlyWhereACameraSeesThem()
    {
        var root = FindRepositoryRoot();
        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));

        Assert.Contains("@if (room.FireIntensity > 0 && room.HasVisualFeed)", home);
        Assert.Contains("if (room.FireIntensity > 0 && room.HasVisualFeed)", home);
        Assert.Contains("OverseerSightSystem.Fire(calloutRoom)", home);
        Assert.Contains("@FireReadout(selectedRoom)", home);
        Assert.DoesNotContain("@selectedRoom.FireIntensity", home);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Overseer.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class SightSession(GameState state)
        : StationSession(new RuleBasedOverseerMessageInterpreter(), state)
    {
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

        protected override Task ThinkAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
