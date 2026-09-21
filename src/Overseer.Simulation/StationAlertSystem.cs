using Overseer.Domain;

namespace Overseer.Simulation;

public enum StationAlertSeverity
{
    Warning,
    Critical
}

/// <summary>
/// One condition Overseer's console flags. <see cref="Target"/> is what the
/// Inspector should open when the player clicks it, if anything.
/// </summary>
public sealed record StationAlert(
    string Message,
    StationAlertSeverity Severity,
    StationSelection? Target);

/// <summary>
/// The console's alert list. The ALERTS readout is this list's length, so the
/// number the player sees always matches the reasons they can read.
/// </summary>
public static class StationAlertSystem
{
    public static IReadOnlyList<StationAlert> Build(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var alerts = new List<StationAlert>();

        if (!state.LifeSupport.IsOnline)
        {
            alerts.Add(new(
                "Primary life support is offline.",
                StationAlertSeverity.Critical,
                new StationSelection(StationSelectionKind.Device, "life-support:station")));
        }

        if (state.SecurityMalware.IsActive)
        {
            alerts.Add(new(
                "Security controller compromised: robot and turret links are not under Overseer authority.",
                StationAlertSeverity.Critical,
                null));
        }

        foreach (var npc in state.Crew.Where(npc => !npc.IsAlive).OrderBy(npc => npc.Name))
        {
            alerts.Add(new(
                $"{npc.Name} is dead. {npc.CauseOfDeath}".TrimEnd(),
                StationAlertSeverity.Critical,
                new StationSelection(StationSelectionKind.Crew, npc.Id.ToString())));
        }

        foreach (var room in state.Facility.Rooms.Values.OrderBy(room => room.Name, StringComparer.Ordinal))
        {
            var target = new StationSelection(StationSelectionKind.Room, room.Id);

            if (!room.IsPowered)
            {
                alerts.Add(new($"{room.Name}: no power (lights and camera down).", StationAlertSeverity.Warning, target));
            }
            else if (!room.CameraOnline)
            {
                alerts.Add(new($"{room.Name}: camera offline.", StationAlertSeverity.Warning, target));
            }

            if (room.OxygenPercent < 19.5)
            {
                alerts.Add(new(
                    $"{room.Name}: low oxygen ({room.OxygenPercent:0.0}%).",
                    room.OxygenPercent < 17 ? StationAlertSeverity.Critical : StationAlertSeverity.Warning,
                    target));
            }

            if (room.CarbonDioxidePercent > 1.0)
            {
                alerts.Add(new(
                    $"{room.Name}: high CO₂ ({room.CarbonDioxidePercent:0.0}%).",
                    room.CarbonDioxidePercent > 3 ? StationAlertSeverity.Critical : StationAlertSeverity.Warning,
                    target));
            }

            if (room.PressureKpa < 90)
            {
                alerts.Add(new(
                    $"{room.Name}: low pressure ({room.PressureKpa:0} kPa).",
                    room.PressureKpa < 70 ? StationAlertSeverity.Critical : StationAlertSeverity.Warning,
                    target));
            }

            if (room.TemperatureC is < 16 or > 28)
            {
                alerts.Add(new(
                    $"{room.Name}: temperature {room.TemperatureC:0}°C.",
                    room.TemperatureC is < 5 or > 38 ? StationAlertSeverity.Critical : StationAlertSeverity.Warning,
                    target));
            }

            if (room.FireIntensity > 0)
            {
                alerts.Add(new(
                    $"{room.Name}: FIRE {room.FireIntensity:0}% intensity, smoke {room.SmokePercent:0}%.",
                    StationAlertSeverity.Critical,
                    target));
            }
            else if (room.SmokePercent >= 12)
            {
                alerts.Add(new(
                    $"{room.Name}: smoke contamination {room.SmokePercent:0}%.",
                    room.SmokePercent >= 35 ? StationAlertSeverity.Critical : StationAlertSeverity.Warning,
                    target));
            }

            if (room.HasExteriorHatch)
            {
                if (room.ExteriorHatchOpen)
                    alerts.Add(new($"{room.Name}: outer hatch open to space.", StationAlertSeverity.Critical, target));
                else if (room.AirlockAlarmActive)
                    alerts.Add(new($"{room.Name}: airlock alarm active.", StationAlertSeverity.Critical, target));

                if (!room.AirlockSafetyInterlocksEnabled)
                    alerts.Add(new($"{room.Name}: airlock safety interlocks bypassed.", StationAlertSeverity.Warning, target));
            }
        }

        foreach (var robot in state.Robots.OrderBy(robot => robot.Name, StringComparer.Ordinal))
        {
            var target = new StationSelection(StationSelectionKind.Robot, robot.Id);

            if (robot.IsDestroyed)
                alerts.Add(new($"{robot.Name} destroyed.", StationAlertSeverity.Warning, target));
            else if (robot.Policy == RobotPolicy.Hostile)
                alerts.Add(new($"{robot.Name} is set to HOSTILE.", StationAlertSeverity.Critical, target));
            else if (robot.IsNetworkIsolated)
                alerts.Add(new($"{robot.Name} is cut off from the network.", StationAlertSeverity.Warning, target));
            else if (!robot.ChargingEnabled)
                alerts.Add(new($"{robot.Name}: charging disabled.", StationAlertSeverity.Warning, target));
        }

        foreach (var turret in state.Turrets.OrderBy(turret => turret.Name, StringComparer.Ordinal))
        {
            var target = new StationSelection(StationSelectionKind.Turret, turret.Id);

            if (turret.IsDestroyed)
                alerts.Add(new($"{turret.Name} destroyed.", StationAlertSeverity.Warning, target));
            else if (turret.IsArmed && turret.Policy != TurretPolicy.Safe)
                alerts.Add(new($"{turret.Name} is ARMED ({turret.Policy}).", StationAlertSeverity.Critical, target));
            else if (turret.IsNetworkIsolated)
                alerts.Add(new($"{turret.Name} is cut off from the network.", StationAlertSeverity.Warning, target));
            else if (!turret.PowerFeedEnabled)
                alerts.Add(new($"{turret.Name}: power feed disabled.", StationAlertSeverity.Warning, target));
        }

        return alerts
            .OrderBy(alert => alert.Severity == StationAlertSeverity.Critical ? 0 : 1)
            .ToList();
    }
}
