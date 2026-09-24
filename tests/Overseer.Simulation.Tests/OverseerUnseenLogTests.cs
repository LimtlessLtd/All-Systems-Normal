using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #23, slice 2: the event log's fire lines follow the cameras.
/// A fire event in a blind room stays in the log (debug telemetry keeps it)
/// but is tagged unseen, and the player's station log hides it.
/// </summary>
public sealed class OverseerUnseenLogTests
{
    [Fact]
    public void AFireGoingOutInAWatchedRoomReachesThePlayersLog()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.FireIntensity = .3;
        kitchen.OxygenPercent = 15;

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        var line = Assert.Single(state.EventLog, entry => entry.Contains($"Fire in {kitchen.Name} goes out", StringComparison.Ordinal));
        Assert.False(OverseerSightSystem.IsUnseen(line));
        Assert.Contains(line, StationLogPresentation.Notable(state.EventLog, 80));
    }

    [Fact]
    public void AFireGoingOutInABlindRoomIsKeptButHiddenFromThePlayer()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.CameraOnline = false;
        kitchen.FireIntensity = .3;
        kitchen.OxygenPercent = 15;

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        var line = Assert.Single(state.EventLog, entry => entry.Contains($"Fire in {kitchen.Name} goes out", StringComparison.Ordinal));
        Assert.True(OverseerSightSystem.IsUnseen(line));
        Assert.DoesNotContain(line, StationLogPresentation.Notable(state.EventLog, 80));
        Assert.Contains(line, DebugTelemetrySystem.Capture(state).Events);
    }

    [Fact]
    public void FireSpreadingIntoABlindCompartmentIsHiddenFromThePlayer()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var source = state.Facility.Rooms["hydroponics"];
        var door = state.Facility.Doors.Single(candidate =>
            candidate.RoomAId == source.Id || candidate.RoomBId == source.Id);
        var neighbour = state.Facility.Rooms[door.RoomAId == source.Id ? door.RoomBId : door.RoomAId];
        door.IsOpen = true;
        door.IsLocked = false;
        source.FireIntensity = 100;
        source.OxygenPercent = 21;
        neighbour.CameraNetworkReachable = false;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(neighbour.FireIntensity > 0);
        var line = Assert.Single(state.EventLog, entry => entry.Contains("FIRE SPREAD", StringComparison.Ordinal));
        Assert.True(OverseerSightSystem.IsUnseen(line));
        Assert.DoesNotContain(StationLogPresentation.Notable(state.EventLog, 80),
            entry => entry.Contains("FIRE", StringComparison.Ordinal));
    }

    [Fact]
    public void AFireBreachInABlindRoomShowsOnlyThePressureSensorLine()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var room = state.Facility.Rooms["engineering"];
        room.IsPowered = false;
        room.FireIntensity = 100;
        room.OxygenPercent = 21;
        room.HullIntegrityPercent = 0.5;

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(room.HasHullBreach);
        var notable = StationLogPresentation.Notable(state.EventLog, 80);
        Assert.DoesNotContain(notable, entry => entry.Contains("STRUCTURAL FAILURE", StringComparison.Ordinal));
        Assert.Contains(notable, entry => entry.Contains($"HULL BREACH: {room.Name} is losing pressure; no camera view.", StringComparison.Ordinal));
        Assert.Contains(state.EventLog, entry => entry.Contains("STRUCTURAL FAILURE", StringComparison.Ordinal));
    }

    [Fact]
    public void AFireBreachInAWatchedRoomNamesTheFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var room = state.Facility.Rooms["engineering"];
        room.FireIntensity = 100;
        room.OxygenPercent = 21;
        room.HullIntegrityPercent = 0.5;

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        var notable = StationLogPresentation.Notable(state.EventLog, 80);
        Assert.Contains(notable, entry => entry.Contains("STRUCTURAL FAILURE", StringComparison.Ordinal));
        Assert.DoesNotContain(state.EventLog, entry => entry.Contains("HULL BREACH:", StringComparison.Ordinal));
    }

    [Fact]
    public void WitnessedTagsOnlyWhatNoCameraCanSee()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var kitchen = state.Facility.Rooms["kitchen"];

        Assert.Equal("x", OverseerSightSystem.Witnessed(kitchen, "x"));
        kitchen.IsPowered = false;
        Assert.Equal(OverseerSightSystem.UnseenMarker + "x", OverseerSightSystem.Witnessed(kitchen, "x"));
    }
}
