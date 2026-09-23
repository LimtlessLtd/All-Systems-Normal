using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// P1 ladder convergence, remaining piece: <c>RuleBasedAiDecisionService.FindSaferRoom</c>
/// used to cost candidates via <see cref="NavigationSystem.ReachableRoomsForCrew"/> (a BFS
/// membership set, no path-length tiebreak) while <c>BrowserMindSystem.FindSaferRoom</c>
/// costed each candidate via a real <see cref="NavigationSystem.FindPathForCrew"/> route and
/// tie-broke on shortest path length. On an equal-risk tie between a geometrically closer but
/// topologically longer room and a geometrically farther but topologically shorter room, the
/// two ladders could disagree. Both now tie-break on genuine path length first.
/// </summary>
public sealed class SaferRoomConvergenceTests
{
    private static GameState BuildState()
    {
        var state = new GameState { Facility = new Facility() };

        state.Facility.Rooms["hazard"] = new Room
        {
            Id = "hazard",
            Name = "Hazard",
            Type = RoomType.ControlRoom,
            MapX = 50,
            MapY = 50,
            OxygenPercent = 15
        };

        // A corridor node, geometrically between hazard and roomX, that forces
        // a 2-hop route to a room that sits almost on top of hazard on the map.
        state.Facility.Rooms["mid"] = new Room
        {
            Id = "mid",
            Name = "Mid Corridor",
            Type = RoomType.Corridor,
            MapX = 50,
            MapY = 60
        };

        // Geometrically the closest safe room (Manhattan distance 1 from
        // hazard), but only reachable via a 2-hop path through "mid".
        state.Facility.Rooms["roomX"] = new Room
        {
            Id = "roomX",
            Name = "Room X",
            Type = RoomType.ControlRoom,
            MapX = 51,
            MapY = 50
        };

        // Geometrically far from hazard (Manhattan distance 450), but directly
        // connected by a door: a single-hop, genuinely shorter path.
        state.Facility.Rooms["roomY"] = new Room
        {
            Id = "roomY",
            Name = "Room Y",
            Type = RoomType.ControlRoom,
            MapX = 500,
            MapY = 50
        };

        state.Facility.Doors.Add(new Door
        {
            Id = "hazard-mid",
            RoomAId = "hazard",
            RoomBId = "mid",
            IsOpen = true,
            IsPowered = true
        });
        state.Facility.Doors.Add(new Door
        {
            Id = "mid-roomX",
            RoomAId = "mid",
            RoomBId = "roomX",
            IsOpen = true,
            IsPowered = true
        });
        state.Facility.Doors.Add(new Door
        {
            Id = "hazard-roomY",
            RoomAId = "hazard",
            RoomBId = "roomY",
            IsOpen = true,
            IsPowered = true
        });

        return state;
    }

    [Fact]
    public async Task FallbackMind_PrefersTheGenuinelyShorterPathOverGeometricProximityOnATie()
    {
        var state = BuildState();
        var npc = new Npc
        {
            Name = "Observer",
            Role = CrewRole.Technician,
            Personality = new Personality(50, 50, 50, 50),
            CurrentRoomId = "hazard"
        };
        state.Crew.Add(npc);

        var intent = await new RuleBasedAiDecisionService().DecideAsync(npc, state);

        Assert.Equal(ActionKind.Move, intent.Action);
        Assert.Equal("roomY", intent.TargetId);
    }

    [Fact]
    public void BothLaddersAgreeOnTheSameSaferRoomWhenGeometryAndTopologyDisagree()
    {
        var browserState = BuildState();
        var browserNpc = new Npc
        {
            Name = "Observer",
            Role = CrewRole.Technician,
            Personality = new Personality(50, 50, 50, 50),
            CurrentRoomId = "hazard"
        };
        browserState.Crew.Add(browserNpc);
        browserState.Elapsed = TimeSpan.FromMinutes(6);

        new BrowserMindSystem().Tick(browserState);

        Assert.NotNull(browserNpc.Intent);
        Assert.Equal(ActionKind.Move, browserNpc.Intent!.Action);
        Assert.Equal("roomY", browserNpc.Intent!.TargetId);
    }
}
