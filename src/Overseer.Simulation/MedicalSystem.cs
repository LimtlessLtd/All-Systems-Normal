using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic clinical mechanics. Physiological emergencies may interrupt
/// low-urgency intentions, but all routing, treatment, supplies, power and
/// resurrection outcomes remain simulation-authoritative.
/// </summary>
public sealed class MedicalSystem
{
    private const double SeekCareBelow = 72;
    private const double TreatmentHeal = 34;
    private const int TreatmentMinutes = 6;
    private const int CheckupMinutes = 3;
    private const int ResurrectionMinutes = 12;
    private const double ResurrectionPowerHeadroomKw = 35;
    private readonly NavigationSystem _navigation = new();

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var medbay = FindMedbay(state);

        if (medbay is null)
            return;

        RoutePatients(state, medbay);
        RouteMedicalWitnesses(state, medbay);
        HoldWaitingPatients(state, medbay);

        foreach (var doctor in state.Crew
                     .Where(npc => npc.IsAlive && npc.IsPresent && npc.Role == CrewRole.Doctor)
                     .OrderBy(npc => npc.Name))
        {
            TickDoctor(state, medbay, doctor);
        }
    }

    /// <summary>
    /// True while an injured crew member is in the medbay and treatment there is
    /// actually possible. Other systems use this to avoid sending them away.
    /// </summary>
    public static bool IsAwaitingCare(GameState state, Npc npc)
    {
        if (!npc.IsAlive || !npc.IsPresent || npc.Health >= SeekCareBelow)
            return false;

        var medbay = FindMedbay(state);

        return medbay is not null
            && npc.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase)
            && IsCareAvailable(state, medbay, npc);
    }

    private void RoutePatients(GameState state, Room medbay)
    {
        foreach (var patient in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.Health < SeekCareBelow
                     && !npc.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase)))
        {
            // Only send people where treatment can happen. Routing to an empty
            // or unsupplied medbay made patients shuttle between it and their
            // routine indefinitely.
            if (!IsCareAvailable(state, medbay, patient))
                continue;

            // Keep an existing trip instead of restarting it every minute.
            if (patient.Intent is { Action: ActionKind.Move } current
                && medbay.Id.Equals(current.TargetId, StringComparison.OrdinalIgnoreCase))
                continue;

            // A more urgent plan (escape, forcing a hatch, shutdown) wins.
            if (patient.Intent is { Urgency: >= 97 })
                continue;

            var path = _navigation.FindPathForCrew(
                state,
                patient,
                patient.CurrentRoomId,
                medbay.Id);

            if (path.Count < 2)
                continue;

            patient.Intent = new NpcIntent(
                ActionKind.Move,
                medbay.Id,
                "Reach the medbay for treatment",
                $"I am injured ({patient.Health:0}% health) and need medical care.",
                96,
                "Physiological emergency",
                state.Elapsed);
            patient.NeedsMindReconsideration = false;
        }
    }

    private void RouteMedicalWitnesses(GameState state, Room medbay)
    {
        var injured = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent && npc.Health < SeekCareBelow)
            .OrderBy(npc => npc.Health)
            .ThenBy(npc => npc.Name)
            .ToList();
        var injuredIds = injured.Select(npc => npc.Id).ToHashSet();

        foreach (var observer in state.Crew)
        {
            // Recovered (or dead) colleagues are forgotten so a new injury is
            // noticed afresh.
            observer.NoticedInjuredCrewIds.RemoveWhere(id => !injuredIds.Contains(id));
        }

        if (!IsSafeForCare(medbay))
            return;

        foreach (var observer in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            var witnessed = injured.FirstOrDefault(patient =>
                patient.Id != observer.Id
                && PerceptionSystem.CanSee(state, observer, patient));

            if (witnessed is null)
                continue;

            // Seeing an injured colleague is a reason to rethink once, not a
            // demand to re-decide every minute they stay in view.
            if (observer.NoticedInjuredCrewIds.Add(witnessed.Id))
                observer.NeedsMindReconsideration = true;

            if (observer.Role != CrewRole.Doctor
                && !observer.Skills.ContainsKey("First Aid"))
                continue;

            if (CrewTaskSystem.IsWorking(observer))
                continue;

            if (observer.Intent is { Urgency: >= 94 })
                continue;

            if (observer.Intent is { Action: ActionKind.AssistCrew } assisting
                && witnessed.Name.Equals(assisting.TargetId, StringComparison.OrdinalIgnoreCase))
                continue;

            observer.Intent = new NpcIntent(
                ActionKind.AssistCrew,
                witnessed.Name,
                $"Get {witnessed.Name} medical help",
                $"{witnessed.Name} is visibly injured and needs the medbay.",
                observer.Role == CrewRole.Doctor ? 95 : 92,
                "Medical witness",
                state.Elapsed);
        }
    }

    /// <summary>
    /// Patients already in a medbay that can treat them wait there rather than
    /// drifting back into routine errands before the doctor arrives.
    /// </summary>
    private static void HoldWaitingPatients(GameState state, Room medbay)
    {
        foreach (var patient in state.Crew.Where(npc =>
                     npc.Intent is null
                     && npc.Movement is null
                     && IsAwaitingCare(state, npc)
                     && npc.CurrentAction.Kind != ActionKind.Rest))
        {
            patient.CurrentAction = new NpcAction(
                ActionKind.Rest,
                medbay.Id,
                "Waiting in the medbay for treatment.");
        }
    }

    private void TickDoctor(GameState state, Room medbay, Npc doctor)
    {
        if (doctor.MedicalActionCompletesAt is null
            && CrewTaskSystem.IsWorking(doctor))
        {
            return;
        }

        if (doctor.MedicalActionCompletesAt is { } completesAt)
        {
            // Walking out abandons the procedure instead of finishing it
            // remotely.
            if (!doctor.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase))
            {
                CrewTaskSystem.Interrupt(
                    state,
                    doctor,
                    "Doctor left the medbay before the medical procedure completed.");
                ClearProcedure(doctor);
                return;
            }

            if (state.Elapsed < completesAt)
                return;

            CompleteMedicalAction(state, medbay, doctor);
            return;
        }

        if (!doctor.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase))
        {
            DispatchToWaitingPatient(state, medbay, doctor);
            return;
        }

        if (!IsSafeForCare(medbay))
            return;

        var deadPatient = state.Crew
            .Where(npc => !npc.IsAlive && npc.IsPresent
                && npc.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(npc => npc.Name)
            .FirstOrDefault();

        if (deadPatient is not null && CanResurrect(state, medbay))
        {
            Begin(state, doctor, deadPatient, ActionKind.ResurrectCrew, ResurrectionMinutes,
                $"Operating resurrection chamber for {deadPatient.Name}.");
            return;
        }

        var injuredPatient = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent && npc.Id != doctor.Id
                && npc.Health < 95
                && npc.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(npc => npc.Health)
            .ThenBy(npc => npc.Name)
            .FirstOrDefault();

        if (injuredPatient is not null && state.Medical.Supplies >= 1)
        {
            var action = injuredPatient.Health < 80
                ? ActionKind.TreatInjury
                : ActionKind.AdministerMedication;
            Begin(state, doctor, injuredPatient, action, TreatmentMinutes,
                $"Treating {injuredPatient.Name}.");
            return;
        }

        var checkup = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent && npc.Id != doctor.Id
                && npc.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase)
                && (npc.LastMedicalCheckupAt is null
                    || state.Elapsed - npc.LastMedicalCheckupAt >= TimeSpan.FromHours(6)))
            .OrderBy(npc => npc.LastMedicalCheckupAt ?? TimeSpan.Zero)
            .ThenBy(npc => npc.Name)
            .FirstOrDefault();

        if (checkup is not null)
        {
            Begin(state, doctor, checkup, ActionKind.MedicalCheckup, CheckupMinutes,
                $"Performing checkup for {checkup.Name}.");
        }
    }

    /// <summary>
    /// Medical duty, like maintenance duty: a patient waiting in a working
    /// medbay calls the doctor in unless they are busy with something urgent.
    /// </summary>
    private void DispatchToWaitingPatient(GameState state, Room medbay, Npc doctor)
    {
        if (!IsSafeForCare(medbay) || state.Medical.Supplies < 1)
            return;

        var waiting = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent && npc.Id != doctor.Id
                && npc.Health < SeekCareBelow
                && npc.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(npc => npc.Health)
            .ThenBy(npc => npc.Name)
            .FirstOrDefault();

        if (waiting is null)
            return;

        if (CrewTaskSystem.IsWorking(doctor))
            return;

        if (doctor.Intent is { Action: ActionKind.Move } current
            && medbay.Id.Equals(current.TargetId, StringComparison.OrdinalIgnoreCase))
            return;

        if (doctor.Intent is { Urgency: >= 94 })
            return;

        if (_navigation.FindPathForCrew(state, doctor, doctor.CurrentRoomId, medbay.Id).Count < 2)
            return;

        doctor.Intent = new NpcIntent(
            ActionKind.Move,
            medbay.Id,
            $"Treat {waiting.Name} in the medbay",
            $"{waiting.Name} is waiting in the medbay for treatment.",
            93,
            "Medical duty",
            state.Elapsed);
    }

    private static void Begin(
        GameState state,
        Npc doctor,
        Npc patient,
        ActionKind action,
        int minutes,
        string reason)
    {
        var duration = TimeSpan.FromMinutes(minutes);
        doctor.MedicalPatientId = patient.Id;
        doctor.MedicalActionCompletesAt = state.Elapsed + duration;
        doctor.MedicalActionKind = action;
        doctor.CurrentAction = new NpcAction(action, patient.Name, reason);
        doctor.Intent = null;
        doctor.Movement = null;
        CrewTaskSystem.Start(
            state,
            doctor,
            action,
            patient.Name,
            reason.TrimEnd('.'),
            duration);
    }

    private static void ClearProcedure(Npc doctor)
    {
        doctor.MedicalActionCompletesAt = null;
        doctor.MedicalPatientId = null;
        doctor.MedicalActionKind = null;
    }

    private static void CompleteMedicalAction(GameState state, Room medbay, Npc doctor)
    {
        // CurrentAction is display state that other systems rewrite while the
        // procedure runs; reading it here silently cancelled most treatments.
        var action = doctor.MedicalActionKind ?? doctor.CurrentAction.Kind;
        var patient = doctor.MedicalPatientId is { } id
            ? state.Crew.FirstOrDefault(npc => npc.Id == id)
            : null;

        ClearProcedure(doctor);

        if (patient is null
            || !patient.IsPresent
            || !patient.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase))
        {
            CrewTaskSystem.Fail(state, doctor, "Medical patient became unavailable.");
            doctor.CurrentAction = new NpcAction(ActionKind.Idle, null, "Medical patient unavailable.");
            return;
        }

        switch (action)
        {
            case ActionKind.MedicalCheckup:
                patient.LastMedicalCheckupAt = state.Elapsed;
                patient.Stress = Math.Max(0, patient.Stress - 6);
                Log(state, $"{doctor.Name} completes a medical checkup for {patient.Name}.");
                break;

            case ActionKind.TreatInjury:
            case ActionKind.AdministerMedication:
                if (!patient.IsAlive || state.Medical.Supplies < 1)
                {
                    CrewTaskSystem.Fail(state, doctor, "Treatment could not be completed because the patient or supplies were unavailable.");
                    doctor.CurrentAction = new NpcAction(ActionKind.Idle, null, "Medical task could not be completed.");
                    return;
                }

                state.Medical.Supplies = Math.Max(0, state.Medical.Supplies - 1);
                if (state.Medical.MedicationDoses > 0)
                {
                    state.Medical.MedicationDoses--;
                    patient.Stress = Math.Max(0, patient.Stress - 8);
                }

                patient.Health = Math.Min(100, patient.Health + TreatmentHeal);
                patient.LastHealthSnapshot = Math.Max(patient.LastHealthSnapshot, patient.Health);
                patient.LastMedicalCheckupAt = state.Elapsed;
                Log(state, $"{doctor.Name} treats {patient.Name}; health is now {patient.Health:0}%.");
                break;

            case ActionKind.ResurrectCrew:
                if (!CanResurrect(state, medbay))
                {
                    CrewTaskSystem.Fail(state, doctor, "Resurrection resources or power were no longer available.");
                    doctor.CurrentAction = new NpcAction(ActionKind.Idle, null, "Resurrection could not be completed.");
                    return;
                }

                state.Medical.Supplies -= 3;
                state.Medical.ResurrectionCharges--;
                state.Power.StoredKilowattHours = Math.Max(
                    0,
                    state.Power.StoredKilowattHours - state.Medical.ResurrectionEnergyKwh);
                patient.Health = 35;
                patient.LastHealthSnapshot = 35;
                patient.CauseOfDeath = null;
                patient.LastDeathAnnouncementAt = null;
                patient.IsPresent = true;
                patient.Intent = null;
                patient.Movement = null;
                patient.CurrentAction = new NpcAction(
                    ActionKind.Rest,
                    medbay.Id,
                    "Recovering after emergency resurrection.");
                patient.Stress = Math.Min(100, patient.Stress + 25);
                patient.Fear = Math.Min(100, patient.Fear + 20);
                Log(state, $"{doctor.Name} revives {patient.Name} in the high-power resurrection chamber.");
                AudioCueSystem.Emit(state, AudioCueKind.Critical, patient.Id.ToString(), medbay.Id);
                break;
        }

        CrewTaskSystem.Succeed(state, doctor, "Medical procedure complete.");
        doctor.CurrentAction = new NpcAction(ActionKind.Idle, null, "Medical task complete.");
    }

    public static bool CanResurrect(GameState state, Room medbay)
    {
        if (!medbay.IsPowered
            || state.Medical.ResurrectionCharges <= 0
            || state.Medical.Supplies < 3)
            return false;

        var headroom = state.Power.SupplyKilowatts
            + state.Power.BufferDischargeKilowatts
            - state.Power.DemandKilowatts;

        return headroom >= ResurrectionPowerHeadroomKw
            || state.Power.StoredKilowattHours >= state.Medical.ResurrectionEnergyKwh;
    }

    private static Room? FindMedbay(GameState state) =>
        state.Facility.Rooms.Values
            .Where(room => room.Type == RoomType.Medical)
            .OrderBy(room => room.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Somebody other than the patient can treat them there.</summary>
    private static bool IsCareAvailable(GameState state, Room medbay, Npc patient) =>
        IsSafeForCare(medbay)
        && state.Medical.Supplies >= 1
        && state.Crew.Any(npc =>
            npc.IsAlive
            && npc.IsPresent
            && npc.Role == CrewRole.Doctor
            && npc.Id != patient.Id);

    private static bool IsSafeForCare(Room room) =>
        room.IsPowered
        && room.PressureKpa >= 85
        && room.OxygenPercent >= 18.5
        && room.CarbonDioxidePercent <= 2
        && room.TemperatureC is >= 12 and <= 32;

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
