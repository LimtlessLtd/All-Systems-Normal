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
