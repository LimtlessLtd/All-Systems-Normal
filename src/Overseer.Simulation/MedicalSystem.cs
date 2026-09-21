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

        var medbay = state.Facility.Rooms.Values
            .Where(room => room.Type == RoomType.Medical)
            .OrderBy(room => room.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (medbay is null)
            return;

        RoutePatients(state, medbay);
        RouteMedicalWitnesses(state, medbay);

        foreach (var doctor in state.Crew
                     .Where(npc => npc.IsAlive && npc.IsPresent && npc.Role == CrewRole.Doctor)
                     .OrderBy(npc => npc.Name))
        {
            TickDoctor(state, medbay, doctor);
        }
    }

    private void RoutePatients(GameState state, Room medbay)
    {
        if (!IsSafeForCare(medbay))
            return;

        foreach (var patient in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.Health < SeekCareBelow
                     && !npc.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase)))
        {
            var path = _navigation.FindPathForCrew(
                state,
                patient,
                patient.CurrentRoomId,
                medbay.Id);

            if (path.Count < 2)
                continue;

            if (patient.Intent is { Urgency: >= 97, Action: ActionKind.SeekSafety or ActionKind.ForceDoor })
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
        if (!IsSafeForCare(medbay))
            return;

        var injured = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent && npc.Health < SeekCareBelow)
            .OrderBy(npc => npc.Health)
            .ThenBy(npc => npc.Name)
            .ToList();

        foreach (var observer in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            var witnessed = injured.FirstOrDefault(patient =>
                patient.Id != observer.Id
                && PerceptionSystem.CanSee(state, observer, patient));

            if (witnessed is null)
                continue;

            observer.NeedsMindReconsideration = true;

            if (observer.Role != CrewRole.Doctor
                && !observer.Skills.ContainsKey("First Aid"))
                continue;

            if (observer.Intent is { Urgency: >= 94 })
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

    private void TickDoctor(GameState state, Room medbay, Npc doctor)
    {
        if (doctor.MedicalActionCompletesAt is { } completesAt)
        {
            if (state.Elapsed < completesAt)
                return;

            CompleteMedicalAction(state, medbay, doctor);
            return;
        }

        if (!doctor.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase)
            || !IsSafeForCare(medbay))
            return;

        var deadPatient = state.Crew
            .Where(npc => !npc.IsAlive && npc.IsPresent
                && npc.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(npc => npc.Name)
            .FirstOrDefault();

        if (deadPatient is not null && CanResurrect(state, medbay))
        {
            Begin(doctor, deadPatient, ActionKind.ResurrectCrew, ResurrectionMinutes,
                $"Operating resurrection chamber for {deadPatient.Name}.", state.Elapsed);
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
            Begin(doctor, injuredPatient, action, TreatmentMinutes,
                $"Treating {injuredPatient.Name}.", state.Elapsed);
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
            Begin(doctor, checkup, ActionKind.MedicalCheckup, CheckupMinutes,
                $"Performing checkup for {checkup.Name}.", state.Elapsed);
        }
    }

    private static void Begin(
        Npc doctor,
        Npc patient,
        ActionKind action,
        int minutes,
        string reason,
        TimeSpan now)
    {
        doctor.MedicalPatientId = patient.Id;
        doctor.MedicalActionCompletesAt = now + TimeSpan.FromMinutes(minutes);
        doctor.CurrentAction = new NpcAction(action, patient.Name, reason);
        doctor.Intent = null;
        doctor.Movement = null;
    }

    private static void CompleteMedicalAction(GameState state, Room medbay, Npc doctor)
    {
        var action = doctor.CurrentAction.Kind;
        var patient = doctor.MedicalPatientId is { } id
            ? state.Crew.FirstOrDefault(npc => npc.Id == id)
            : null;

        doctor.MedicalActionCompletesAt = null;
        doctor.MedicalPatientId = null;

        if (patient is null
            || !patient.IsPresent
            || !patient.CurrentRoomId.Equals(medbay.Id, StringComparison.OrdinalIgnoreCase))
        {
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
                    break;

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
                    break;

                state.Medical.Supplies -= 3;
                state.Medical.ResurrectionCharges--;
                state.Power.StoredKilowattHours = Math.Max(
                    0,
                    state.Power.StoredKilowattHours - state.Medical.ResurrectionEnergyKwh);
                patient.Health = 35;
                patient.LastHealthSnapshot = 35;
                patient.CauseOfDeath = null;
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

    private static bool IsSafeForCare(Room room) =>
        room.IsPowered
        && room.PressureKpa >= 85
        && room.OxygenPercent >= 18.5
        && room.CarbonDioxidePercent <= 2
        && room.TemperatureC is >= 12 and <= 32;

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
