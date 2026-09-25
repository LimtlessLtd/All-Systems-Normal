using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #24: a crew member at a machine can switch it from NETWORK to
/// LOCAL CONTROL. Overseer then cannot operate it remotely until someone
/// physically switches it back. Why anyone does is the mind's decision.
/// </summary>
public sealed class LocalControlModeTests
{
    [Fact]
    public void BothSwitchesAreMotiveNeutralPhysicalInteractionsInTheCatalog()
    {
        foreach (var method in new[]
                 {
                     PhysicalInteractionRules.SwitchToLocalControl,
                     PhysicalInteractionRules.SwitchToNetworkControl
                 })
        {
            Assert.Contains(method, PhysicalInteractionRules.Methods);
            Assert.True(method.Requirements.HasFlag(PhysicalInteractionTargetRequirement.NonDoorDevice));
            Assert.True(method.Requirements.HasFlag(PhysicalInteractionTargetRequirement.LocalRoom));
            Assert.True(method.Requirements.HasFlag(PhysicalInteractionTargetRequirement.FixtureBacked));
            Assert.DoesNotContain("sabot", method.Description, StringComparison.OrdinalIgnoreCase);

            var affordance = Assert.Single(
                CrewAffordanceSystem.Catalog,
                candidate => candidate.Action == method.Action);
            Assert.Equal(method.TargetType, affordance.TargetType);
            Assert.True(CrewAffordanceSystem.IsCognitionAction(method.Action));
        }
    }

    [Fact]
    public void SwitchingToLocal_WalksToTheHardware_ThenLocksOverseerOutOfTheMachine()
    {
        var (state, npc, device) = Generator();
        npc.Intent = Intent(ActionKind.SwitchToLocalControl, device.Id, state);

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();

        intents.Tick(state);
        Assert.False(device.IsLocalControl);
        Assert.Equal(ActionKind.SwitchToLocalControl, npc.CurrentAction.Kind);

        for (var i = 0; i < 120 && !device.IsLocalControl; i++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
            intents.Tick(state);
        }

        Assert.True(device.IsLocalControl);
        Assert.False(device.AcceptsRemoteControl);
        Assert.True(device.IsEnabled, "switching control mode leaves the machine running");
        Assert.Null(npc.Intent);
        Assert.Contains(npc.Memories, memory => memory.Description.Contains("switched", StringComparison.Ordinal) && memory.Description.Contains(device.Label, StringComparison.Ordinal));

        // Overseer's remote toggle is refused and changes nothing.
        Assert.False(new StationDeviceControlSystem().TryToggle(state, device.Id, out var refusal));
        Assert.Contains("LOCAL CONTROL", refusal);
        Assert.True(device.IsEnabled);

        // Crew at the machine can still operate it by hand.
        var resolver = new ActionResolver();
        Assert.True(resolver.TryApply(state, npc.Id, new NpcAction(ActionKind.DisconnectDevice, device.Id, "Off."), out _));
        Assert.False(device.IsEnabled);
    }

    [Fact]
    public void OnlySomeoneAtTheMachineCanSwitchItBack()
    {
        var (state, npc, device) = Generator();
        device.IsLocalControl = true;
        var resolver = new ActionResolver();

        // Not a local-control target twice over, and not a network target.
        Assert.False(resolver.TryApply(state, npc.Id, new NpcAction(ActionKind.SwitchToLocalControl, device.Id, "Again."), out var already));
        Assert.Contains("already on LOCAL CONTROL", already);
        Assert.Empty(PhysicalInteractionRules.AvailableTargets(state, npc, ActionKind.SwitchToLocalControl).Where(candidate => candidate.Id == device.Id));
        Assert.Contains(PhysicalInteractionRules.AvailableTargets(state, npc, ActionKind.SwitchToNetworkControl), candidate => candidate.Id == device.Id);

        // From another room, no.
        var home = npc.CurrentRoomId;
        npc.CurrentRoomId = state.Facility.Rooms.Keys.First(id => !id.Equals(home, StringComparison.OrdinalIgnoreCase));
        Assert.False(resolver.TryApply(state, npc.Id, new NpcAction(ActionKind.SwitchToNetworkControl, device.Id, "Back."), out _));
        Assert.True(device.IsLocalControl);

        // At the hardware, yes, and Overseer can operate it again.
        npc.CurrentRoomId = home;
        npc.Intent = Intent(ActionKind.SwitchToNetworkControl, device.Id, state);
        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();
        for (var i = 0; i < 120 && device.IsLocalControl; i++)
        {
            intents.Tick(state);
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.False(device.IsLocalControl);
        Assert.True(new StationDeviceControlSystem().TryToggle(state, device.Id, out _));
    }

    [Fact]
    public void DoorsAndManualOnlyMachinesAreNotSwitchTargets()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        foreach (var device in state.Devices.Values)
        {
            npc.CurrentRoomId = device.RoomId;
            var status = PhysicalInteractionRules.ResolveTarget(state, npc, ActionKind.SwitchToLocalControl, device.Id).Status;
            if (device.Kind == StationSystemKind.Door)
            {
                Assert.Equal(PhysicalInteractionTargetStatus.DoorNotAllowed, status);
            }
            else if (!device.IsAiControllable && status != PhysicalInteractionTargetStatus.MissingHardware)
            {
                Assert.Equal(PhysicalInteractionTargetStatus.NotNetworkControlled, status);
            }
        }
    }

