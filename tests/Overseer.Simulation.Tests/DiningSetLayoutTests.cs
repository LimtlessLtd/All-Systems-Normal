using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// The layout pass keeps a table and the seats authored against it together:
/// a generated kitchen's chairs stand at the mess table, the desk chair at
/// the writing desk and the lounge sofas at the low table, instead of being
/// scattered across the floor grid one by one.
/// </summary>
public sealed class DiningSetLayoutTests
{
    private static readonly (string Room, string Table, string[] Seats)[] Sets =
    [
        ("kitchen", "Mess Table", ["Chair North", "Chair South", "Chair West", "Chair East"]),
        ("quarters", "Writing Desk", ["Desk Chair"]),
        ("lounge", "Low Table", ["Sofa West", "Sofa East"])
    ];

    [Fact]
    public void GeneratedStations_KeepEverySeatAtItsTable()
    {
        var misses = new List<string>();
        var seated = 0;
        var total = 0;
        var stations = 0;

        foreach (var state in Stations(60))
        {
            stations++;
            foreach (var (roomId, tableLabel, seatLabels) in Sets)
            {
                var room = state.Facility.Rooms[roomId];
                var table = room.Fixtures.Single(fixture => fixture.Label == tableLabel);

                foreach (var seatLabel in seatLabels)
                {
                    // Every authored seat still exists: none is dropped.
                    var seat = room.Fixtures.Single(fixture => fixture.Label == seatLabel);
                    total++;
                    Assert.True(
                        EdgeGap(seat, table) > 0,
                        $"seed {state.StationGeneration?.Seed}: {seatLabel} is inside {tableLabel}");
                    if (EdgeGap(seat, table) <= 4)
                        seated++;
                    else
                        misses.Add($"seed {state.StationGeneration?.Seed} {roomId}/{seatLabel} gap {EdgeGap(seat, table):0.#}");
                }
            }
        }

        Assert.Equal(60, stations);

        // Before the table and its seats were placed as one set, almost no
        // generated kitchen had a chair at its mess table. A very tight room
        // may still push one seat to ordinary placement, never a whole set.
        Assert.True(
            seated >= total * 0.97,
            $"{seated}/{total} seats at their table; misses: {string.Join("; ", misses)}");
        Assert.All(
            misses.GroupBy(miss => miss.Split('/')[0]),
            station => Assert.Single(station));
    }

    [Fact]
    public void ASeatMovedToAnotherSideOfItsTable_FacesTheTable()
    {
        foreach (var state in Stations(60))
        {
            var room = state.Facility.Rooms["kitchen"];
            var table = room.Fixtures.Single(fixture => fixture.Label == "Mess Table");

            foreach (var chair in room.Fixtures.Where(fixture =>
                         fixture.Type == FixtureType.Chair && EdgeGap(fixture, table) <= 4))
            {
                // The authored mess-table convention: 0 faces north, 180 south.
                var expected = chair.Y < table.Y - (table.Height / 2) ? 180
                    : chair.Y > table.Y + (table.Height / 2) ? 0
                    : chair.X < table.X ? 90
                    : 270;
                Assert.Equal(expected, chair.FacingDegrees);
            }
        }
    }

    private static double EdgeGap(RoomFixture first, RoomFixture second)
    {
        var dx = Math.Max(0, Math.Abs(first.X - second.X) - ((first.Width + second.Width) / 2));
        var dy = Math.Max(0, Math.Abs(first.Y - second.Y) - ((first.Height + second.Height) / 2));
        return Math.Max(dx, dy);
    }

    private static IEnumerable<GameState> Stations(int count)
    {
        var found = 0;
        for (var seed = 100; seed < 1000 && found < count; seed++)
        {
            GameState state;
            try
            {
                state = FacilitySeeder.CreateDefault(upkeepSeed: seed, stationSeed: seed);
            }
            catch (StationGenerationException)
            {
                // Procedural packing intentionally rejects some exact seeds.
                continue;
            }

            found++;
            yield return state;
        }
    }
}
