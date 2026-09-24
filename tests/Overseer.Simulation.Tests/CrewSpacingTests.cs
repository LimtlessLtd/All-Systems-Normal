using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #95: crew must not stand on top of each other. A 10-seed × 24h
/// soak found most stacking at shared destinations: everyone working in a
/// room went to its first workstation, a second toilet user stood on the
/// first, and two people seeking each other out chased into a corner.
/// </summary>
public sealed class CrewSpacingTests
{
    [Fact]
    public void CrewWorkingInOneRoom_UseDistinctWorkstations()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var control = state.Facility.Rooms["control"];
        Clear(state, control);
        var workstations = control.Fixtures.Count(fixture => fixture.Type == FixtureType.Console);
        Assert.True(workstations >= 3);

        var workers = state.Crew.Take(3).ToList();
        foreach (var npc in workers)
        {
            Place(state, npc, control, new NpcAction(ActionKind.Work, control.Id, "Working."));
        }

        Walk(state, minutes: 10);

        AssertApart(control, workers);
    }

    [Fact]
    public void ASecondToiletUser_WaitsInsteadOfStandingOnTheFirst()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var washroom = state.Facility.Rooms["washroom"];
        Clear(state, washroom);
        Assert.Single(washroom.Fixtures, fixture => fixture.Type == FixtureType.Toilet);

        var users = state.Crew.Take(2).ToList();
        foreach (var npc in users)
        {
            Place(state, npc, washroom, new NpcAction(ActionKind.UseToilet, washroom.Id, "Need the toilet."));
        }

        Walk(state, minutes: 10);

        AssertApart(washroom, users);
    }

    [Fact]
    public void TwoPeopleSeekingEachOtherOut_MeetSideBySide_NotInACorner()
    {
        var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
        var lounge = state.Facility.Rooms["lounge"];
        Clear(state, lounge);
        var (a, b) = (state.Crew[0], state.Crew[1]);
        Place(state, a, lounge, new NpcAction(ActionKind.Socialize, b.Name, "Catching up."));
        Place(state, b, lounge, new NpcAction(ActionKind.Socialize, a.Name, "Catching up."));
        a.PositionX = 30;
        a.PositionY = 40;
        b.PositionX = 60;
        b.PositionY = 60;

        Walk(state, minutes: 10);

        AssertApart(lounge, [a, b]);
        Assert.True(
            Distance(lounge, a, b) < 3,
            "they end up close enough to talk");
        Assert.All([a, b], npc => Assert.True(
            npc.PositionX < 90 && npc.PositionY < 90,
            $"{npc.Name} was chased into the corner at ({npc.PositionX:0}, {npc.PositionY:0})"));
    }

    private static void Place(GameState state, Npc npc, Room room, NpcAction action)
    {
        npc.CurrentRoomId = room.Id;
        npc.PositionX = 50;
        npc.PositionY = 50;
        npc.Movement = null;
        npc.Intent = null;
        npc.CurrentAction = action;
    }

    /// <summary>Sends everyone else to another room so only the test's crew share this one.</summary>
    private static void Clear(GameState state, Room room)
    {
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = room.Id == "quarters" ? "control" : "quarters";
            npc.Movement = null;
        }
    }

    private static void Walk(GameState state, int minutes)
    {
        var movement = new LocalMovementSystem();
        for (var i = 0; i < minutes * 3; i++)
        {
            movement.Tick(state, TimeSpan.FromSeconds(20));
        }
    }

    /// <summary>Map units; the same line the overlap soak measured stacking by.</summary>
    private const double StackedWithin = 0.35;

    private static void AssertApart(Room room, IReadOnlyList<Npc> crew)
    {
        for (var i = 0; i < crew.Count; i++)
        {
            for (var j = i + 1; j < crew.Count; j++)
            {
                Assert.True(
                    Distance(room, crew[i], crew[j]) > StackedWithin,
                    $"{crew[i].Name} and {crew[j].Name} stand on the same spot " +
                    $"({crew[i].PositionX:0.0}, {crew[i].PositionY:0.0}) / ({crew[j].PositionX:0.0}, {crew[j].PositionY:0.0})");
            }
        }
    }

    private static double Distance(Room room, Npc a, Npc b)
    {
        var dx = (a.PositionX - b.PositionX) / 100d * room.MapWidth;
        var dy = (a.PositionY - b.PositionY) / 100d * room.MapHeight;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