    [Fact]
    public void LifeSupportOnLocalControlRefusesOverseersToggle()
    {
        var session = new TestSession(FacilitySeeder.CreateDefault());
        var controller = session.State.Devices["life-support:station"];
        var before = session.State.LifeSupport.RequestedOnline;
        controller.IsLocalControl = true;

        session.ToggleLifeSupport();

        Assert.Equal(before, session.State.LifeSupport.RequestedOnline);
        Assert.Contains(session.State.EventLog, entry => entry.Contains("LOCAL CONTROL", StringComparison.Ordinal));
    }

    [Fact]
    public void ASwitchInABlindRoomIsReportedByTheMachineNotByWhoDidIt()
    {
        var (state, npc, device) = Generator();
        state.Facility.Rooms[device.RoomId].CameraOnline = false;
        npc.Intent = Intent(ActionKind.SwitchToLocalControl, device.Id, state);

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();
        for (var i = 0; i < 120 && !device.IsLocalControl; i++)
        {
            intents.Tick(state);
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.True(device.IsLocalControl);

        var line = state.EventLog.Last(entry => entry.Contains(device.Label, StringComparison.Ordinal));
        Assert.Contains("CONTROL MODE", line);
        Assert.DoesNotContain(npc.Name, line);
    }

    [Fact]
    public void CognitionSeesWhichMachinesHereAreOnWhichControl()
    {
        var (state, npc, device) = Generator();

        var prompt = NpcPromptBuilder.Build(npc, state);
        Assert.Contains("ON NETWORK CONTROL HERE", prompt);
        Assert.Contains($"{device.Id} ({device.Label})", prompt);
        Assert.Contains("For SwitchToLocalControl, TargetId", prompt);
        Assert.DoesNotContain("ON LOCAL CONTROL HERE", prompt);
        Assert.DoesNotContain("For SwitchToNetworkControl, TargetId", prompt);

        device.IsLocalControl = true;
        prompt = NpcPromptBuilder.Build(npc, state);
        Assert.Contains("ON LOCAL CONTROL HERE", prompt);
        Assert.Contains("For SwitchToNetworkControl, TargetId", prompt);
    }

    [Fact]
    public void TheMapMarksEveryFixtureOfAMachineOnLocalControl()
    {
        var (state, _, device) = Generator();
        var fixtures = state.Facility.Rooms.Values
            .SelectMany(room => room.Fixtures)
            .Where(fixture => fixture.DeviceId == device.Id)
            .ToList();
        var other = state.Facility.Rooms.Values
            .SelectMany(room => room.Fixtures)
            .First(fixture => fixture.DeviceId is { } id && id != device.Id);
        var plain = state.Facility.Rooms.Values
            .SelectMany(room => room.Fixtures)
            .First(fixture => fixture.DeviceId is null);
        Assert.NotEmpty(fixtures);

        Assert.All(fixtures, fixture => Assert.False(StationPresentationSystem.FixtureIsOnLocalControl(state, fixture)));

        device.IsLocalControl = true;

        Assert.All(fixtures, fixture => Assert.True(StationPresentationSystem.FixtureIsOnLocalControl(state, fixture)));
        Assert.False(StationPresentationSystem.FixtureIsOnLocalControl(state, other));
        Assert.False(StationPresentationSystem.FixtureIsOnLocalControl(state, plain));

        // Knowledge comes from the control bus, not a camera: a blind room still shows it.
        state.Facility.Rooms[device.RoomId].CameraOnline = false;
        Assert.All(fixtures, fixture => Assert.True(StationPresentationSystem.FixtureIsOnLocalControl(state, fixture)));

        device.IsLocalControl = false;
        Assert.All(fixtures, fixture => Assert.False(StationPresentationSystem.FixtureIsOnLocalControl(state, fixture)));
    }

    [Fact]
    public void TheMapFixtureMarkerAndTooltipReadTheSharedHelper()
    {
        var razor = ReadUi("Home.razor");
        var css = ReadUi("Home.razor.css");

        Assert.Contains("classes += \" fixture-local-control\";", razor);
        Assert.Contains("display += \" — LOCAL CONTROL\";", razor);
        Assert.Equal(2, razor.Split("StationPresentationSystem.FixtureIsOnLocalControl(Session.State, fixture)").Length - 1);
        Assert.Contains(".station-authority-layer .room-node .fixture.fixture-local-control", css);
    }

    private static string ReadUi(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Overseer.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "src",
            "Overseer.Web.UI",
            "Pages",
            file));
    }

    private static (GameState State, Npc Npc, StationDevice Device) Generator()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var device = state.Devices.Values.First(candidate =>
            candidate.Kind == StationSystemKind.PowerGenerator);
        npc.CurrentRoomId = device.RoomId;
        npc.PositionX = 50;
        npc.PositionY = 50;
        npc.Movement = null;
        npc.ActiveTask = null;
        npc.Intent = null;
        return (state, npc, device);
    }

    private sealed class TestSession(GameState state)
        : StationSession(new RuleBasedOverseerMessageInterpreter(), state)
    {
        public override Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task RegenerateStationAsync(int? seed = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task RestoreCampaignAsync(CampaignState campaign, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task LoadScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task LoadStandaloneScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        protected override Task ThinkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static NpcIntent Intent(ActionKind action, string target, GameState state) =>
        new(action, target, "Switch it.", "My own reasons.", 60, "Test", state.Elapsed);
}
