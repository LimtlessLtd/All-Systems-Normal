using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #76: a fire starts at a point and its front grows from there.
/// </summary>
public sealed class FireFrontTests
{
    [Fact]
    public void AFailingMachineLightsTheFireAtItsOwnFixture()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        foreach (var device in state.Devices.Values)
        {
            device.Condition = 0;
        }

        var hazards = new StationHazardSystem();
        Room? burning = null;
        for (var minute = 12; minute <= 12 * 40 && burning is null; minute += 12)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            hazards.Tick(state, TimeSpan.FromMinutes(1));
            burning = state.Facility.Rooms.Values.FirstOrDefault(room => room.FireIntensity > 0);
        }

        Assert.NotNull(burning);
        var origin = (burning.FireOriginX, burning.FireOriginY);
        Assert.Contains(
            state.Devices.Values
                .Where(device => device.RoomId == burning.Id)
                .Select(device => FireFrontRules.MachineOrigin(burning, device)),
            candidate => (candidate.X, candidate.Y) == (origin.FireOriginX, origin.FireOriginY));
    }

    [Fact]
    public void TheFrontGrowsWithTheFireUntilItFillsTheRoom()
    {
        Assert.Equal(0, FireFrontRules.FrontRadius(0));
        Assert.True(FireFrontRules.FrontRadius(16) < 50, "A newly lit fire is a local patch.");
        Assert.True(FireFrontRules.FrontRadius(40) > FireFrontRules.FrontRadius(16));
        Assert.True(
            FireFrontRules.FrontRadius(75) >= Math.Sqrt(2) * 100,
            "An inferno reaches every corner even from a corner origin.");
    }

    [Fact]
    public void FlamesBurnOnlyCrewTheFrontHasReached()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var room = state.Facility.Rooms["hydroponics"];
        var near = state.Crew[0];
        var far = state.Crew[1];
        foreach (var npc in new[] { near, far })
        {
            npc.CurrentRoomId = room.Id;
            npc.Movement = null;
            npc.Health = 100;
        }

        near.PositionX = 12;
        near.PositionY = 12;
        far.PositionX = 92;
        far.PositionY = 92;
        FireFrontRules.Ignite(room, 8, 8, 50);
        room.OxygenPercent = 21;
        state.Elapsed = TimeSpan.FromMinutes(1);

        Assert.True(FireFrontRules.IsInsideFront(room, near.PositionX, near.PositionY));
        Assert.False(FireFrontRules.IsInsideFront(room, far.PositionX, far.PositionY));

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(near.Health < 100, "Crew inside the front are burned.");
        Assert.True(far.Stress > 0 || far.Fear > 0, "Crew across the room still feel the fire.");
        Assert.True(far.Health >= 100 - 1e-9 || far.Health > near.Health);
    }

    [Fact]
    public void AFireWithNoKnownOriginStillFillsTheRoom()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var room = state.Facility.Rooms["hydroponics"];
        room.FireIntensity = 30;

        Assert.True(FireFrontRules.IsInsideFront(room, 2, 2));
        Assert.True(FireFrontRules.IsInsideFront(room, 98, 98));
    }

    [Fact]
    public void FireSpreadingThroughAHatchStartsAtThatHatch()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var source = state.Facility.Rooms["hydroponics"];
        var door = state.Facility.Doors.Single(candidate =>
            candidate.RoomAId == source.Id || candidate.RoomBId == source.Id);
        var neighbour = state.Facility.Rooms[door.RoomAId == source.Id ? door.RoomBId : door.RoomAId];
        door.IsOpen = true;
        door.IsLocked = false;
        FireFrontRules.Ignite(source, 50, 50, 100);
        source.OxygenPercent = 21;
        state.Elapsed = TimeSpan.FromMinutes(5);

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(neighbour.FireIntensity > 0);
        var portal = StationGeometry.FindSharedPortal(source, neighbour);
        var expectedX = 50 + ((portal.X - neighbour.MapX) / neighbour.MapWidth * 100);
        var expectedY = 50 + ((portal.Y - neighbour.MapY) / neighbour.MapHeight * 100);
        Assert.Equal(Math.Clamp(expectedX, 0, 100), neighbour.FireOriginX!.Value, 6);
        Assert.Equal(Math.Clamp(expectedY, 0, 100), neighbour.FireOriginY!.Value, 6);
        Assert.True(
            neighbour.FireOriginX is <= 1 or >= 99 || neighbour.FireOriginY is <= 1 or >= 99,
            "The hatch is on the receiving room's wall.");
    }

    [Fact]
    public void AnExtinguishedFireForgetsItsOrigin()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var room = state.Facility.Rooms["hydroponics"];
        FireFrontRules.Ignite(room, 20, 30, 20);
        room.FireIntensity = 0;
        state.Elapsed = TimeSpan.FromMinutes(1);

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Null(room.FireOriginX);
        Assert.Null(room.FireOriginY);
    }

    [Fact]
    public void MapDrawsTheFireFrontFromSimulationState()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Overseer.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        var home = File.ReadAllText(Path.Combine(root, "src", "Overseer.Web.UI", "Pages", "Home.razor"));
        Assert.Contains("--fire-x:{x:0.##}%;--fire-y:{y:0.##}%;--fire-r:{FireFrontRules.FrontRadius(room.FireIntensity):0.##}%;", home);
        Assert.Contains("{FireFrontStyle(room)}", home);
    }
}
