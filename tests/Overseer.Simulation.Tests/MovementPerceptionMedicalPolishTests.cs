using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class MovementPerceptionMedicalPolishTests
{
    [Fact]
    public void LocalMovement_DetoursAroundPhysicalFixtures()
    {
        var state = SingleRoomState();
        var room = state.Facility.Rooms["room"];
        room.Fixtures.Add(new RoomFixture(
            FixtureType.Console,
            "Blocking console",
            50,
            50,
            20,
            28,
            82,
            50,
            FixtureUsePose.Stand,
            DeviceId: "test:console"));

        var npc = state.Crew[0];
        npc.PositionX = 18;
        npc.PositionY = 50;
        npc.ServicingDeviceId = "test:console";
        npc.CurrentAction = new NpcAction(ActionKind.Work, "room", "Cross-room service test.");

        var movement = new LocalMovementSystem();
        for (var index = 0; index < 40; index++)
        {
            movement.Tick(state, TimeSpan.FromSeconds(4));

            var insideFixture =
                npc.PositionX > 40 && npc.PositionX < 60
                && npc.PositionY > 36 && npc.PositionY < 64;
            Assert.False(insideFixture, $"Crew entered fixture at {npc.PositionX:0.0},{npc.PositionY:0.0}.");
        }

        Assert.True(npc.PositionX > 60, "Crew should make progress around the obstruction.");
    }

    [Fact]
    public void SeedOneStorageDoorApproach_DoesNotStallOnFixtureWaypoints()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        marcus.CurrentRoomId = "storage";
        marcus.PositionX = 25.9;
        marcus.PositionY = 21.9;
        marcus.Movement = null;
        marcus.Intent = new NpcIntent(
            ActionKind.Eat,
            null,
            "Get something to eat.",
            "I am hungry.",
            75,
            "Test",
            state.Elapsed);

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();

        for (var minute = 0;
             minute < 12 && marcus.CurrentRoomId.Equals("storage", StringComparison.OrdinalIgnoreCase);
             minute++)
        {
            intents.Tick(state);
            movement.Tick(state, TimeSpan.FromMinutes(1));
            state.Elapsed += TimeSpan.FromMinutes(1);
        }

        Assert.False(
            marcus.CurrentRoomId.Equals("storage", StringComparison.OrdinalIgnoreCase),
            $"Marcus stalled in Storage at {marcus.PositionX:0.0},{marcus.PositionY:0.0}.");
    }

    [Fact]
    public void HumanLos_UsesForwardConeAndOpenDoorGeometry()
    {
        var state = TwoRoomState();
        var observer = state.Crew[0];
        var target = state.Crew[1];

        observer.CurrentRoomId = "a";
        observer.PositionX = 88;
        observer.PositionY = 50;
        observer.FacingDegrees = 0;
        target.CurrentRoomId = "b";
        target.PositionX = 12;
        target.PositionY = 50;

        var door = Assert.Single(state.Facility.Doors);
        door.IsOpen = false;
        Assert.False(PerceptionSystem.CanSee(state, observer, target));

        door.IsOpen = true;
        Assert.True(PerceptionSystem.CanSee(state, observer, target));

        observer.FacingDegrees = 180;
        Assert.False(PerceptionSystem.CanSee(state, observer, target));
    }

    [Fact]
    public void SensorLos_IsOmnidirectionalAndLongerRangeThanHumanVision()
    {
        var state = SingleRoomState(width: 100, height: 60);
        var human = state.Crew[0];
        var target = new Npc
        {
            Name = "Target",
            Role = CrewRole.Engineer,
            Personality = new Personality(50, 50, 50, 50),
            CurrentRoomId = "room",
            PositionX = 10,
            PositionY = 50
        };
        state.Crew.Add(target);

        human.PositionX = 50;
        human.PositionY = 50;
        human.FacingDegrees = 0;

        var robot = new StationRobot
        {
            Id = "sensor",
            Name = "Sensor",
            CurrentRoomId = "room",
            PositionX = 50,
            PositionY = 50,
            FacingDegrees = 0
        };
        state.Robots.Add(robot);

        Assert.False(PerceptionSystem.CanSee(state, human, target));
        Assert.True(PerceptionSystem.CanSee(state, robot, target));
    }

    [Fact]
    public void InjuredCrew_PrioritiseReachableSafeMedbay()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var patient = state.Crew.First(npc => npc.Role != CrewRole.Doctor);
        var medbay = state.Facility.Rooms.Values.First(room => room.Type == RoomType.Medical);

        patient.Health = 45;
        patient.LastHealthSnapshot = 45;
        patient.CurrentRoomId = "corridor";
        patient.Intent = null;

        new MedicalSystem().Tick(state);

        var intent = Assert.IsType<NpcIntent>(patient.Intent);
        Assert.Equal(ActionKind.Move, intent.Action);
        Assert.Equal(medbay.Id, intent.TargetId);
        Assert.True(intent.Urgency >= 90);
    }

    [Fact]
    public void Doctor_TreatsInjuredPatientAndConsumesMedicalResources()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var doctor = state.Crew.Single(npc => npc.Role == CrewRole.Doctor);
        var patient = state.Crew.First(npc => npc.Role != CrewRole.Doctor);
        var medbay = state.Facility.Rooms.Values.First(room => room.Type == RoomType.Medical);

        doctor.CurrentRoomId = medbay.Id;
        doctor.PositionX = 50;
        doctor.PositionY = 50;
        patient.CurrentRoomId = medbay.Id;
        patient.Health = 40;
        patient.LastHealthSnapshot = 40;
        var suppliesBefore = state.Medical.Supplies;

        var medical = new MedicalSystem();
        medical.Tick(state);

        Assert.Equal(ActionKind.TreatInjury, doctor.CurrentAction.Kind);
        Assert.NotNull(doctor.MedicalActionCompletesAt);

        state.Elapsed += TimeSpan.FromMinutes(7);
        medical.Tick(state);

        Assert.True(patient.Health > 40);
        Assert.True(state.Medical.Supplies < suppliesBefore);
        Assert.Equal(ActionKind.Idle, doctor.CurrentAction.Kind);
    }

    [Fact]
    public void Resurrection_RequiresPowerResourcesAndRevivesPresentBody()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var doctor = state.Crew.Single(npc => npc.Role == CrewRole.Doctor);
        var patient = state.Crew.First(npc => npc.Role != CrewRole.Doctor);
        var medbay = state.Facility.Rooms.Values.First(room => room.Type == RoomType.Medical);

        doctor.CurrentRoomId = medbay.Id;
        patient.CurrentRoomId = medbay.Id;
        patient.Health = 0;
        patient.CauseOfDeath = "Test fatality.";
        patient.LastHealthSnapshot = 0;
        medbay.IsPowered = true;
        state.Medical.Supplies = 6;
        state.Medical.ResurrectionCharges = 1;
        state.Power.StoredKilowattHours = 20;

        var medical = new MedicalSystem();
        Assert.True(MedicalSystem.CanResurrect(state, medbay));

        medical.Tick(state);
        Assert.Equal(ActionKind.ResurrectCrew, doctor.CurrentAction.Kind);

        state.Elapsed += TimeSpan.FromMinutes(13);
        medical.Tick(state);

        Assert.True(patient.IsAlive);
        Assert.Null(patient.CauseOfDeath);
        Assert.Equal(0, state.Medical.ResurrectionCharges);
        Assert.True(state.Power.StoredKilowattHours < 20);
    }

    [Fact]
    public void BloodEvidence_PersistsUntilCleaningCapableActorRemovesIt()
    {
        var state = SingleRoomState();
        var injured = state.Crew[0];
        injured.Health = 55;
        injured.LastHealthSnapshot = 100;

        var evidenceSystem = new MedicalEvidenceSystem();
        evidenceSystem.Tick(state);
        var evidence = Assert.Single(state.BloodEvidence);

        state.Elapsed += TimeSpan.FromMinutes(1);
        evidenceSystem.Tick(state);
        Assert.Contains(evidence, state.BloodEvidence);

        injured.PositionX = evidence.X;
        injured.PositionY = evidence.Y;
        injured.CurrentAction = new NpcAction(ActionKind.CleanBlood, "room", "Clean spill.");
        evidenceSystem.Tick(state);

        Assert.Empty(state.BloodEvidence);
    }

    [Fact]
    public void Medbay_ContainsHighPowerResurrectionMachine()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 51515);
        var medbay = state.Facility.Rooms.Values.First(room => room.Type == RoomType.Medical);

        Assert.Contains(
            medbay.Fixtures,
            fixture => fixture.Type == FixtureType.ResurrectionChamber);
    }

    private static GameState SingleRoomState(double width = 20, double height = 20)
    {
        var state = new GameState { Facility = new Facility() };
        state.Facility.Rooms["room"] = new Room
        {
            Id = "room",
            Name = "Room",
            Type = RoomType.Engineering,
            MapX = 50,
            MapY = 50,
            MapWidth = width,
            MapHeight = height
        };
        state.Crew.Add(new Npc
        {
            Name = "Observer",
            Role = CrewRole.Technician,
            Personality = new Personality(50, 50, 50, 50),
            CurrentRoomId = "room",
            PositionX = 20,
            PositionY = 50
        });
        return state;
    }

    private static GameState TwoRoomState()
    {
        var state = new GameState { Facility = new Facility() };
        state.Facility.Rooms["a"] = new Room
        {
            Id = "a",
            Name = "A",
            Type = RoomType.ControlRoom,
            MapX = 40,
            MapY = 50,
            MapWidth = 20,
            MapHeight = 20
        };
        state.Facility.Rooms["b"] = new Room
        {
            Id = "b",
            Name = "B",
            Type = RoomType.Engineering,
            MapX = 60,
            MapY = 50,
            MapWidth = 20,
            MapHeight = 20
        };
        state.Facility.Doors.Add(new Door
        {
            Id = "door",
            RoomAId = "a",
            RoomBId = "b",
            IsOpen = true,
            IsPowered = true
        });
        state.Crew.Add(new Npc
        {
            Name = "Observer",
            Role = CrewRole.Security,
            Personality = new Personality(50, 50, 50, 50),
            CurrentRoomId = "a"
        });
        state.Crew.Add(new Npc
        {
            Name = "Target",
            Role = CrewRole.Engineer,
            Personality = new Personality(50, 50, 50, 50),
            CurrentRoomId = "b"
        });
        return state;
    }
}
