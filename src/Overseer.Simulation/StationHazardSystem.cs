using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic station hazard physics. This system creates and advances the
/// world problem; cognition chooses what it wants to do about it through the
/// generic crew affordances exposed elsewhere.
/// </summary>
public sealed class StationHazardSystem
{
    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (delta <= TimeSpan.Zero) return;

        TryIgniteEquipment(state);
        AdvanceFires(state, delta);
    }

    private static void TryIgniteEquipment(GameState state)
    {
        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);
        if (minute <= 0 || minute % 12 != 0)
            return;

        foreach (var device in state.Devices.Values
                     .Where(device => device.Condition <= device.DegradedAt)
                     .OrderBy(device => device.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!state.Facility.Rooms.TryGetValue(device.RoomId, out var room)
                || room.Type == RoomType.Corridor
                || room.FireIntensity > 0)
                continue;

            var conditionRisk = Math.Clamp((device.DegradedAt - device.Condition) / 140d, 0, .22);
            var roomRisk = room.Type is RoomType.Reactor or RoomType.Generator
                ? .035
                : room.Type == RoomType.Kitchen ? .02 : .008;
            var chance = Math.Clamp(.008 + conditionRisk + roomRisk, .008, .28);

            if (StableRoll(state.UpkeepSeed, minute, room.Id, device.Id, "ignite") >= chance)
                continue;

            room.FireIntensity = Math.Clamp(16 + ((device.DegradedAt - device.Condition) * .35), 14, 38);
            room.SmokePercent = Math.Max(room.SmokePercent, 4);
            Log(state, $"FIRE: {device.Label} ignites in {room.Name}.");
            AudioCueSystem.Emit(state, AudioCueKind.Critical, roomId: room.Id);

            foreach (var npc in state.Crew.Where(n =>
                         n.IsAlive && n.IsPresent
                         && n.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
            {
                npc.NeedsMindReconsideration = true;
                npc.Fear = Math.Clamp(npc.Fear + 20, 0, 100);
                npc.Stress = Math.Clamp(npc.Stress + 12, 0, 100);
            }
        }
    }

    private static void AdvanceFires(GameState state, TimeSpan delta)
    {
        var minutes = delta.TotalMinutes;
        var burning = state.Facility.Rooms.Values
            .Where(room => room.FireIntensity > 0)
            .OrderBy(room => room.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var room in burning)
        {
            var intensity = room.FireIntensity;
            room.OxygenPercent = Math.Clamp(room.OxygenPercent - (intensity * .00055 * minutes), 0, 23);
            room.CarbonDioxidePercent = Math.Clamp(room.CarbonDioxidePercent + (intensity * .00038 * minutes), 0, 20);
            room.TemperatureC = Math.Clamp(room.TemperatureC + (intensity * .0045 * minutes), -50, 180);
            room.SmokePercent = Math.Clamp(
                room.SmokePercent + (intensity * .035 * minutes)
                - (room.VentilationEnabled ? .35 * minutes : 0),
                0,
                100);

            var growth = room.OxygenPercent >= 18
                ? .16 * minutes
                : -.48 * minutes;
            if (!room.IsPowered) growth -= .06 * minutes;
            room.FireIntensity = Math.Clamp(room.FireIntensity + growth, 0, 100);

            foreach (var npc in state.Crew.Where(n =>
                         n.IsAlive && n.IsPresent
                         && n.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
            {
                npc.Fear = Math.Clamp(npc.Fear + (.32 * minutes), 0, 100);
                npc.Stress = Math.Clamp(npc.Stress + (.28 * minutes), 0, 100);

                var damageRate = Math.Max(0, room.FireIntensity - 32) * .008
                    + Math.Max(0, room.SmokePercent - 35) * .004;
                if (damageRate > 0)
                    npc.Health = Math.Max(0, npc.Health - (damageRate * minutes));
            }

            if (room.FireIntensity < .5)
            {
                room.FireIntensity = 0;
                Log(state, $"Fire in {room.Name} goes out.");
                continue;
            }

            TrySpread(state, room, (int)Math.Floor(state.Elapsed.TotalMinutes));
        }
    }

    private static void TrySpread(GameState state, Room source, int minute)
    {
        if (minute <= 0 || minute % 5 != 0 || source.FireIntensity < 28)
            return;

        foreach (var door in state.Facility.Doors.Where(d =>
                     d.IsPassable
                     && (d.RoomAId.Equals(source.Id, StringComparison.OrdinalIgnoreCase)
                         || d.RoomBId.Equals(source.Id, StringComparison.OrdinalIgnoreCase))))
        {
            var otherId = door.RoomAId.Equals(source.Id, StringComparison.OrdinalIgnoreCase)
                ? door.RoomBId : door.RoomAId;
            if (!state.Facility.Rooms.TryGetValue(otherId, out var other)
                || other.Type == RoomType.Corridor
                || other.FireIntensity > 0)
                continue;

            var chance = Math.Clamp(source.FireIntensity / 900d, .02, .11);
            if (StableRoll(state.UpkeepSeed, minute, source.Id, other.Id, "spread") >= chance)
                continue;

            other.FireIntensity = Math.Clamp(source.FireIntensity * .32, 10, 28);
            other.SmokePercent = Math.Max(other.SmokePercent, 3);
            Log(state, $"FIRE SPREAD: flames reach {other.Name} from {source.Name}.");
            AudioCueSystem.Emit(state, AudioCueKind.Critical, roomId: other.Id);
        }
    }

    public static bool TryExecuteCrewAction(
        GameState state,
        Npc npc,
        ActionKind action,
        Room room,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(room);

        if (!npc.IsAlive || !npc.IsPresent
            || !npc.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
        {
            message = "Crew member is not physically present in the target compartment.";
            return false;
        }

        switch (action)
        {
            case ActionKind.FightFire:
            {
                if (room.FireIntensity <= 0)
                {
                    message = $"{room.Name} has no active fire.";
                    return false;
                }

                var skill = Math.Max(
                    npc.Skills.GetValueOrDefault("Engineering"),
                    npc.Skills.GetValueOrDefault("Security"));
                skill = CrewConditionRules.EffectiveSkill(npc, skill);
                var reduction = 18 + (skill * .22);
                room.FireIntensity = Math.Max(0, room.FireIntensity - reduction);
                room.SmokePercent = Math.Max(0, room.SmokePercent - 6);
                npc.Stress = Math.Clamp(npc.Stress + 3, 0, 100);
                message = room.FireIntensity <= 0
                    ? $"{npc.Name} extinguishes the fire in {room.Name}."
                    : $"{npc.Name} knocks the fire in {room.Name} down to {room.FireIntensity:0}% intensity.";
                Log(state, message);
                return true;
            }

            case ActionKind.SealHazardRoom:
            {
                var closable = state.Facility.Doors
                    .Where(door => CrewDoorInteractionSystem.CanClose(npc, door))
                    .ToList();
                if (closable.Count == 0)
                {
                    message = $"{npc.Name} cannot reach an operable open hatch from {room.Name}.";
                    return false;
                }

                foreach (var door in closable)
                {
                    door.IsOpen = false;
                    door.CrewAutoCloseAt = null;
                    door.LastCrewOperatorId = npc.Id;
                }

                message = $"{npc.Name} seals {closable.Count} hatch(es) around {room.Name}.";
                Log(state, message);
                return true;
            }

            case ActionKind.VentHazardRoom:
            {
                if (room.FireIntensity <= 0 && room.SmokePercent < 8)
                {
                    message = $"{room.Name} has no fire or smoke worth venting.";
                    return false;
                }
                if (room.PressureKpa < 72)
                {
                    message = $"{room.Name} is already too depressurised to vent safely.";
                    return false;
                }

                room.SmokePercent = Math.Max(0, room.SmokePercent - 38);
                room.FireIntensity = Math.Max(0, room.FireIntensity - 14);
                room.PressureKpa = Math.Max(55, room.PressureKpa - 14);
                room.OxygenPercent = Math.Max(12, room.OxygenPercent - 1.8);
                message = $"{npc.Name} vents atmosphere from {room.Name}; smoke and fire fall, but pressure drops.";
                Log(state, message);
                return true;
            }

            default:
                message = "That is not a deterministic hazard response affordance.";
                return false;
        }
    }

    private static double StableRoll(int seed, int minute, string a, string b, string salt)
    {
        unchecked
        {
            uint hash = (uint)seed ^ 2166136261u ^ (uint)minute;
            foreach (var ch in $"{a}|{b}|{salt}")
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return (hash % 10000) / 10000d;
        }
    }

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
