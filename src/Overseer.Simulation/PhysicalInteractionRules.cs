using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic properties a physical interaction target must satisfy. These
/// describe capability/physics only; they never encode why an NPC wants to act.
/// </summary>
[Flags]
public enum PhysicalInteractionTargetRequirement
{
    None = 0,
    NonDoorDevice = 1 << 0,
    LocalRoom = 1 << 1,
    Enabled = 1 << 2,
    Working = 1 << 3,
    FixtureBacked = 1 << 4
}

/// <summary>
/// Reusable metadata for one motive-neutral physical interaction method.
/// Additional methods (cut, loosen, overload, spill, jam, block, etc.) can
/// compose the same target pipeline without inventing a "sabotage" action.
/// </summary>
public sealed record PhysicalInteractionMethod(
    string Id,
    ActionKind Action,
    string TargetType,
    string Description,
    PhysicalInteractionTargetRequirement Requirements);

public enum PhysicalInteractionTargetStatus
{
    Available,
    UnsupportedAction,
    MissingTarget,
    DoorNotAllowed,
    WrongRoom,
    MissingRoom,
    MissingHardware,
    Failed,
    Disabled
}

/// <summary>
/// Result of validating a physical interaction target. Callers may use the
/// status for human-facing rejection feedback while sharing one authoritative
/// target/capability check.
/// </summary>
public sealed record PhysicalInteractionTargetResolution(
    PhysicalInteractionMethod? Method,
    PhysicalInteractionTargetStatus Status,
    StationDevice? Device = null,
    Room? Room = null,
    RoomFixture? Fixture = null)
{
    public bool IsAvailable => Status == PhysicalInteractionTargetStatus.Available;
}

/// <summary>
/// Owner idea #12: shared physical-interaction metadata and target resolution.
/// C# owns target validity and physical access; cognition owns motive.
/// </summary>
public static class PhysicalInteractionRules
{
    public static readonly PhysicalInteractionMethod DisconnectDevice = new(
        Id: "disconnect",
        Action: ActionKind.DisconnectDevice,
        TargetType: "local-device",
        Description: "Physically disconnect a non-door station device in your current room. This only changes the machine; why you want to do it is your decision.",
        Requirements:
            PhysicalInteractionTargetRequirement.NonDoorDevice
            | PhysicalInteractionTargetRequirement.LocalRoom
            | PhysicalInteractionTargetRequirement.Enabled
            | PhysicalInteractionTargetRequirement.Working
            | PhysicalInteractionTargetRequirement.FixtureBacked);

    public static readonly IReadOnlyList<PhysicalInteractionMethod> Methods =
    [
        DisconnectDevice
    ];

    public static PhysicalInteractionMethod? ForAction(ActionKind action) =>
        Methods.FirstOrDefault(method => method.Action == action);

    public static bool IsPhysicalInteraction(ActionKind action) =>
        ForAction(action) is not null;

    public static PhysicalInteractionTargetResolution ResolveTarget(
        GameState state,
        Npc npc,
        ActionKind action,
        string? requestedTarget)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);

        var method = ForAction(action);
        if (method is null)
        {
            return new(
                null,
                PhysicalInteractionTargetStatus.UnsupportedAction);
        }

        var requested = requestedTarget?.Trim();
        if (string.IsNullOrWhiteSpace(requested)
            || !state.Devices.TryGetValue(requested, out var device))
        {
            return new(method, PhysicalInteractionTargetStatus.MissingTarget);
        }

        var requirements = method.Requirements;

        if (requirements.HasFlag(PhysicalInteractionTargetRequirement.NonDoorDevice)
            && device.Kind == StationSystemKind.Door)
        {
            return new(
                method,
                PhysicalInteractionTargetStatus.DoorNotAllowed,
                device);
        }

        if (requirements.HasFlag(PhysicalInteractionTargetRequirement.LocalRoom)
            && !device.RoomId.Equals(
                npc.CurrentRoomId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                method,
                PhysicalInteractionTargetStatus.WrongRoom,
                device);
        }

        if (!state.Facility.Rooms.TryGetValue(device.RoomId, out var room))
        {
            return new(
                method,
                PhysicalInteractionTargetStatus.MissingRoom,
                device);
        }

        RoomFixture? fixture = null;
        if (requirements.HasFlag(PhysicalInteractionTargetRequirement.FixtureBacked))
        {
            fixture = LocalMovementSystem.FixtureForDevice(room, device.Kind);
            if (fixture is null)
            {
                return new(
                    method,
                    PhysicalInteractionTargetStatus.MissingHardware,
                    device,
                    room);
            }
        }

        if (requirements.HasFlag(PhysicalInteractionTargetRequirement.Working)
            && device.IsFailed)
        {
            return new(
                method,
                PhysicalInteractionTargetStatus.Failed,
                device,
                room,
                fixture);
        }

        if (requirements.HasFlag(PhysicalInteractionTargetRequirement.Enabled)
            && !device.IsEnabled)
        {
            return new(
                method,
                PhysicalInteractionTargetStatus.Disabled,
                device,
                room,
                fixture);
        }

        return new(
            method,
            PhysicalInteractionTargetStatus.Available,
            device,
            room,
            fixture);
    }

    public static IReadOnlyList<StationDevice> AvailableTargets(
        GameState state,
        Npc npc,
        ActionKind action) =>
        state.Devices.Values
            .Where(device =>
                ResolveTarget(state, npc, action, device.Id).IsAvailable)
            .OrderBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
