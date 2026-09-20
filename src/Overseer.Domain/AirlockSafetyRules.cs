namespace Overseer.Domain;

/// <summary>
/// Pure/read-only airlock safety facts that may be consumed by cognition and
/// presentation without depending on the state-changing simulation service.
/// </summary>
public static class AirlockSafetyRules
{
    public const double ExteriorOpenPressureKpa = 5.0;
    public const double InnerPressureToleranceKpa = 8.0;

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

    public static bool CanPerceiveSafetyState(
        GameState state,
        Npc npc,
        Room airlock) =>
        IsAtCrewControls(state, npc, airlock);

    public static bool CanCrewSecure(Npc npc)
    {
        if (npc.Role is CrewRole.Commander or CrewRole.Security)
            return true;

        var baseTechnical = new[]
            {
                "Engineering",
                "Electrical",
                "Operations",
                "Reactor"
            }
            .Select(skill =>
                npc.Skills.TryGetValue(skill, out var value)
                    ? value
                    : 0)
            .DefaultIfEmpty(0)
            .Max();

        var technical = Math.Clamp(
            baseTechnical
            + CrewTraitMath.Modifier(
                npc,
                TraitEffectKind.Technical),
            0,
            120);

        return technical >= 40;
    }

    public static bool NeedsCrewSecuring(
        GameState state,
        Room airlock)
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
            || (airlock.ExteriorHatchOpen
                && innerDoor is { IsPassable: true });
    }

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
}
