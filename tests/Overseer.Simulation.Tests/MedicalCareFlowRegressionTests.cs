using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Medical care must actually complete inside the running station, not only
/// when MedicalSystem is ticked in isolation.
/// </summary>
public sealed class MedicalCareFlowRegressionTests
{
    [Fact]
    public void Treatment_CompletesEvenWhenTheDoctorsDisplayedActionIsRewritten()
    {
        var (state, medbay, doctor, patient) = CareScene();
        var medical = new MedicalSystem();

        medical.Tick(state);
        Assert.NotNull(doctor.MedicalActionCompletesAt);

        // Movement, routine and social systems rewrite CurrentAction every tick.
        doctor.CurrentAction = new NpcAction(ActionKind.Work, medbay.Id, "Busy.");

        for (var minute = 0; minute < 8; minute++)
        {
            state.Elapsed += TimeSpan.FromMinutes(1);
            medical.Tick(state);
        }

        Assert.True(patient.Health > 60, $"Patient health stayed at {patient.Health:0}.");
    }

    [Fact]
    public void Routine_DoesNotPullTheDoctorOrWaitingPatientAwayMidTreatment()
    {
        var (state, medbay, doctor, patient) = CareScene();
        var medical = new MedicalSystem();
        var routine = new CrewRoutineSystem();

        // Routine plans every fifth minute; the treatment takes six.
        for (var minute = 1; minute <= 10 && patient.Health <= 60; minute++)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            medical.Tick(state);
            routine.Tick(state);

            if (patient.Health <= 60)
            {
                Assert.Null(doctor.Movement);
                Assert.Null(patient.Movement);
                Assert.Equal(medbay.Id, doctor.CurrentRoomId);
            }
        }

