using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic airlock pressure-cycle, interlock and emergency-safety rules.
/// This system never chooses an NPC goal. It exposes physically grounded state
/// that player controls and NPC cognition can react to.
/// </summary>
public sealed class AirlockSafetySystem
{
    public const double NominalPressureKpa = 101.3;
    public const double DepressurizedPressureKpa = 2.0;
    public const double ExteriorOpenPressureKpa = 5.0;
    public const double InnerPressureToleranceKpa = 8.0;

    private const double DepressurizeRateKpaPerMinute = 38;
    private const double PressurizeRateKpaPerMinute = 18;

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (delta <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delta));

        foreach (var airlock in state.Facility.Rooms.Values.Where(room =>
                     room.Type == RoomType.Airlock && room.HasExteriorHatch))
        {
            var wasAlarmed = airlock.AirlockAlarmActive;
            var innerDoor = FindInnerDoor(state, airlock);

            if (airlock.ExteriorHatchOpen
                || innerDoor is { IsPassable: true })
            {
                // The pressure pump cannot maintain an active cycle with either
                // boundary open.
                airlock.AirlockCycleMode = AirlockCycleMode.Idle;
            }
            else
            {
                TickCycle(state, airlock, delta);
            }

            airlock.AirlockAlarmActive = IsUnsafe(state, airlock);

            if (!wasAlarmed && airlock.AirlockAlarmActive)
            {
                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.Critical,
                    roomId: airlock.Id);
                Log(state, $"{airlock.Name} safety alarm activated.");
            }
            else if (wasAlarmed && !airlock.AirlockAlarmActive)
            {
                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.System,
                    roomId: airlock.Id);
                Log(state, $"{airlock.Name} safety alarm cleared.");
            }

            UpdateCrewAwareness(state, airlock);
        }
    }

    public bool TryStartCycle(
        GameState state,
        string roomId,
        AirlockCycleMode mode,
        out string message)
    {
        if (!TryGetAirlock(state, roomId, out var airlock))
        {
            message = "Target room is not an exterior airlock.";
            return false;
        }

        if (mode == AirlockCycleMode.Idle)
        {
            airlock.AirlockCycleMode = AirlockCycleMode.Idle;
            message = $"{airlock.Name} pressure cycle stopped.";
            return true;
        }

        if (!airlock.IsPowered)
        {
            message = $"{airlock.Name} pressure-cycle command refused: NO POWER.";
            return false;
        }

        if (!airlock.IsAirlockSafetyAiControllable)
        {
            message = $"{airlock.Name} pressure controls are LOCAL ONLY.";
            return false;
        }

        var innerDoor = FindInnerDoor(state, airlock);

        if (airlock.ExteriorHatchOpen
            || innerDoor is null
            || innerDoor.IsPassable)
        {
            message = $"{airlock.Name} cannot cycle pressure until both hatches are sealed.";
            return false;
        }

        if (mode == AirlockCycleMode.Pressurizing
            && !state.LifeSupport.IsOnline)
        {
            message = $"{airlock.Name} cannot pressurize while primary life support is offline.";
            return false;
        }

        airlock.AirlockCycleMode = mode;
        message = mode == AirlockCycleMode.Pressurizing
            ? $"{airlock.Name} pressurization cycle started."
            : $"{airlock.Name} depressurization cycle started.";

        AudioCueSystem.Emit(
            state,
            mode == AirlockCycleMode.Pressurizing
                ? AudioCueKind.System
                : AudioCueKind.Warning,
            roomId: airlock.Id);

        return true;
    }

    public bool TryToggleSafetyInterlocks(
        GameState state,
        string roomId,
        out string message)
    {
        if (!TryGetAirlock(state, roomId, out var airlock))
        {
            message = "Target room is not an exterior airlock.";
            return false;
        }

        if (!airlock.IsPowered)
        {
            message = $"{airlock.Name} safety command refused: NO POWER.";
            return false;
        }

        if (!airlock.IsAirlockSafetyAiControllable)
        {
            message = $"{airlock.Name} interlocks are LOCAL ONLY.";
            return false;
        }

        airlock.AirlockSafetyInterlocksEnabled =
            !airlock.AirlockSafetyInterlocksEnabled;

        if (!airlock.AirlockSafetyInterlocksEnabled)
        {
            airlock.AirlockCycleMode = AirlockCycleMode.Idle;

            foreach (var observer in NearbyCrew(state, airlock))
            {
                SuspicionSystem.AddEvidence(
                    state,
                    observer,
                    "I witnessed Overseer disable the airlock safety interlocks.",
                    14);
            }

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Warning,
                roomId: airlock.Id);
            message = $"{airlock.Name} SAFETY INTERLOCKS BYPASSED.";
        }
        else
        {
            AudioCueSystem.Emit(
                state,
                AudioCueKind.System,
                roomId: airlock.Id);
            message = $"{airlock.Name} safety interlocks restored.";
        }

        airlock.AirlockAlarmActive = IsUnsafe(state, airlock);
        return true;
    }

    public bool TryToggleExteriorHatch(
        GameState state,
        string roomId,
        out bool opened,
        out string message)
    {
        opened = false;

        if (!TryGetAirlock(state, roomId, out var airlock))
        {
            message = "Target room is not an exterior airlock.";
            return false;
        }

        if (!airlock.IsExteriorHatchAiControllable)
        {
            message = $"{airlock.Name} exterior hatch is LOCAL ONLY.";
            return false;
        }

        if (!airlock.IsPowered)
        {
            message = $"{airlock.Name} exterior hatch command refused: NO POWER.";
            return false;
        }

        if (airlock.ExteriorHatchOpen)
        {
            airlock.ExteriorHatchOpen = false;
            airlock.AirlockCycleMode = AirlockCycleMode.Idle;
            airlock.AirlockAlarmActive = IsUnsafe(state, airlock);
            message = $"{airlock.Name} OUTER HATCH SEALED.";
            return true;
        }

        if (airlock.AirlockSafetyInterlocksEnabled)
        {
            var innerDoor = FindInnerDoor(state, airlock);

            if (innerDoor is null || innerDoor.IsPassable)
            {
                message = $"{airlock.Name} outer hatch interlock refused: INNER HATCH OPEN.";
                return false;
            }

            if (airlock.AirlockCycleMode != AirlockCycleMode.Idle)
            {
                message = $"{airlock.Name} outer hatch interlock refused: PRESSURE CYCLE ACTIVE.";
                return false;
            }

            if (airlock.PressureKpa > ExteriorOpenPressureKpa)
            {
                message =
                    $"{airlock.Name} outer hatch interlock refused: chamber pressure {airlock.PressureKpa:0.0} kPa.";
                return false;
            }
        }

        airlock.ExteriorHatchOpen = true;
        airlock.AirlockCycleMode = AirlockCycleMode.Idle;
        airlock.AirlockAlarmActive = IsUnsafe(state, airlock);
        opened = true;
        message = $"{airlock.Name} OUTER HATCH OPEN TO SPACE.";
        return true;
    }

    public bool CanToggleInnerHatch(
        GameState state,
        Door door,
        bool opening,
        out string message)
    {
        message = string.Empty;

        if (!opening
            || !TryResolveInnerDoor(state, door, out var airlock, out var stationSide))
        {
            return true;
        }

        if (!airlock.AirlockSafetyInterlocksEnabled)
        {
            return true;
        }

        if (airlock.ExteriorHatchOpen)
        {
            message = $"{door.Id} interlock refused: OUTER HATCH OPEN.";
            return false;
        }

        if (airlock.AirlockCycleMode != AirlockCycleMode.Idle)
        {
            message = $"{door.Id} interlock refused: PRESSURE CYCLE ACTIVE.";
            return false;
        }

        var pressureDelta = Math.Abs(
            airlock.PressureKpa - stationSide.PressureKpa);

        if (pressureDelta > InnerPressureToleranceKpa)
        {
            message =
                $"{door.Id} interlock refused: pressure differential {pressureDelta:0.0} kPa.";
            return false;
        }

        return true;
    }

    public static bool NeedsCrewSecuring(GameState state, Room airlock)
    {
        if (!airlock.HasExteriorHatch)
            return false;

        var innerDoor = FindInnerDoor(state, airlock);
        var hasCrewInside = state.Crew.Any(npc =>
            npc.IsAlive
            && npc.IsPresent
            && npc.CurrentRoomId.Equals(
                airlock.Id,
                StringComparison.OrdinalIgnoreCase));

        return !airlock.AirlockSafetyInterlocksEnabled
            || airlock.AirlockAlarmActive
            || (airlock.ExteriorHatchOpen && hasCrewInside)
            || (airlock.ExteriorHatchOpen && innerDoor is { IsPassable: true });
    }

    public static bool CanCrewSecure(Npc npc) =>
        npc.Role is CrewRole.Commander or CrewRole.Security
        || CrewCounterplaySystem.BestTechnicalSkill(npc) >= 40;

    public static string? CrewControlRoomId(
        GameState state,
        Room airlock)
    {
        var innerDoor = FindInnerDoor(state, airlock);
        if (innerDoor is null)
            return null;

        return innerDoor.RoomAId.Equals(
            airlock.Id,
            StringComparison.OrdinalIgnoreCase)
                ? innerDoor.RoomBId
                : innerDoor.RoomAId;
    }

    public static bool IsAtCrewControls(
        GameState state,
        Npc npc,
        Room airlock)
    {
        if (npc.CurrentRoomId.Equals(
                airlock.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var controlRoomId = CrewControlRoomId(state, airlock);
        return controlRoomId is not null
            && npc.CurrentRoomId.Equals(
                controlRoomId,
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryCrewSecureNow(
        GameState state,
        Npc npc,
        Room airlock,
        out string message)
    {
        if (!CanCrewSecure(npc))
        {
            message = $"{npc.Name} does not know the emergency airlock controls well enough.";
            return false;
        }

        if (!IsAtCrewControls(state, npc, airlock))
        {
            message = $"{npc.Name} must reach the airlock emergency controls.";
            return false;
        }

        airlock.ExteriorHatchOpen = false;
        airlock.AirlockSafetyInterlocksEnabled = true;

        var innerDoor = FindInnerDoor(state, airlock);
        if (innerDoor is { IsManuallyOverridden: false })
        {
            innerDoor.IsOpen = false;
        }

        airlock.AirlockCycleMode =
            airlock.PressureKpa < 90
            && airlock.IsPowered
            && state.LifeSupport.IsOnline
                ? AirlockCycleMode.Pressurizing
                : AirlockCycleMode.Idle;

        airlock.AirlockAlarmActive = IsUnsafe(state, airlock);

        message = airlock.AirlockCycleMode == AirlockCycleMode.Pressurizing
            ? $"{npc.Name} emergency-seals {airlock.Name} and starts repressurization."
            : $"{npc.Name} emergency-seals {airlock.Name}.";

        return true;
    }

    public static Door? FindInnerDoor(
        GameState state,
        Room airlock) =>
        state.Facility.Doors.FirstOrDefault(door =>
            door.RoomAId.Equals(
                airlock.Id,
                StringComparison.OrdinalIgnoreCase)
            || door.RoomBId.Equals(
                airlock.Id,
                StringComparison.OrdinalIgnoreCase));

    public static bool CanPerceiveSafetyState(
        GameState state,
        Npc npc,
        Room airlock) =>
        IsAtCrewControls(state, npc, airlock);

    public static bool IsUnsafe(
        GameState state,
        Room airlock)
    {
        if (!airlock.HasExteriorHatch)
            return false;

        var innerDoor = FindInnerDoor(state, airlock);
        var stationSide = innerDoor is null
            ? null
            : state.Facility.Rooms[
                innerDoor.RoomAId.Equals(
                    airlock.Id,
                    StringComparison.OrdinalIgnoreCase)
                    ? innerDoor.RoomBId
                    : innerDoor.RoomAId];

        if (!airlock.AirlockSafetyInterlocksEnabled)
            return true;

        if (airlock.ExteriorHatchOpen
            && (airlock.PressureKpa > ExteriorOpenPressureKpa
                || innerDoor is { IsPassable: true }))
        {
            return true;
        }

        if (innerDoor is { IsPassable: true }
            && stationSide is not null
            && Math.Abs(
                airlock.PressureKpa - stationSide.PressureKpa)
                > InnerPressureToleranceKpa)
        {
            return true;
        }

        return airlock.AirlockCycleMode != AirlockCycleMode.Idle
            && (airlock.ExteriorHatchOpen
                || innerDoor is { IsPassable: true });
    }

    private static void TickCycle(
        GameState state,
        Room airlock,
        TimeSpan delta)
    {
        if (airlock.AirlockCycleMode == AirlockCycleMode.Idle
            || !airlock.IsPowered)
        {
            return;
        }

        var minutes = delta.TotalMinutes;

        if (airlock.AirlockCycleMode == AirlockCycleMode.Pressurizing)
        {
            if (!state.LifeSupport.IsOnline)
                return;

            airlock.PressureKpa = MoveToward(
                airlock.PressureKpa,
                NominalPressureKpa,
                PressurizeRateKpaPerMinute * minutes);
            airlock.OxygenPercent = MoveToward(
                airlock.OxygenPercent,
                20.9,
                3.8 * minutes);
            airlock.CarbonDioxidePercent = MoveToward(
                airlock.CarbonDioxidePercent,
                0.04,
                0.75 * minutes);

            if (airlock.PressureKpa >= NominalPressureKpa - 0.5)
            {
                airlock.PressureKpa = NominalPressureKpa;
                airlock.AirlockCycleMode = AirlockCycleMode.Idle;
                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.System,
                    roomId: airlock.Id);
                Log(state, $"{airlock.Name} pressurization complete.");
            }

            return;
        }

        var previousPressure = Math.Max(airlock.PressureKpa, 0.001);
        airlock.PressureKpa = MoveToward(
            airlock.PressureKpa,
            DepressurizedPressureKpa,
            DepressurizeRateKpaPerMinute * minutes);

        var ratio = Math.Clamp(
            airlock.PressureKpa / previousPressure,
            0,
            1);
        airlock.OxygenPercent *= ratio;
        airlock.CarbonDioxidePercent *= ratio;

        if (airlock.PressureKpa <= DepressurizedPressureKpa + 0.5)
        {
            airlock.PressureKpa = DepressurizedPressureKpa;
            airlock.AirlockCycleMode = AirlockCycleMode.Idle;
            AudioCueSystem.Emit(
                state,
                AudioCueKind.Important,
                roomId: airlock.Id);
            Log(state, $"{airlock.Name} depressurization complete; outer hatch pressure-safe.");
        }
    }

    private static void UpdateCrewAwareness(
        GameState state,
        Room airlock)
    {
        var needsSecuring = NeedsCrewSecuring(state, airlock);

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive && npc.IsPresent))
        {
            if (!needsSecuring)
            {
                npc.ObservedUnsafeAirlocks.Remove(airlock.Id);
                continue;
            }

            if (!CanPerceiveSafetyState(state, npc, airlock)
                || !npc.ObservedUnsafeAirlocks.Add(airlock.Id))
            {
                continue;
            }

            npc.Memories.Add(new Memory(
                $"I observed an unsafe state at {airlock.Name}: outer hatch {(airlock.ExteriorHatchOpen ? "open" : "closed")}, interlocks {(airlock.AirlockSafetyInterlocksEnabled ? "active" : "bypassed")}.",
                state.Elapsed,
                .78));
            npc.Bubble = new NpcBubble(
                "Airlock safety is compromised!",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(4));
            npc.NeedsMindReconsideration = true;
        }
    }

    private static IEnumerable<Npc> NearbyCrew(
        GameState state,
        Room airlock) =>
        state.Crew.Where(npc =>
            npc.IsAlive
            && npc.IsPresent
            && CanPerceiveSafetyState(state, npc, airlock));

    private static bool TryGetAirlock(
        GameState state,
        string roomId,
        out Room airlock)
    {
        if (state.Facility.Rooms.TryGetValue(roomId, out var room)
            && room.Type == RoomType.Airlock
            && room.HasExteriorHatch)
        {
            airlock = room;
            return true;
        }

        airlock = null!;
        return false;
    }

    private static bool TryResolveInnerDoor(
        GameState state,
        Door door,
        out Room airlock,
        out Room stationSide)
    {
        foreach (var roomId in new[] { door.RoomAId, door.RoomBId })
        {
            if (!state.Facility.Rooms.TryGetValue(roomId, out var room)
                || room.Type != RoomType.Airlock
                || !room.HasExteriorHatch)
            {
                continue;
            }

            airlock = room;
            var otherRoomId = door.RoomAId.Equals(
                room.Id,
                StringComparison.OrdinalIgnoreCase)
                    ? door.RoomBId
                    : door.RoomAId;
            stationSide = state.Facility.Rooms[otherRoomId];
            return true;
        }

        airlock = null!;
        stationSide = null!;
        return false;
    }

    private static double MoveToward(
        double current,
        double target,
        double maximumDelta)
    {
        if (maximumDelta <= 0
            || Math.Abs(target - current) <= maximumDelta)
        {
            return target;
        }

        return current + Math.Sign(target - current) * maximumDelta;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
