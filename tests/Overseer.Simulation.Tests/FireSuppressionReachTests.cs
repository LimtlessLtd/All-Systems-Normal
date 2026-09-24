using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #76: suppressing a fire means walking up to its front. Someone
/// on the far side of a large compartment cannot put out a local patch of
/// flames from where they stand.
/// </summary>
public sealed class FireSuppressionReachTests
{
    [Fact]
    public void TheAttackPointSitsJustOutsideTheFront_FromEitherSide()
    {
        var room = new Room { Id = "test", Name = "Test", Type = RoomType.Recreation, FireIntensity = 16 };
        FireFrontRules.Ignite(room, 30, 50, 16);
        var radius = FireFrontRules.FrontRadius(16);

        var fromFar = Assert.NotNull(FireFrontRules.AttackPoint(room, 92, 50));
        Assert.Equal(30 + radius + (FireFrontRules.SuppressionReach / 2), fromFar.X, 6);
        Assert.Equal(50, fromFar.Y, 6);
        Assert.False(FireFrontRules.IsInsideFront(room, fromFar.X, fromFar.Y));
        Assert.True(FireFrontRules.CanReachFront(room, fromFar.X, fromFar.Y));

        // Someone standing in the flames backs out to the same edge.
        var fromInside = Assert.NotNull(FireFrontRules.AttackPoint(room, 35, 50));
        Assert.Equal(fromFar.X, fromInside.X, 6);

        Assert.False(FireFrontRules.CanReachFront(room, 92, 50));
    }

    [Fact]
    public void AFireWithNoOriginFillsTheRoom_SoItCanBeReachedFromAnywhere()
    {
        var room = new Room { Id = "test", Name = "Test", Type = RoomType.Recreation, FireIntensity = 20 };

        Assert.Null(FireFrontRules.AttackPoint(room, 10, 10));
        Assert.True(FireFrontRules.CanReachFront(room, 10, 10));
    }

    [Fact]
    public void SuppressingFromAcrossTheRoom_IsRejected()
    {
        var (state, room, fighter) = FarCornerFire();
        var intensity = room.FireIntensity;

        Assert.False(StationHazardSystem.TryExecuteCrewAction(
            state,
            fighter,
            ActionKind.FightFire,
            room,
            out var message));

        Assert.Contains("too far from the flames", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(intensity, room.FireIntensity);
    }

    [Fact]
    public void AFireFighterWalksUpToTheFront_BeforeSuppressionStarts_ThenKnocksItDown()
    {
        var (state, room, fighter) = FarCornerFire();
        var intensity = room.FireIntensity;
        fighter.Intent = new NpcIntent(
            ActionKind.FightFire,
            room.Id,
            "Put out the fire.",
            "The corner is burning.",
            80,
            "Test",
            state.Elapsed);

        var intents = new IntentExecutionSystem();
        var movement = new LocalMovementSystem();

        intents.Tick(state);
        Assert.Equal(ActionKind.FightFire, fighter.CurrentAction.Kind);
        Assert.Null(fighter.ActiveTask);

        var startedInReach = false;
        for (var i = 0; i < 20 && fighter.Intent is not null; i++)
        {
            movement.Tick(state, TimeSpan.FromMinutes(1));
            intents.Tick(state);
            if (fighter.ActiveTask is { Action: ActionKind.FightFire } task
                && task.Status == CrewTaskStatus.InProgress
                && !startedInReach)
            {
                startedInReach = FireFrontRules.CanReachFront(room, fighter.PositionX, fighter.PositionY);
                Assert.True(startedInReach, "suppression only starts within reach of the front");
            }

            state.Elapsed += TimeSpan.FromMinutes(1);
        }

        Assert.True(startedInReach);
        Assert.True(room.FireIntensity < intensity, "the fire-fighter knocked the fire down once in reach");
        Assert.False(
            FireFrontRules.IsInsideFront(room, fighter.PositionX, fighter.PositionY),
            "they fight it from the edge, not from inside the flames");
    }

    private static (GameState State, Room Room, Npc Fighter) FarCornerFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var room = state.Facility.Rooms["hydroponics"];
        FireFrontRules.Ignite(room, 88, 88, 16);

        var fighter = state.Crew[0];
        fighter.CurrentRoomId = room.Id;
        fighter.PositionX = 10;
        fighter.PositionY = 10;
        fighter.Movement = null;
        fighter.Intent = null;
        fighter.ActiveTask = null;
        fighter.Health = 100;
        fighter.Skills["Engineering"] = 70;
        Assert.False(FireFrontRules.CanReachFront(room, fighter.PositionX, fighter.PositionY));

        foreach (var other in state.Crew.Skip(1))
        {
            other.CurrentRoomId = "quarters";
        }

        return (state, room, fighter);
    }
}
