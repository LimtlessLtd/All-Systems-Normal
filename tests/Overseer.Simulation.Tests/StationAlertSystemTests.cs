using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class StationAlertSystemTests
{
    [Fact]
    public void EachAlertSaysWhatIsWrongAndPointsAtIt()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var before = StationAlertSystem.Build(state).Count;
        var kitchen = state.Facility.Rooms["kitchen"];
        kitchen.IsPowered = false;

        var alerts = StationAlertSystem.Build(state);

        Assert.Equal(before + 1, alerts.Count);
        var alert = Assert.Single(alerts, candidate => candidate.Message.StartsWith($"{kitchen.Name}: no power", StringComparison.Ordinal));
        Assert.Equal(new StationSelection(StationSelectionKind.Room, "kitchen"), alert.Target);
    }

    [Fact]
    public void CriticalAlertsComeFirst()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        state.Facility.Rooms["kitchen"].IsPowered = false;
        var victim = state.Crew[0];
        victim.Health = 0;
        victim.CauseOfDeath = "Died from oxygen deprivation.";

        var alerts = StationAlertSystem.Build(state);

        Assert.Equal(StationAlertSeverity.Critical, alerts[0].Severity);
        Assert.Contains(victim.Name, alerts[0].Message, StringComparison.Ordinal);
        Assert.Equal(new StationSelection(StationSelectionKind.Crew, victim.Id.ToString()), alerts[0].Target);
    }

    [Fact]
    public void FreshStation_ReportsRealGridReadingsBeforeTheFirstTurn()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);

        Assert.True(state.Power.SupplyKilowatts > 0);
        Assert.True(state.Power.DemandKilowatts > 0);
    }

    [Fact]
    public void Console_CountsAlertsFromTheSameList()
    {
        var root = FindRepositoryRoot();

        var session = File.ReadAllText(Path.Combine(root, "src/Overseer.Simulation/StationSession.cs"));
        Assert.Contains("public int AlertCount => Alerts.Count;", session);

        var home = File.ReadAllText(Path.Combine(root, "src/Overseer.Web.UI/Pages/Home.razor"));
        Assert.Contains("ToggleStationOverlay(\"alerts\")", home);
        Assert.Contains("@Session.AlertCount ALERT", home);
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
