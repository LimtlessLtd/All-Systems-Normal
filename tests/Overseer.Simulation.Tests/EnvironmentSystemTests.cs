using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class EnvironmentSystemTests
{
    [Fact]
    public void PoweredClimateControl_MovesRoomTowardSetpoint()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["quarters"];
        room.TemperatureC = 21;
        room.TemperatureSetpointC = 28;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(room.TemperatureC > 21);
        Assert.True(room.TemperatureC <= 28.2);
    }

    [Fact]
    public void TheAirLoopKeepsCorridorsHabitable_AndTheyGoColdWithoutIt()
    {
        // 2026-09-24 36h soak: heat loss and the air loop were applied as two
        // fixed steps, so the larger (heat loss) won and every corridor slid to
        // 11C after ~18h. Crew crossing them took "cold room" stress all day,
        // crews passed the stress ceiling for fighting fires, and stations
        // burned. With the loop running a corridor settles between the two.
        var state = FacilitySeeder.CreateDefault();
        var corridor = state.Facility.Rooms.Values.First(room => room.Type == RoomType.Corridor);
        foreach (var npc in state.Crew)
            npc.CurrentRoomId = "quarters";
        Assert.False(corridor.HasTemperatureControl);
        Assert.True(state.LifeSupport.IsOnline);

        var environment = new EnvironmentSystem();
        for (var minute = 0; minute < 24 * 60; minute++)
            environment.Tick(state, TimeSpan.FromMinutes(1));

        Assert.InRange(corridor.TemperatureC, 17.5, 19.5);

        // Without the loop, hull heat loss takes over.
        corridor.VentilationEnabled = false;
        for (var minute = 0; minute < 6 * 60; minute++)
            environment.Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(corridor.TemperatureC < 16, $"{corridor.TemperatureC:0.0}C");
    }

    [Fact]
    public void NearVacuumRoom_CoolsTowardDeepSpaceFloorRegardlessOfClimateControl()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["quarters"];
        room.TemperatureC = 21;
        room.TemperatureSetpointC = 21;
        room.IsPowered = true;
        room.HasTemperatureControl = true;
        room.TemperatureControlOnline = true;
        room.VentilationEnabled = false;
        room.PressureKpa = 0.5;
        room.OxygenPercent = 0;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(120));

        Assert.True(room.TemperatureC < -20);
    }

    [Fact]
    public void OxygenFreeButPressurisedRoom_IsNotTreatedAsVacuum()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["quarters"];
        room.TemperatureC = 21;
        room.TemperatureSetpointC = 28;
        room.IsPowered = true;
        room.HasTemperatureControl = true;
        room.TemperatureControlOnline = true;
        room.VentilationEnabled = false;
        room.PressureKpa = 101.3;
        room.OxygenPercent = 0;
        room.CarbonDioxidePercent = 10;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(room.TemperatureC > 21);
        Assert.True(room.TemperatureC <= 28.2);
    }

    [Fact]
    public void RestoringPressureLetsAFormerVacuumRoomWarmBackTowardNormal()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["quarters"];
        room.TemperatureC = -90;
        room.TemperatureSetpointC = 21;
        room.PressureKpa = 101.3;
        room.OxygenPercent = 20.9;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(room.TemperatureC > -90);
    }

    [Fact]
    public void IsolatedOccupiedRoom_LosesOxygenAndAccumulatesCo2()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["control"];
        room.VentilationEnabled = false;

        var oxygenBefore = room.OxygenPercent;
        var co2Before = room.CarbonDioxidePercent;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(30));

        Assert.True(room.OxygenPercent < oxygenBefore);
        Assert.True(room.CarbonDioxidePercent > co2Before);
    }

    [Fact]
    public void GlobalLifeSupportLoss_DegradesOccupiedRoomAtmosphere()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["medical"];
        state.LifeSupport.IsOnline = false;

        var oxygenBefore = room.OxygenPercent;
        var co2Before = room.CarbonDioxidePercent;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(30));

        Assert.True(room.OxygenPercent < oxygenBefore);
        Assert.True(room.CarbonDioxidePercent > co2Before);
    }

    [Fact]
    public void Hydroponics_IsAutonomousEnvironmentalZone()
    {
        var state = FacilitySeeder.CreateDefault();
        var hydroponics = state.Facility.Rooms["hydroponics"];

        Assert.Equal(RoomType.Hydroponics, hydroponics.Type);
        Assert.True(hydroponics.HasTemperatureControl);
        Assert.False(hydroponics.IsTemperatureAiControllable);
        Assert.False(hydroponics.IsVentilationAiControllable);
        Assert.Equal(24, hydroponics.TemperatureSetpointC);
        Assert.Contains(
            hydroponics.Fixtures,
            fixture => fixture.Type == FixtureType.GrowBed);
    }

    [Fact]
    public void DangerousAtmosphere_DamagesCrewDeterministically()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew.Single(crew => crew.Name == "Nadia Okafor");
        var room = state.Facility.Rooms[npc.CurrentRoomId];

        room.OxygenPercent = 14;
        room.CarbonDioxidePercent = 4.5;
        var healthBefore = npc.Health;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(10));

        Assert.True(npc.Health < healthBefore);
        Assert.True(npc.Stress > 10);
    }
}
