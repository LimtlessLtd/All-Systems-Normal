using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class VacuumAndBodyDiscoveryTests
{
    [Fact]
    public void OpeningOuterHatch_VentsAirlockButClosedInnerHatchContainsVacuum()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var hallway = state.Facility.Rooms["hall-airlock"];
        var innerDoor = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;

        Assert.False(innerDoor.IsOpen);
        airlock.ExteriorHatchOpen = true;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(airlock.PressureKpa < 30);
        Assert.InRange(hallway.PressureKpa, 101.2, 101.3);
        Assert.Contains("airlock", EnvironmentSystem.FindVacuumDepths(state).Keys);
        Assert.DoesNotContain("hall-airlock", EnvironmentSystem.FindVacuumDepths(state).Keys);
    }

    [Fact]
    public void OpeningBothAirlockHatches_PropagatesDecompressionIntoOpenStationTopology()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var hallway = state.Facility.Rooms["hall-airlock"];
        var innerDoor = state.Facility.FindDoorBetween("airlock", "hall-airlock")!;
        var networkDoor = state.Facility.Doors.Single(door =>
            (door.RoomAId == "hall-airlock" || door.RoomBId == "hall-airlock")
            && !door.Connects("airlock", "hall-airlock"));
        var networkRoomId = networkDoor.RoomAId == "hall-airlock"
            ? networkDoor.RoomBId
            : networkDoor.RoomAId;
        var networkRoom = state.Facility.Rooms[networkRoomId];

        innerDoor.IsOpen = true;
        airlock.ExteriorHatchOpen = true;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(airlock.PressureKpa < hallway.PressureKpa);
        Assert.True(hallway.PressureKpa < networkRoom.PressureKpa);
        Assert.True(networkRoom.PressureKpa < 101.3);

        var vacuum = EnvironmentSystem.FindVacuumDepths(state);
        Assert.Equal(0, vacuum["airlock"]);
        Assert.Equal(1, vacuum["hall-airlock"]);
        Assert.Equal(2, vacuum[networkRoomId]);
    }

    [Fact]
    public void CrewInVentedAirlock_AreLostToSpaceAndLeaveNoBodyAboard()
    {
        var state = FacilitySeeder.CreateDefault();
        var victim = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var airlock = state.Facility.Rooms["airlock"];

        victim.CurrentRoomId = "airlock";
        airlock.ExteriorHatchOpen = true;

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(1));
        new VacuumConsequenceSystem().Tick(state);

        Assert.False(victim.IsAlive);
        Assert.False(victim.IsPresent);
        Assert.Contains("no body remains aboard", victim.CauseOfDeath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PresentCorpse_CanBeDiscoveredButEjectedCrewCannot()
    {
        var state = FacilitySeeder.CreateDefault();
        var witness = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var body = state.Crew.Single(npc => npc.Name == "Emma Voss");

        witness.CurrentRoomId = "medical";
        body.CurrentRoomId = "medical";
        body.Health = 0;
        body.IsPresent = true;
        body.CauseOfDeath = "Died from oxygen deprivation.";

        var suspicion = new SuspicionSystem();
        suspicion.Tick(state);

        Assert.Contains(body.Id, witness.DiscoveredBodies);
        Assert.True(witness.OverseerSuspicion > 0);

        var before = witness.OverseerSuspicion;
        body.IsPresent = false;
        witness.DiscoveredBodies.Clear();
        witness.OverseerEvidence.Clear();

        suspicion.Tick(state);

        Assert.Empty(witness.DiscoveredBodies);
        Assert.Equal(before, witness.OverseerSuspicion);
    }
}
