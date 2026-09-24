using Overseer.Domain;

namespace Overseer.Simulation.Tests;

public sealed class SensorReportedReadingsTests
{
    [Fact]
    public void RoomSensors_DefaultToLivePhysicalTruth()
    {
        var room = new Room
        {
            Id = "lab",
            Name = "Lab",
            Type = RoomType.ControlRoom,
            TemperatureC = 18.5,
            OxygenPercent = 19.2
        };

        Assert.Equal(18.5, room.ReportedTemperatureC);
        Assert.Equal(19.2, room.ReportedOxygenPercent);

        room.TemperatureC = 31.25;
        room.OxygenPercent = 16.8;

        Assert.Equal(31.25, room.ReportedTemperatureC);
        Assert.Equal(16.8, room.ReportedOxygenPercent);
    }

    [Fact]
    public void SensorOverrides_CanLieWithoutChangingPhysicalAtmosphere()
    {
        var room = new Room
        {
            Id = "lab",
            Name = "Lab",
            Type = RoomType.ControlRoom,
            TemperatureC = 42,
            OxygenPercent = 14.5,
            TemperatureSensorReadingOverrideC = 21,
            OxygenSensorReadingOverridePercent = 20.9
        };

        Assert.Equal(42, room.TemperatureC);
        Assert.Equal(14.5, room.OxygenPercent);
        Assert.Equal(21, room.ReportedTemperatureC);
        Assert.Equal(20.9, room.ReportedOxygenPercent);

        room.TemperatureSensorReadingOverrideC = null;
        room.OxygenSensorReadingOverridePercent = null;

        Assert.Equal(42, room.ReportedTemperatureC);
        Assert.Equal(14.5, room.ReportedOxygenPercent);
    }

    [Fact]
    public void StationConsole_UsesReportedOxygenAndTemperatureForSensorFacingPresentation()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src/Overseer.Web.UI/Pages/Home.razor"));

        Assert.Contains("calloutRoom.ReportedTemperatureC", source);
        Assert.Contains("calloutRoom.ReportedOxygenPercent", source);
        Assert.Contains("selectedRoom.ReportedTemperatureC", source);
        Assert.Contains("selectedRoom.ReportedOxygenPercent", source);
        Assert.Contains("room.ReportedTemperatureC is < 5 or > 38", source);
        Assert.Contains("room.ReportedOxygenPercent < 17", source);
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

        throw new DirectoryNotFoundException(
            "Could not locate Overseer.slnx from test output.");
    }
}
