using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class FacilityHallwayTests
{
    [Fact]
    public void EveryFunctionalRoomHasARealTwoDoorAccessPassage()
    {
        foreach (var seed in Enumerable.Range(1, 24))
        {
            var facility = FacilitySeeder.CreateDefault(stationSeed: seed).Facility;
            var functionalRooms = facility.Rooms.Values
                .Where(room => room.Type != RoomType.Corridor)
                .ToList();

            foreach (var room in functionalRooms)
            {
                var hallwayId = $"hall-{room.Id}";
                Assert.True(
                    facility.Rooms.TryGetValue(hallwayId, out var hallway),
                    $"Seed {seed}: missing connector hallway for {room.Id}.");

                var doors = facility.Doors.Where(door =>
                        door.RoomAId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase)
                        || door.RoomBId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Assert.Equal(2, doors.Count);
                Assert.NotNull(facility.FindDoorBetween(room.Id, hallwayId));

                var networkDoor = doors.Single(door => !door.Connects(room.Id, hallwayId));
                var networkRoomId = networkDoor.RoomAId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase)
                    ? networkDoor.RoomBId
                    : networkDoor.RoomAId;

                Assert.Equal(RoomType.Corridor, facility.Rooms[networkRoomId].Type);
                Assert.False(networkRoomId.StartsWith("hall-", StringComparison.OrdinalIgnoreCase));
                Assert.Null(facility.FindDoorBetween(room.Id, networkRoomId));
            }
        }
    }

    [Fact]
    public void AccessPassageCanBeSealedIndependentlyAtEitherEnd()
    {
        var facility = FacilitySeeder.CreateDefault(stationSeed: 74021).Facility;
        var hallwayId = "hall-engineering";
        var roomDoor = facility.FindDoorBetween("engineering", hallwayId)!;
        var networkDoor = facility.Doors.Single(door =>
            (door.RoomAId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase)
             || door.RoomBId.Equals(hallwayId, StringComparison.OrdinalIgnoreCase))
            && !door.Connects("engineering", hallwayId));

        roomDoor.IsOpen = false;
        roomDoor.IsLocked = true;

        Assert.False(roomDoor.IsPassable);
        Assert.True(networkDoor.IsPassable);

        roomDoor.IsLocked = false;
        roomDoor.IsOpen = true;
        networkDoor.IsOpen = false;
        networkDoor.IsLocked = true;

        Assert.True(roomDoor.IsPassable);
        Assert.False(networkDoor.IsPassable);
    }
}
