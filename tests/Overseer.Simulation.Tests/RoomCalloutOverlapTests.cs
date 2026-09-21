using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class RoomCalloutOverlapTests
{
    [Fact]
    public void RoomLabelsNeverOverlapAndFitTheirRoomAcrossSeeds()
    {
        foreach (var seed in Enumerable.Range(0, 32).Select(index => 930_000 + index))
        {
            var state = FacilitySeeder.CreateDefault(stationSeed: seed);
            var callouts = StationRoomCalloutSystem.Build(state.Facility);

            foreach (var callout in callouts)
            {
                var room = state.Facility.Rooms[callout.RoomId];
                Assert.True(
                    callout.LabelWidth <= (room.MapWidth * StationRoomCalloutSystem.AuthorityScale) + 1e-9,
                    $"Seed {seed}: {room.Id} label is wider than its room.");
            }

            for (var first = 0; first < callouts.Count; first++)
            {
                for (var second = first + 1; second < callouts.Count; second++)
                {
                    Assert.False(
                        StationRoomCalloutSystem.Overlaps(callouts[first], callouts[second]),
                        $"Seed {seed}: {callouts[first].RoomId} and {callouts[second].RoomId} labels overlap.");
                }
            }
        }
    }

    [Fact]
    public void ConsoleSizesLabelsFromTheLayout()
    {
        var root = FindRepositoryRoot();

        Assert.Contains(
            "width:{callout.LabelWidth:0.###}%;",
            File.ReadAllText(Path.Combine(root, "src/Overseer.Web.UI/Pages/Home.razor")));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Overseer.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Overseer.slnx from test output.");
    }
}
