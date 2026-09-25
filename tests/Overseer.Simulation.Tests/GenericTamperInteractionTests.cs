using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class GenericTamperInteractionTests
{
    [Fact]
    public void DisconnectDevice_IsDeclaredAsReusableMotiveNeutralInteractionMetadata()
    {
        var method = Assert.Single(
            PhysicalInteractionRules.Methods,
            candidate => candidate.Action == ActionKind.DisconnectDevice);

        Assert.Equal("disconnect", method.Id);
        Assert.Equal(ActionKind.DisconnectDevice, method.Action);
        Assert.Equal("local-device", method.TargetType);
        Assert.Contains("why you want to do it is your decision", method.Description);
        Assert.True(method.Requirements.HasFlag(PhysicalInteractionTargetRequirement.NonDoorDevice));
        Assert.True(method.Requirements.HasFlag(PhysicalInteractionTargetRequirement.LocalRoom));
        Assert.True(method.Requirements.HasFlag(PhysicalInteractionTargetRequirement.Enabled));
        Assert.True(method.Requirements.HasFlag(PhysicalInteractionTargetRequirement.Working));
        Assert.True(method.Requirements.HasFlag(PhysicalInteractionTargetRequirement.FixtureBacked));

        var affordance = Assert.Single(
            CrewAffordanceSystem.Catalog.Where(candidate =>
                candidate.Action == ActionKind.DisconnectDevice));
        Assert.Equal(method.TargetType, affordance.TargetType);
        Assert.Equal(method.Description, affordance.Description);
    }

    [Fact]
    public void SharedPhysicalInteractionResolution_ExplainsWhyATargetIsUnavailable()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var device = state.Devices.Values.First(candidate =>
            candidate.Kind == StationSystemKind.PowerGenerator);

        npc.CurrentRoomId = device.RoomId;

        var available = PhysicalInteractionRules.ResolveTarget(
            state,
            npc,
            ActionKind.DisconnectDevice,
            device.Id);
        Assert.Equal(PhysicalInteractionTargetStatus.Available, available.Status);
        Assert.Equal(device.Id, available.Device?.Id);
        Assert.NotNull(available.Room);
        Assert.NotNull(available.Fixture);
        Assert.Contains(
            PhysicalInteractionRules.AvailableTargets(
                state,
                npc,
                ActionKind.DisconnectDevice),
            candidate => candidate.Id == device.Id);

        npc.CurrentRoomId = state.Facility.Rooms.Values
            .First(room => !room.Id.Equals(device.RoomId, StringComparison.OrdinalIgnoreCase))
            .Id;
        Assert.Equal(
            PhysicalInteractionTargetStatus.WrongRoom,
            PhysicalInteractionRules.ResolveTarget(
                state,
                npc,
                ActionKind.DisconnectDevice,
                device.Id).Status);

        npc.CurrentRoomId = device.RoomId;
        device.IsEnabled = false;
        Assert.Equal(
            PhysicalInteractionTargetStatus.Disabled,
            PhysicalInteractionRules.ResolveTarget(
                state,
                npc,
                ActionKind.DisconnectDevice,
                device.Id).Status);
    }

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
    public void DisconnectDevice_LeavesPerceivedPhysicalEvidenceWithoutInventingAMotive()
    {
        var state = FacilitySeeder.CreateDefault();
        var actor = state.Crew[0];
        var witness = state.Crew[1];
        var elsewhere = state.Crew[2];
        var device = state.Devices.Values.First(candidate =>
            candidate.Kind == StationSystemKind.PowerGenerator);

        actor.CurrentRoomId = device.RoomId;
        actor.PositionX = 50;
        actor.PositionY = 50;
        actor.Memories.Clear();
        actor.Intent = new NpcIntent(
            ActionKind.DisconnectDevice,
            device.Id,
            "Stop this machine.",
            "I have my own reasons.",
            60,
            "Test",
            state.Elapsed);

        witness.CurrentRoomId = device.RoomId;
        witness.PositionX = 50;
        witness.PositionY = 50;
        witness.Memories.Clear();
        witness.NeedsMindReconsideration = false;

        elsewhere.CurrentRoomId = state.Facility.Rooms.Values
            .First(room => !room.Id.Equals(device.RoomId, StringComparison.OrdinalIgnoreCase))
            .Id;
        elsewhere.Memories.Clear();

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();

        for (var i = 0; i < 120 && device.IsEnabled; i++)
        {
            intents.Tick(state);
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.False(device.IsEnabled);

        var actorMemory = Assert.Single(actor.Memories.Where(memory =>
            memory.Description.Contains("physically disconnected", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(device.Label, actorMemory.Description);
        Assert.DoesNotContain("sabot", actorMemory.Description, StringComparison.OrdinalIgnoreCase);

        var witnessMemory = Assert.Single(witness.Memories.Where(memory =>
            memory.Description.Contains("physically disconnect", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(actor.Name, witnessMemory.Description);
        Assert.Contains(device.Label, witnessMemory.Description);
        Assert.Null(witnessMemory.MoralActorName);
        Assert.DoesNotContain("sabot", witnessMemory.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(witness.NeedsMindReconsideration);

        Assert.DoesNotContain(
            elsewhere.Memories,
            memory => memory.Description.Contains("physically disconnect", StringComparison.OrdinalIgnoreCase));
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
