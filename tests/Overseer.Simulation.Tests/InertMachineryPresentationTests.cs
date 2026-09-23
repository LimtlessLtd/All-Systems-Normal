namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #82: disabled, failed or unpowered machinery renders inert, and
/// restoring it resumes the original animation.
/// </summary>
public sealed class InertMachineryPresentationTests
{
    [Fact]
    public void MachineFixtures_DeriveInertStateFromAuthoritativeDeviceAndRoomPower()
    {
        var razor = ReadUi("Home.razor");

        Assert.Contains("MachineFixtureClass(room, fixture)", razor);
        Assert.Contains("if (!device.IsOperational)", razor);
        Assert.Contains("device.Kind is StationSystemKind.Reactor or StationSystemKind.PowerGenerator", razor);
        Assert.Contains("return room.IsPowered;", razor);
    }

    [Fact]
    public void InertFixtures_StopEveryAnimationLayerAndDimTheirGlow()
    {
        var css = ReadUi("Home.razor.css");

        Assert.Contains(".station-authority-layer .room-node .fixture.fixture-inert::after", css);
        Assert.Contains("animation: none !important;", css);
        Assert.Contains("filter: grayscale(.85) brightness(.55) !important;", css);
    }

    private static string ReadUi(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Overseer.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "src",
            "Overseer.Web.UI",
            "Pages",
            file));
    }
}
