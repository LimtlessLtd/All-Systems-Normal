using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class PanicAlertSystemTests
{
    [Fact]
    public void FleeingADangerousRoom_SoundsAnIdentifiedClaimForSameRoomWitnesses()
    {
        var (state, fleeing, witness) = SameRoom();
        state.Facility.Rooms[fleeing.CurrentRoomId].FireIntensity = 50;
        fleeing.Intent = FleeIntent(state, fleeing);

        new PanicAlertSystem().Tick(state);

        var memory = Assert.Single(witness.Memories, m => m.PanicClaimRoomId is not null);
        Assert.Contains(fleeing.Name, memory.Description, StringComparison.Ordinal);
        Assert.Contains("a fire", memory.Description, StringComparison.Ordinal);
        Assert.True(witness.NeedsMindReconsideration);
    }

    [Fact]
    public void FleeingInTheDarkAwayFromWitnesses_SoundsAnAnonymousClaim()
    {
        var (state, fleeing, witness) = SameRoom();
        var room = state.Facility.Rooms[fleeing.CurrentRoomId];
        room.FireIntensity = 50;
        room.LightsOn = false;
        fleeing.PositionX = 5;
        fleeing.PositionY = 5;
        witness.PositionX = 95;
        witness.PositionY = 95;
        fleeing.Intent = FleeIntent(state, fleeing);

        new PanicAlertSystem().Tick(state);

        var memory = Assert.Single(witness.Memories, m => m.PanicClaimRoomId is not null);
        Assert.DoesNotContain(fleeing.Name, memory.Description, StringComparison.Ordinal);
        Assert.Contains("Someone", memory.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenHatch_CarriesAnAnonymousClaimIntoTheAdjacentRoom()
    {
        var (state, fleeing, _) = SameRoom();
        var room = state.Facility.Rooms[fleeing.CurrentRoomId];
        room.FireIntensity = 50;
        fleeing.Intent = FleeIntent(state, fleeing);

        var adjacentRoomId = state.Facility.Rooms.Keys.First(id => id != room.Id);
        var hearer = state.Crew[3];
        hearer.CurrentRoomId = adjacentRoomId;
        state.Facility.Doors.Add(new Door
        {
            Id = "test-hatch",
            RoomAId = room.Id,
            RoomBId = adjacentRoomId,
            IsOpen = true,
        });

        new PanicAlertSystem().Tick(state);

        var memory = Assert.Single(hearer.Memories, m => m.PanicClaimRoomId is not null);
        Assert.Contains("through the hatch", memory.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(fleeing.Name, memory.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedHatch_DoesNotCarryTheClaimIntoTheAdjacentRoom()
    {
        var (state, fleeing, _) = SameRoom();
        var room = state.Facility.Rooms[fleeing.CurrentRoomId];
        room.FireIntensity = 50;
        fleeing.Intent = FleeIntent(state, fleeing);

        var adjacentRoomId = state.Facility.Rooms.Keys.First(id => id != room.Id);
        var hearer = state.Crew[3];
        hearer.CurrentRoomId = adjacentRoomId;
        state.Facility.Doors.Add(new Door
        {
            Id = "test-hatch",
            RoomAId = room.Id,
            RoomBId = adjacentRoomId,
            IsOpen = false,
        });

        new PanicAlertSystem().Tick(state);

        Assert.DoesNotContain(hearer.Memories, m => m.PanicClaimRoomId is not null);
    }

    [Fact]
    public void ContinuingToFleeTheSameDanger_DoesNotSoundASecondAlarm()
    {
        var (state, fleeing, witness) = SameRoom();
        state.Facility.Rooms[fleeing.CurrentRoomId].FireIntensity = 50;
        fleeing.Intent = FleeIntent(state, fleeing);
        var system = new PanicAlertSystem();

        system.Tick(state);
        fleeing.Intent = FleeIntent(state, fleeing);
        system.Tick(state);

        Assert.Single(witness.Memories, m => m.PanicClaimRoomId is not null);
    }

    [Fact]
    public void ReachingSafetyThenFleeingAgain_SoundsASeparateAlarm()
    {
        var (state, fleeing, witness) = SameRoom();
        var room = state.Facility.Rooms[fleeing.CurrentRoomId];
        room.FireIntensity = 50;
        fleeing.Intent = FleeIntent(state, fleeing);
        var system = new PanicAlertSystem();
        system.Tick(state);
        Assert.True(fleeing.PanicAlertSounded);

        room.FireIntensity = 0;
        system.Tick(state);
        Assert.False(fleeing.PanicAlertSounded);

        room.FireIntensity = 50;
        fleeing.Intent = FleeIntent(state, fleeing);
        system.Tick(state);

        Assert.Equal(2, witness.Memories.Count(m => m.PanicClaimRoomId is not null));
    }

    [Fact]
    public void NotActuallyFleeing_DoesNotSoundAnAlarmEvenInDanger()
    {
        var (state, fleeing, witness) = SameRoom();
        state.Facility.Rooms[fleeing.CurrentRoomId].FireIntensity = 50;
        fleeing.Intent = null;

        new PanicAlertSystem().Tick(state);

        Assert.DoesNotContain(witness.Memories, m => m.PanicClaimRoomId is not null);
    }

    [Fact]
    public void SafeRoom_NeverSoundsAnAlarmEvenWhileMoving()
    {
        var (state, fleeing, witness) = SameRoom();
        fleeing.Intent = FleeIntent(state, fleeing);

        new PanicAlertSystem().Tick(state);

        Assert.DoesNotContain(witness.Memories, m => m.PanicClaimRoomId is not null);
    }

    private static NpcIntent FleeIntent(GameState state, Npc fleeing) => new(
        ActionKind.Move,
        state.Facility.Rooms.Keys.First(id => id != fleeing.CurrentRoomId),
        "Get somewhere safer.",
        "This room is dangerous.",
        100,
        "Test",
        state.Elapsed);

    private static (GameState State, Npc Fleeing, Npc Witness) SameRoom()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var fleeing = state.Crew[0];
        var witness = state.Crew[1];
        var room = state.Facility.Rooms[fleeing.CurrentRoomId];
        room.IsPowered = true;
        room.LightsOn = true;
        foreach (var npc in state.Crew)
        {
            npc.NeedsMindReconsideration = false;
        }

        foreach (var npc in new[] { fleeing, witness })
        {
            npc.CurrentRoomId = room.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
        }

        return (state, fleeing, witness);
    }
}
