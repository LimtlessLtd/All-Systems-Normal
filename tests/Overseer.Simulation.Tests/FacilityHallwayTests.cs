using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class FacilityHallwayTests
{
    [Fact]
    public void EveryFunctionalRoomHasAHallwayWithADoorAtBothEnds()
    {
        var state = FacilitySeeder.CreateDefault();
        var facility = state.Facility;

        var functionalRooms = facility.Rooms.Values
            .Where(room =>
                room.Id != "corridor"
                && !room.Id.StartsWith("hall-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var room in functionalRooms)
        {
            var hallwayId = $"hall-{room.Id}";

            Assert.True(
                facility.Rooms.ContainsKey(hallwayId),
                $"Missing connector hallway for {room.Id}.");

            Assert.NotNull(
                facility.FindDoorBetween(room.Id, hallwayId));

            Assert.NotNull(
                facility.FindDoorBetween(hallwayId, "corridor"));

            Assert.Null(
                facility.FindDoorBetween(room.Id, "corridor"));
        }
    }

    [Fact]
    public void HallwayCanBeSealedIndependentlyAtEitherEnd()
    {
        var state = FacilitySeeder.CreateDefault();
        var facility = state.Facility;
        var roomDoor = facility.FindDoorBetween("engineering", "hall-engineering")!;
        var corridorDoor = facility.FindDoorBetween("hall-engineering", "corridor")!;

        roomDoor.IsOpen = false;
        roomDoor.IsLocked = true;

        Assert.False(roomDoor.IsPassable);
        Assert.True(corridorDoor.IsPassable);

        roomDoor.IsLocked = false;
        roomDoor.IsOpen = true;
        corridorDoor.IsOpen = false;
        corridorDoor.IsLocked = true;

        Assert.True(roomDoor.IsPassable);
        Assert.False(corridorDoor.IsPassable);
    }
}
