using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationHazardPolishTests
{
    [Fact]
    public void SevereFireSpreadsIntoConnectedAccessTunnel()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var source = state.Facility.Rooms["hydroponics"];
        var door = state.Facility.Doors.Single(candidate =>
            candidate.RoomAId == source.Id || candidate.RoomBId == source.Id);
        var neighbourId = door.RoomAId == source.Id ? door.RoomBId : door.RoomAId;
        var neighbour = state.Facility.Rooms[neighbourId];

        Assert.Equal(RoomType.Corridor, neighbour.Type);

        door.IsOpen = true;
        door.IsLocked = false;
        source.FireIntensity = 100;
        source.OxygenPercent = 21;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(
            neighbour.FireIntensity > 0,
            "A severe uncontrolled fire should visibly flash over into its open access tunnel.");
        Assert.True(neighbour.SmokePercent > 0);
    }

    [Fact]
    public void SmokePropagatesThroughOpenCompartmentsButASealedHatchContainsIt()
    {
        var openState = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var source = openState.Facility.Rooms["hydroponics"];
        var door = openState.Facility.Doors.First(candidate =>
            candidate.RoomAId == source.Id || candidate.RoomBId == source.Id);
        var neighbourId = door.RoomAId == source.Id ? door.RoomBId : door.RoomAId;
        var neighbour = openState.Facility.Rooms[neighbourId];

        source.SmokePercent = 100;
        neighbour.SmokePercent = 0;
        door.IsOpen = true;
        door.IsLocked = false;

        new StationHazardSystem().Tick(openState, TimeSpan.FromMinutes(1));

        Assert.True(neighbour.SmokePercent > 0);

        var sealedState = FacilitySeeder.CreateDefault(stationSeed: 1337);
        source = sealedState.Facility.Rooms["hydroponics"];
        door = sealedState.Facility.Doors.First(candidate =>
            candidate.RoomAId == source.Id || candidate.RoomBId == source.Id);
        neighbourId = door.RoomAId == source.Id ? door.RoomBId : door.RoomAId;
        neighbour = sealedState.Facility.Rooms[neighbourId];

        source.SmokePercent = 100;
        neighbour.SmokePercent = 0;
        door.IsOpen = false;
        door.IsLocked = false;

        new StationHazardSystem().Tick(sealedState, TimeSpan.FromMinutes(1));

        Assert.Equal(0, neighbour.SmokePercent, 6);
    }

    [Fact]
    public void ExtremeSmokeMakesVisibilityNearZeroAndHurtsCrewWithoutLocalFlames()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        room.FireIntensity = 0;
        room.SmokePercent = 95;
        room.VentilationEnabled = false;
        var healthBefore = npc.Health;

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.InRange(room.VisibilityPercent, 0, 1);
        Assert.True(npc.Health < healthBefore);
        Assert.True(npc.NeedsMindReconsideration);
    }

    [Fact]
    public void ConventionalFirefightingKnocksFireDownButDoesNotOneClickASevereFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var npc = state.Crew[0];
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        npc.Skills["Engineering"] = 100;
        room.FireIntensity = 70;
        room.SmokePercent = 40;

        Assert.True(StationHazardSystem.TryExecuteCrewAction(
            state,
            npc,
            ActionKind.FightFire,
            room,
            out _));

        Assert.InRange(room.FireIntensity, 50, 69);
        Assert.True(room.SmokePercent >= 30);
    }

    [Fact]
    public void UncontrolledFireCanBreachHullAndExistingAtmosphereSystemDecompressesRoom()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var room = state.Facility.Rooms["engineering"];
        room.FireIntensity = 100;
        room.OxygenPercent = 21;
        room.HullIntegrityPercent = 0.5;
        room.PressureKpa = 101.3;

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(room.HasHullBreach);
        Assert.Equal(0, room.HullIntegrityPercent, 6);

        var beforePressure = room.PressureKpa;
        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(
            room.PressureKpa < beforePressure,
            $"Hull breach did not feed the existing decompression model: {beforePressure:0.0} -> {room.PressureKpa:0.0} kPa.");
    }
}
