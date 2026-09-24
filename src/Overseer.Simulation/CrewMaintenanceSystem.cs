using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// The crew keeping the station alive.
///
/// Equipment wears out continuously, so somebody has to go and service it.
/// This is what turns degradation from a countdown to failure into an ongoing
/// argument between the crew's upkeep and whatever Overseer is quietly doing to
/// undermine it. Sealing a compartment does not just inconvenience people any
/// more — it stops the person who was going to fix the thing inside it.
///
/// Assignment is deterministic duty work rather than deliberation: the crew do
/// not need a language model to notice a dead light. What the model still
/// decides is whether somebody would rather do something else, because a
/// maintenance intent can be replaced like any other.
/// </summary>
public sealed class CrewMaintenanceSystem
{
    private readonly NavigationSystem _navigation = new();

    /// <summary>Urgency below which nobody bothers making a special trip.</summary>
    private const double AttentionThreshold = 8;

    /// <summary>An existing goal this urgent is not interrupted for maintenance.</summary>
    private const int ProtectedUrgency = 70;

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Devices.Count == 0 || !state.IsSimulationLive)
        {
            return;
        }

        foreach (var npc in state.Crew.Where(n => n.IsAlive && n.IsPresent))
        {
            ProgressService(state, npc);
        }

        AssignWork(state);
    }

    /// <summary>
    /// Hands out the most urgent job each qualified, uncommitted crew member can
    /// actually reach.
    /// </summary>
    private void AssignWork(GameState state)
    {
        var outstanding = state.Devices.Values
            .Where(device => device.ServiceUrgency >= AttentionThreshold)
            .OrderByDescending(device => device.ServiceUrgency)
            .ToList();

        if (outstanding.Count == 0)
        {
            return;
        }

        var claimed = state.Crew
            .Where(npc => npc.IsAlive && npc.ServicingDeviceId is not null)
            .Select(npc => npc.ServicingDeviceId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var npc in state.Crew
                     .Where(npc => IsAvailable(state, npc))
                     .OrderBy(n => n.Name, StringComparer.Ordinal))
        {
            // Properly qualified first; if nothing here is their speciality they
            // will still have a go at something rather than let it rot.
            // Off-shift crew are only called out of bed for a genuinely
            // urgent fault, not routine upkeep the on-shift cohort can cover.
            var offShift = ScheduledSleepRules.IsOffShift(npc, state.Elapsed);
            var job = outstanding.FirstOrDefault(device =>
                    !claimed.Contains(device.Id)
                    && (!offShift || device.ServiceUrgency >= ScheduledSleepRules.OffShiftCallOutUrgency)
                    && StationUpkeepRules.CanService(npc, device)
                    && CanReach(state, npc, device))
                ?? outstanding.FirstOrDefault(device =>
                    !claimed.Contains(device.Id)
                    && (!offShift || device.ServiceUrgency >= ScheduledSleepRules.OffShiftCallOutUrgency)
                    && StationUpkeepRules.CanAttempt(npc, device)
                    && CanReach(state, npc, device));

            if (job is null)
            {
                continue;
            }

            claimed.Add(job.Id);
            npc.ServicingDeviceId = job.Id;
            npc.ServiceCompletesAt = null;

            npc.Intent = new NpcIntent(
                ActionKind.Repair,
                job.RoomId,
                job.IsFailed
                    ? $"Get {job.Label} working again."
                    : $"Service {job.Label} before it fails.",
                job.IsFailed
                    ? $"{job.Label} has failed and the station needs it."
                    : $"{job.Label} is down to {job.Condition:0}% and will not last.",
                job.IsFailed ? 80 : 45,
                "Maintenance",
                state.Elapsed);
        }
    }

    /// <summary>
    /// Somebody standing at the equipment they were sent to service gets on
    /// with it. Work takes real time and can be interrupted by walking away.
    /// </summary>
    private static void ProgressService(GameState state, Npc npc)
    {
        if (npc.ServicingDeviceId is not { } deviceId
            || !state.Devices.TryGetValue(deviceId, out var device))
        {
            Release(npc);
            return;
        }

        // Before hands-on work starts, a different goal may take the assignment.
        // Once servicing begins, ordinary thoughts/routines are ignored; only a
        // deterministic immediate survival threat may interrupt the task.
        if (npc.Intent is { } competing && competing.Action != ActionKind.Repair)
        {
            if (npc.ServiceCompletesAt is not null)
            {
                if (CrewTaskSystem.CanInterruptForLifeThreat(state, npc, competing))
                {
                    CrewTaskSystem.Interrupt(
                        state,
                        npc,
                        $"Emergency interruption while servicing {device.Label}.");
                    device.ServicedByNpcId = null;
                    Release(npc);
                    return;
                }

                npc.Intent = null;
            }
            else
            {
                device.ServicedByNpcId = null;
                Release(npc);
                return;
            }
        }

        if (!npc.CurrentRoomId.Equals(device.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            // Still travelling before work starts. Leaving after hands-on work
            // begins is a physical invalidation (normally an emergency escape).
            if (npc.ServiceCompletesAt is not null)
            {
                CrewTaskSystem.Interrupt(
                    state,
                    npc,
                    $"Left {device.Label} before servicing completed.");
                device.ServicedByNpcId = null;
                Release(npc);
            }
            return;
        }

        // Loss of power pauses practical work without silently abandoning the
        // committed task. The existing deadline remains authoritative and the
        // outcome is applied only when the worker can physically resume here.
        if (state.Facility.Rooms.TryGetValue(device.RoomId, out var room) && !room.IsPowered)
        {
            return;
        }

        if (npc.ServiceCompletesAt is null)
        {
            var skill = StationUpkeepRules.SkillOf(npc, device.Discipline);
            var speed = Math.Clamp(skill / (double)Math.Max(1, device.ServiceDifficulty), 0.7, 2.0);
            var minutes = Math.Max(4, (int)Math.Round(StationUpkeepRules.ServiceMinutes / speed));

            var duration = TimeSpan.FromMinutes(minutes);
            npc.ServiceCompletesAt = state.Elapsed + duration;
            device.ServicedByNpcId = npc.Id;

            npc.CurrentAction = new NpcAction(
                ActionKind.Repair,
                device.Id,
                $"Servicing {device.Label}.");
            CrewTaskSystem.Start(
                state,
                npc,
                ActionKind.Repair,
                device.Id,
                $"servicing {device.Label}",
                duration);

            ConversationPacingSystem.Schedule(
                npc,
                device.IsFailed
                    ? $"This one's dead. Give me a few minutes."
                    : $"Getting ahead of {device.Label} while I can.",
                NpcBubbleKind.Speech,
                state.Elapsed + TimeSpan.FromMinutes(1),
                2);

            return;
        }

        if (state.Elapsed < npc.ServiceCompletesAt.Value)
        {
            return;
        }

        var before = device.Condition;
        device.Condition = Math.Clamp(
            device.Condition + StationUpkeepRules.RestorationBy(npc, device),
            0,
            100);
        device.LastServicedAt = state.Elapsed;
        device.ServicedByNpcId = null;

        RestoreFunction(state, device);

        CrewTaskSystem.Succeed(
            state,
            npc,
            $"{device.Label} is serviceable again.");
        npc.CurrentAction = new NpcAction(
            ActionKind.Idle,
            null,
            $"{device.Label} is serviceable again.");
        npc.Intent = null;
        npc.RoutineUntil = TimeSpan.Zero;
        Release(npc);

        AudioCueSystem.Emit(
            state,
            AudioCueKind.System,
            npc.Id.ToString(),
            npc.CurrentRoomId);

        Log(
            state,
            $"{npc.Name} services {device.Label} ({before:0}% to {device.Condition:0}%).");
    }

    /// <summary>
    /// Hands a repaired unit's function back. Overseer regains control of what
    /// the failure took away, but the crew decide when that happens.
    /// </summary>
    private static void RestoreFunction(GameState state, StationDevice device)
    {
        if (!state.Facility.Rooms.TryGetValue(device.RoomId, out var room))
        {
            return;
        }

        switch (device.Kind)
        {
            case StationSystemKind.Lighting when room.IsPowered:
                room.LightsOn = true;
                break;

            case StationSystemKind.Camera when room.IsPowered:
                room.CameraOnline = true;
                break;

            case StationSystemKind.ClimateControl:
                room.TemperatureControlOnline = true;
                room.IsTemperatureAiControllable = true;
                break;

            case StationSystemKind.Ventilation:
                room.VentilationEnabled = true;
                room.IsVentilationAiControllable = true;
                break;

            case StationSystemKind.Door when device.DoorId is { } doorId:
            {
                var door = state.Facility.Doors.FirstOrDefault(d => d.Id == doorId);

                if (door is not null)
                {
                    door.IsDamaged = false;
                    door.StructuralIntegrityPercent = (int)Math.Round(device.Condition);
                    door.IsAiControllable = true;
                }

                break;
            }

            case StationSystemKind.LifeSupport:
                state.LifeSupport.IsAiControllable = true;
                break;

            case StationSystemKind.AirlockMechanism:
                room.IsExteriorHatchAiControllable = true;
                room.IsAirlockSafetyAiControllable = true;
                break;

            case StationSystemKind.IsolationMechanism:
            {
                var mechanism = state.ShutdownMechanisms.FirstOrDefault(m =>
                    device.Id.Equals($"isolation:{m.Id}", StringComparison.OrdinalIgnoreCase));

                if (mechanism is not null)
                {
                    mechanism.IsOnline = true;
                }

                break;
            }
        }
    }

    private static bool IsAvailable(GameState state, Npc npc) =>
        npc.IsAlive
        && npc.IsPresent
        && npc.ServicingDeviceId is null
        && !CrewTaskSystem.IsWorking(npc)

        // Somebody already watering the beds or cooking is not free. Two
        // assignment systems overwriting each other's intents meant neither job
        // was ever finished.
        && npc.ProvisioningJob is null

        // Clinical care owns both sides of an active treatment. A waiting
        // patient must remain available to the doctor, and a doctor already
        // performing a procedure must not be reassigned to a repair mid-case.
        && npc.MedicalActionCompletesAt is null
        && !MedicalSystem.IsAwaitingCare(state, npc)

        // Nor is somebody who needs their meal. Handing out jobs regardless
        // kept the crew permanently busy and permanently hungry, with a full
        // galley they never got to.
        && (npc.Hunger < StationProvisionRules.HungryAt || !state.Stores.HasMeal)
        && (npc.Intent is null || npc.Intent.Urgency < ProtectedUrgency);

    private bool CanReach(GameState state, Npc npc, StationDevice device) =>
        npc.CurrentRoomId.Equals(device.RoomId, StringComparison.OrdinalIgnoreCase)
        || _navigation.FindPathForCrew(
            state,
            npc,
            npc.CurrentRoomId,
            device.RoomId).Count > 0;

    private static void Release(Npc npc)
    {
        npc.ServicingDeviceId = null;
        npc.ServiceCompletesAt = null;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
