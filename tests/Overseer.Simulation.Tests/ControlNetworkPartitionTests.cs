using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class ControlNetworkPartitionTests
{
    [Fact]
    public void PhysicallyDisconnectingTheNetworkRack_CutsOverseerRemoteMachineryControlAndCameraReachability()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var actor = state.Crew[0];
        var network = state.Devices["network:control"];
        var room = state.Facility.Rooms[network.RoomId];
        var fixture = Assert.IsType<RoomFixture>(
            LocalMovementSystem.FixtureForDevice(room, network.Kind));

        actor.CurrentRoomId = room.Id;
        actor.PositionX = 50;
        actor.PositionY = 50;
        actor.Intent = new NpcIntent(
            ActionKind.DisconnectDevice,
            network.Id,
            "Physically disconnect the control network.",
            "I want the control network physically offline.",
            70,
            "Test",
            state.Elapsed);

        Assert.True(state.ControlNetworkOnline);

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();
        for (var i = 0; i < 120 && network.IsEnabled; i++)
        {
            intents.Tick(state);
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.False(network.IsEnabled);
        Assert.Contains(
            state.EventLog,
            entry => entry.Contains(
                $"physically disconnects {network.Label}",
                StringComparison.OrdinalIgnoreCase));

        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.False(state.ControlNetworkOnline);
        Assert.All(
            state.Facility.Rooms.Values,
            candidate => Assert.False(candidate.CameraNetworkReachable));

        var generator = state.Devices.Values.First(device =>
            device.Kind == StationSystemKind.PowerGenerator);
        var generatorEnabled = generator.IsEnabled;

        var remoteApplied = new StationDeviceControlSystem().TryToggle(
            state,
            generator.Id,
            out var remoteMessage);

        Assert.False(remoteApplied);
        Assert.Equal(generatorEnabled, generator.IsEnabled);
        Assert.Contains("control network is offline", remoteMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("operated locally", remoteMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADisconnectedNetworkRack_IsARestorableLocalSystem_AndCrewCanBringItBack()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var network = state.Devices["network:control"];
        network.IsEnabled = false;

        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.False(state.ControlNetworkOnline);
        Assert.True(CrewCounterplaySystem.HasRestorableProblem(state, network.Id));
        Assert.Equal(network.RoomId, CrewCounterplaySystem.RequiredRoomForRestore(state, network.Id));

        Assert.True(CrewCounterplaySystem.TryRestoreOneProblem(state, network.Id));
        Assert.True(network.IsEnabled);

        new StationUpkeepSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(state.ControlNetworkOnline);
        Assert.All(
            state.Facility.Rooms.Values,
            candidate => Assert.True(candidate.CameraNetworkReachable));
    }

    [Fact]
    public void CognitionCanSeeTheDisconnectedNetworkAsARestorableTarget()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var network = state.Devices["network:control"];
        network.IsEnabled = false;
        var npc = state.Crew[0];
        npc.CurrentRoomId = network.RoomId;

        var prompt = NpcPromptBuilder.Build(npc, state);

        Assert.Contains(network.Id, prompt);
        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state,
            npc,
            ActionKind.RestoreSystem,
            network.Id,
            out var normalized));
        Assert.Equal(network.Id, normalized);
    }
}
