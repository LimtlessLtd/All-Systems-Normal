using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class GenericTamperInteractionTests
{
    [Fact]
    public void DisconnectDevice_IsOnlyTargetableForEnabledLocalPhysicalMachinery()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var device = state.Devices.Values.First(candidate =>
            candidate.Kind == StationSystemKind.PowerGenerator);

        npc.CurrentRoomId = device.RoomId;

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(
            state,
            npc,
            ActionKind.DisconnectDevice,
            device.Id,
            out var normalized));
        Assert.Equal(device.Id, normalized);

        npc.CurrentRoomId = state.Facility.Rooms.Values
            .First(room => !room.Id.Equals(device.RoomId, StringComparison.OrdinalIgnoreCase))
            .Id;

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state,
            npc,
            ActionKind.DisconnectDevice,
            device.Id,
            out _));

        npc.CurrentRoomId = device.RoomId;
        device.IsEnabled = false;

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(
            state,
            npc,
            ActionKind.DisconnectDevice,
            device.Id,
            out _));
    }

    [Fact]
    public void DisconnectDevice_RequiresWalkingToHardwareBeforeMachineChangesState()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var device = state.Devices.Values.First(candidate =>
            candidate.Kind == StationSystemKind.PowerGenerator);
        var room = state.Facility.Rooms[device.RoomId];
        var fixture = LocalMovementSystem.FixtureForDevice(room, device.Kind);

        Assert.NotNull(fixture);

        npc.CurrentRoomId = room.Id;
        npc.PositionX = 50;
        npc.PositionY = 50;
        npc.Intent = new NpcIntent(
            ActionKind.DisconnectDevice,
            device.Id,
            "Stop this machine.",
            "I want this generator offline.",
            60,
            "Test",
            state.Elapsed);

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();

        intents.Tick(state);

        Assert.True(device.IsEnabled);
        Assert.Equal(ActionKind.DisconnectDevice, npc.CurrentAction.Kind);
        Assert.Equal(device.Id, npc.CurrentAction.TargetId);

        for (var i = 0; i < 120 && device.IsEnabled; i++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
            intents.Tick(state);
        }

        Assert.False(device.IsEnabled);
        Assert.Null(npc.Intent);
        Assert.Contains(
            state.EventLog,
            entry => entry.Contains(
                $"physically disconnects {device.Label}",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ActionResolver_RejectsDisconnectWhenActorHasNotReachedDeviceHardware()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var device = state.Devices.Values.First(candidate =>
            candidate.Kind == StationSystemKind.PowerGenerator);

        npc.CurrentRoomId = device.RoomId;
        npc.PositionX = 50;
        npc.PositionY = 50;

        var applied = new ActionResolver().TryApply(
            state,
            npc.Id,
            new NpcAction(
                ActionKind.DisconnectDevice,
                device.Id,
                "Disconnect locally."),
            out var message);

        Assert.False(applied);
        Assert.True(device.IsEnabled);
        Assert.Contains("physically reach", message, StringComparison.OrdinalIgnoreCase);
    }
}
