using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #100: the map's flames are a picture of the deterministic fire,
/// placed only where the front burns and multiplying as it spreads.
/// </summary>
public sealed class FlameSpriteTests
{
    [Fact]
    public void NoFireDrawsNoFlames()
    {
        var room = Engineering();

        Assert.Empty(FireFrontRules.FlameSprites(room));
    }

    [Fact]
    public void ANewFireIsASmallPatchAtItsOrigin()
    {
        var room = Engineering();
        FireFrontRules.Ignite(room, 20, 30, intensity: 5);

        var flames = FireFrontRules.FlameSprites(room);

        Assert.InRange(flames.Count, 1, 4);
        Assert.Equal((20, 30), (flames[0].X, flames[0].Y));
    }

    [Fact]
    public void EveryFlameSitsInsideTheBurningFrontAndClearOfTheWalls()
    {
        var room = Engineering();

        foreach (var intensity in new[] { 5d, 20, 40, 60, 80, 100 })
        {
            FireFrontRules.Ignite(room, 85, 70, intensity);

            Assert.All(FireFrontRules.FlameSprites(room), flame =>
            {
                Assert.True(FireFrontRules.IsInsideFront(room, flame.X, flame.Y));
                Assert.InRange(flame.X, 6, 94);
                Assert.InRange(flame.Y, 6, 94);
            });
        }
    }

    [Fact]
    public void FlamesMultiplyAsTheFireGrowsAndNeverJumpAround()
    {
        var room = Engineering();
        IReadOnlyList<FireFrontRules.FlameSprite> previous = [];

        for (var intensity = 1d; intensity <= 100; intensity += 3)
        {
            FireFrontRules.Ignite(room, 40, 45, intensity);
            var flames = FireFrontRules.FlameSprites(room);

            Assert.True(flames.Count >= previous.Count, $"Flames shrank at intensity {intensity}.");
            if (flames.Count < FireFrontRules.MaxFlameSprites)
            {
                // Below the cap the patch only gains flames: every flame
                // already burning stays exactly where it was.
                Assert.Superset(
                    previous.Select(flame => (flame.X, flame.Y)).ToHashSet(),
                    flames.Select(flame => (flame.X, flame.Y)).ToHashSet());
            }

            previous = flames;
        }

        // An inferno covers the room with many flames, where ignition drew one.
        Assert.True(previous.Count >= 9, $"An inferno drew only {previous.Count} flames.");
    }

    [Fact]
    public void HotterFiresDrawLargerFlames()
    {
        var room = Engineering();
        FireFrontRules.Ignite(room, 50, 50, intensity: 10);
        var cool = FireFrontRules.FlameSprites(room)[0].Scale;
        FireFrontRules.Ignite(room, 50, 50, intensity: 90);
        var hot = FireFrontRules.FlameSprites(room)[0].Scale;

        Assert.True(hot > cool);
    }

    [Fact]
    public void AFireOnTheWallStillShowsAFlameAtThatWall()
    {
        var room = Engineering();
        FireFrontRules.Ignite(room, 0, 50, intensity: 5);

        var flames = FireFrontRules.FlameSprites(room);

        Assert.NotEmpty(flames);
        Assert.All(flames, flame => Assert.True(flame.X <= 20));
    }

    [Fact]
    public void AFireWithNoRecordedOriginSpreadsFlamesAcrossTheRoom()
    {
        var room = Engineering();
        room.FireIntensity = 30;

        var flames = FireFrontRules.FlameSprites(room);

        Assert.True(flames.Count >= 9);
        Assert.Contains(flames, flame => flame.X < 35);
        Assert.Contains(flames, flame => flame.X > 65);
    }

    [Fact]
    public void FlamesAreDeterministic()
    {
        var room = Engineering();
        FireFrontRules.Ignite(room, 33, 61, intensity: 57);

        Assert.Equal(FireFrontRules.FlameSprites(room), FireFrontRules.FlameSprites(room));
    }

    private static Room Engineering() =>
        FacilitySeeder.CreateDefault(stationSeed: 1337).Facility.Rooms["engineering"];
}
