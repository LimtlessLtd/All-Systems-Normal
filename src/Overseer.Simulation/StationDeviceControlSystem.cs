using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Player-facing controls for physical station machinery. This owns only
/// deterministic enable/disable authority; power flow and consequences are
/// recalculated by StationUpkeepSystem on the next simulation tick.
/// </summary>
public sealed class StationDeviceControlSystem
{
    public bool TryToggle(
        GameState state,
        string deviceId,
        out string message)
    {
        if (!state.Devices.TryGetValue(deviceId, out var device))
        {
            message = "Station device not found.";
            return false;
        }

        if (!device.IsAiControllable)
        {
            message = $"{device.Label} is local/manual control only.";
            return false;
        }

        if (device.Kind == StationSystemKind.Door)
        {
            message = "Use the hatch controls for door actuators.";
            return false;
        }

        if (device.IsFailed && !device.IsEnabled)
        {
            message = $"{device.Label} has failed and must be physically serviced.";
            return false;
        }

        device.IsEnabled = !device.IsEnabled;

        if (device.Kind == StationSystemKind.LifeSupport)
        {
            state.LifeSupport.RequestedOnline = device.IsEnabled;
            if (!device.IsEnabled)
                state.LifeSupport.IsOnline = false;
        }

        message = $"{device.Label} {(device.IsEnabled ? "enabled" : "disabled")}.";
        return true;
    }
}
