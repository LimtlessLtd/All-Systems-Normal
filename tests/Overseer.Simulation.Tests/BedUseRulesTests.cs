using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class BedUseRulesTests
{
    [Fact]
    public void ConcurrentSleepersReceiveDistinctBedsUntilCapacityIsFull()
    {
        var state = FacilitySeeder.CreateDefault(SeededCrewRosterGenerator.Generate(4242), stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.Take(7).ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep capacity regression.");
            npc.Intent = null;
            npc.Movement = null;
        }

        var assignments = sleepers
            .Select(npc => BedUseRules.AssignedBed(state, npc))
            .ToList();

        var beds = quarters.Fixtures.Count(fixture => fixture.Type == FixtureType.Bed);
        Assert.Equal(6, beds);
        Assert.Equal(beds, assignments.Count(fixture => fixture is not null));
        Assert.Equal(
            beds,
            assignments
                .Where(fixture => fixture is not null)
                .Select(fixture => fixture!.Label)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.Single(assignments, fixture => fixture is null);
    }

    [Fact]
    public void SleepersPhysicallyWalkTowardDifferentAssignedBeds()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.Take(2).ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep in a real bunk.");
            npc.Intent = null;
            npc.Movement = null;
        }

        var firstBed = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleepers[0]));
        var secondBed = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleepers[1]));
        Assert.NotEqual(firstBed.Label, secondBed.Label);

        var movement = new LocalMovementSystem();
        for (var minute = 0; minute < 20; minute++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.NotEqual(
            (Math.Round(sleepers[0].PositionX, 2), Math.Round(sleepers[0].PositionY, 2)),
            (Math.Round(sleepers[1].PositionX, 2), Math.Round(sleepers[1].PositionY, 2)));
    }

    [Fact]
    public void SettledSleeperKeepsAssignedBedWhenPeerStopsSleeping()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.Take(2)
            .OrderBy(npc => npc.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep in a stable bunk.");
            npc.Intent = null;
            npc.Movement = null;
        }

        var movement = new LocalMovementSystem();
        for (var minute = 0; minute < 20; minute++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
        }

        var retained = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleepers[1]));

        sleepers[0].CurrentAction = new NpcAction(ActionKind.Idle, quarters.Id, "Done resting.");

        var after = Assert.IsType<RoomFixture>(BedUseRules.AssignedBed(state, sleepers[1]));
        Assert.Equal(retained.Label, after.Label);
    }

    [Fact]
    public void SleeperWithoutABedDoesNotReceiveRestorativeRecovery()
    {
        var state = FacilitySeeder.CreateDefault(SeededCrewRosterGenerator.Generate(4242), stationSeed: 1337);
        var quarters = state.Facility.Rooms["quarters"];
        var sleepers = state.Crew.Take(7).ToList();

        foreach (var npc in sleepers)
        {
            npc.CurrentRoomId = quarters.Id;
            npc.CurrentAction = new NpcAction(ActionKind.Sleep, quarters.Id, "Sleep capacity regression.");
            npc.Fatigue = 80;
            npc.SleepDebtMinutes = 240;
        }

        var overflow = sleepers.Single(npc => BedUseRules.AssignedBed(state, npc) is null);
        var fatigueBefore = overflow.Fatigue;
        var debtBefore = overflow.SleepDebtMinutes;

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(overflow.Fatigue >= fatigueBefore);
        Assert.True(overflow.SleepDebtMinutes >= debtBefore);
    }
}