        Assert.True(patient.Health > 60, $"Patient health stayed at {patient.Health:0}.");
    }

    [Fact]
    public void LeavingTheMedbayAbandonsTheProcedure()
    {
        var (state, _, doctor, patient) = CareScene();
        var medical = new MedicalSystem();

        medical.Tick(state);
        Assert.NotNull(doctor.MedicalActionCompletesAt);

        doctor.CurrentRoomId = "corridor";
        state.Elapsed += TimeSpan.FromMinutes(10);
        medical.Tick(state);

        Assert.Null(doctor.MedicalActionCompletesAt);
        Assert.Null(doctor.MedicalActionKind);
        Assert.Equal(60, patient.Health);
    }

    [Fact]
    public void Doctor_IsCalledToAPatientWaitingInTheMedbay()
    {
        var (state, medbay, doctor, _) = CareScene();
        doctor.CurrentRoomId = "corridor";

        new MedicalSystem().Tick(state);

        Assert.NotNull(doctor.Intent);
        Assert.Equal(ActionKind.Move, doctor.Intent!.Action);
        Assert.Equal(medbay.Id, doctor.Intent.TargetId);
    }

    [Fact]
    public void InjuredCrew_AreNotSentToAMedbayWithNobodyToTreatThem()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        foreach (var doctor in state.Crew.Where(npc => npc.Role == CrewRole.Doctor))
        {
            doctor.IsPresent = false;
        }

        var patient = state.Crew.First(npc => npc.Role != CrewRole.Doctor);
        patient.Health = 45;
        patient.CurrentRoomId = "corridor";
        patient.Intent = null;

        new MedicalSystem().Tick(state);

        Assert.Null(patient.Intent);
    }

    [Fact]
    public void MedbayTrip_IsKeptRatherThanRestartedEveryMinute()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var patient = state.Crew.First(npc => npc.Role != CrewRole.Doctor);
        patient.Health = 45;
        patient.CurrentRoomId = "corridor";
        patient.Intent = null;
        var medical = new MedicalSystem();

        medical.Tick(state);
        var firstTrip = patient.Intent;
        state.Elapsed += TimeSpan.FromMinutes(1);
        medical.Tick(state);

        Assert.NotNull(firstTrip);
        Assert.Same(firstTrip, patient.Intent);
    }

    [Fact]
    public void MedbayRouting_NeverOverridesAMoreUrgentPlan()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var patient = state.Crew.First(npc => npc.Role != CrewRole.Doctor);
        patient.Health = 45;
        patient.CurrentRoomId = "corridor";
        var shutdown = new NpcIntent(
            ActionKind.ShutdownOverseer,
            "isolation",
            "Isolate Overseer.",
            "The team is ready.",
            98,
            "Test",
            state.Elapsed);
        patient.Intent = shutdown;

        new MedicalSystem().Tick(state);

        Assert.Same(shutdown, patient.Intent);
    }

    [Fact]
    public void SeeingAnInjuredColleague_TriggersOneRethinkNotOnePerMinute()
    {
        var (state, _, doctor, patient) = CareScene();
        var observer = state.Crew.First(npc =>
            npc.Id != doctor.Id && npc.Id != patient.Id && npc.Role != CrewRole.Doctor);
        observer.CurrentRoomId = patient.CurrentRoomId;
        observer.PositionX = patient.PositionX - 10;
        observer.PositionY = patient.PositionY;
        observer.FacingDegrees = 0;
        doctor.CurrentRoomId = "corridor";
        Assert.True(PerceptionSystem.CanSee(state, observer, patient));

        var medical = new MedicalSystem();
        medical.Tick(state);
        Assert.True(observer.NeedsMindReconsideration);

        observer.NeedsMindReconsideration = false;
        state.Elapsed += TimeSpan.FromMinutes(1);
        medical.Tick(state);

        Assert.False(observer.NeedsMindReconsideration);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(202)]
    [InlineData(303)]
    public void InjuredCrewMember_IsTreatedDuringAnOrdinaryShift(int seed)
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: seed);
        var patient = state.Crew.First(npc => npc.Role != CrewRole.Doctor);
        patient.Health = 60;
        var station = new BrowserStation();

        for (var minute = 0; minute < 240 && patient.Health <= 60; minute++)
        {
            station.Advance(state);
        }

        Assert.True(patient.Health > 60, $"Seed {seed}: patient still at {patient.Health:0}% after four hours.");
    }

    private static (GameState State, Room Medbay, Npc Doctor, Npc Patient) CareScene()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var medbay = state.Facility.Rooms.Values.First(room => room.Type == RoomType.Medical);
        var doctor = state.Crew.First(npc => npc.Role == CrewRole.Doctor);
        var patient = state.Crew.First(npc => npc.Role != CrewRole.Doctor);

        doctor.CurrentRoomId = medbay.Id;
        doctor.Intent = null;
        patient.CurrentRoomId = medbay.Id;
        patient.Intent = null;
        patient.Health = 60;
        patient.LastHealthSnapshot = 60;
        return (state, medbay, doctor, patient);
    }

    /// <summary>The browser runtime's turn order, without the UI session.</summary>
    private sealed class BrowserStation
    {
        private readonly StationUpkeepSystem _upkeep = new();
        private readonly EnvironmentSystem _environment = new();
        private readonly AirlockSafetySystem _airlockSafety = new();
        private readonly VacuumConsequenceSystem _vacuum = new();
        private readonly SimulationEngine _simulation = new();
        private readonly MedicalEvidenceSystem _medicalEvidence = new();
        private readonly PerceptionSystem _perception = new();
        private readonly MedicalSystem _medical = new();
        private readonly MissingPersonSystem _missingPeople = new();
        private readonly SecurityMalwareSystem _malware = new();
        private readonly BrowserMindSystem _browserMind = new();
        private readonly IntentExecutionSystem _intentExecution = new();
        private readonly InvestigationSystem _investigations = new();
        private readonly CrewCounterplaySystem _counterplay = new();
        private readonly ManualOverrideSystem _manualOverrides = new();
        private readonly SocialSimulationSystem _social = new();
        private readonly SuspicionSystem _suspicion = new();
        private readonly ConversationPacingSystem _conversationPacing = new();
        private readonly CrewRoutineSystem _crewRoutines = new();
        private readonly RobotSystem _robots = new();
        private readonly TurretSystem _turrets = new();
        private readonly LocalMovementSystem _movement = new();
        private readonly CrewDoorInteractionSystem _crewDoors = new();
        private readonly CrewProvisioningSystem _provisioning = new();
        private readonly CrewMaintenanceSystem _maintenance = new();

        public void Advance(GameState state)
        {
            var turn = TimeSpan.FromMinutes(1);
            _upkeep.Tick(state, turn);
            _environment.Tick(state, turn);
            _airlockSafety.Tick(state, turn);
            _vacuum.Tick(state);
            _simulation.Tick(state, turn);
            _medicalEvidence.Tick(state);
            _perception.Tick(state);
            _medical.Tick(state);
            _missingPeople.Tick(state);
            _malware.Tick(state);
            _browserMind.Tick(state);
            _intentExecution.Tick(state);
            _investigations.Tick(state);
            _counterplay.Tick(state);
            _manualOverrides.Tick(state);
            _social.Tick(state);
            _suspicion.Tick(state);
            _conversationPacing.Tick(state);
            _crewRoutines.Tick(state);
            _robots.Tick(state, turn);
            _turrets.Tick(state, turn);
            _movement.Tick(state, turn);
            _crewDoors.Tick(state);
            _provisioning.Tick(state, turn);
            _maintenance.Tick(state);
        }
    }
}
