using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #88: every run has 4-12 crew and 1-4 robots, with every
/// crew/robot combination equally likely.
/// </summary>
public sealed class RosterCompositionTests
{
    [Fact]
    public void ContractCoversEveryCombinationFromFourCrewAndOneRobotUpward()
    {
        Assert.Equal(4, RosterCompositionRules.MinCrew);
        Assert.Equal(12, RosterCompositionRules.MaxCrew);
        Assert.Equal(1, RosterCompositionRules.MinRobots);
        Assert.Equal(4, RosterCompositionRules.MaxRobots);

        var expected = (
            from crew in Enumerable.Range(4, 9)
            from robots in Enumerable.Range(1, 4)
            select new RosterComposition(crew, robots)).ToHashSet();

        Assert.Equal(36, RosterCompositionRules.CombinationCount);
        Assert.Equal(expected, RosterCompositionRules.AllCombinations.ToHashSet());
        Assert.Equal(36, RosterCompositionRules.AllCombinations.Count);
    }

    [Fact]
    public void SamplingIsDeterministicPerSeed()
    {
        foreach (var seed in new[] { 0, 1, -1, 42, int.MaxValue, int.MinValue, 1_234_567 })
        {
            Assert.Equal(RosterCompositionRules.Sample(seed), RosterCompositionRules.Sample(seed));
        }
    }

    [Fact]
    public void SamplingNeverLeavesTheContract()
    {
        for (var seed = -5_000; seed < 5_000; seed++)
        {
            var composition = RosterCompositionRules.Sample(seed);
            Assert.InRange(composition.CrewCount, 4, 12);
            Assert.InRange(composition.RobotCount, 1, 4);
        }
    }

    [Fact]
    public void EveryCombinationIsEquallyLikelyWithoutHiddenWeighting()
    {
        // 36 combinations over 360,000 consecutive seeds: each should appear
        // about 10,000 times. A +/-5% band is ~16 standard deviations for a
        // fair draw, so this only fails on real weighting, never on noise.
        const int Samples = 360_000;
        var counts = new Dictionary<RosterComposition, int>();
        for (var seed = 0; seed < Samples; seed++)
        {
            var composition = RosterCompositionRules.Sample(seed);
            counts[composition] = counts.GetValueOrDefault(composition) + 1;
        }

        Assert.Equal(RosterCompositionRules.CombinationCount, counts.Count);
        var expected = Samples / (double)RosterCompositionRules.CombinationCount;
        foreach (var (composition, count) in counts)
        {
            Assert.True(
                Math.Abs(count - expected) <= expected * 0.05,
                $"{composition} drawn {count} times; expected about {expected:0}.");
        }

        // Crew and robot counts are drawn together, so each crew size's robot
        // split is also flat (no crew-size/robot-count correlation).
        foreach (var crew in Enumerable.Range(4, 9))
        {
            var perCrew = counts.Where(pair => pair.Key.CrewCount == crew).Sum(pair => pair.Value);
            Assert.True(Math.Abs(perCrew - (Samples / 9d)) <= (Samples / 9d) * 0.03);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void StationSeedsTheRequestedNumberOfDistinctOperationalRobots(int robotCount)
    {
        var crew = SeededCrewRosterGenerator.Generate(77, 4);
        var state = FacilitySeeder.CreateDefault(
            crew,
            stationSeed: 1_234,
            stationConstraints: ScenarioCatalog.SecureContinuity.StationConstraints,
            robotCount: robotCount);

        Assert.Equal(robotCount, state.Robots.Count);
        Assert.Equal(robotCount, state.Robots.Select(robot => robot.Id).Distinct().Count());
        Assert.Equal(
            robotCount,
            state.Robots.Select(robot => (robot.PositionX, robot.PositionY)).Distinct().Count());
        Assert.All(state.Robots, robot =>
        {
            Assert.True(robot.IsOperational);
            Assert.Equal(RobotPolicy.Friendly, robot.Policy);
            Assert.True(state.Facility.Rooms.ContainsKey(robot.CurrentRoomId));
        });
        Assert.Contains(
            state.EventLog,
            line => line.Contains($"{robotCount} maintenance/security platform(s) online."));
        Assert.Contains(
            state.EventLog,
            line => line.Contains("4 crew members online."));
    }

    [Fact]
    public void OmittedRobotCountKeepsTheHistoricalSingleRobot()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1_234);

        Assert.Single(state.Robots);
    }

    [Fact]
    public void ScenarioRobotRequirementStaysAHardMinimum()
    {
        var constraints = new StationGenerationConstraints { RequiredRobotCount = 3 };

        var state = FacilitySeeder.CreateDefault(
            SeededCrewRosterGenerator.Generate(5, 6),
            stationSeed: 1_234,
            stationConstraints: constraints,
            robotCount: 1);

        Assert.Equal(3, state.Robots.Count);
    }

    [Fact]
    public void ZeroRobotRosterIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FacilitySeeder.CreateDefault(
            SeededCrewRosterGenerator.Generate(5, 4),
            robotCount: 0));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(12)]
    public void FitToSizeReturnsExactlyTheSampledCrewCount(int size)
    {
        var six = SeededCrewRosterGenerator.Generate(9, 6);

        var fitted = CrewRosterScalingSystem.FitToSize(six, "secure-continuity", size);

        Assert.Equal(size, fitted.Count);
        Assert.Equal(size, fitted.Select(npc => npc.Id).Distinct().Count());
        Assert.Equal(size, fitted.Select(npc => npc.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // Generated crew are kept first; only the shortfall is supplemented.
        Assert.Equal(
            six.Take(Math.Min(size, 6)).Select(npc => npc.Id),
            fitted.Take(Math.Min(size, 6)).Select(npc => npc.Id));
    }

    [Fact]
    public void SmallestRosterStillCoversCommandEngineeringSecurityAndMedicine()
    {
        var roles = SeededCrewRosterGenerator.Generate(3, RosterCompositionRules.MinCrew)
            .Select(npc => npc.Role)
            .ToHashSet();

        Assert.Equal(
            new HashSet<CrewRole> { CrewRole.Commander, CrewRole.Engineer, CrewRole.Security, CrewRole.Doctor },
            roles);
    }

    [Fact]
    public void BothRuntimesBuildFreshRunsFromTheSampledComposition()
    {
        var root = FindRepositoryRoot();
        var server = File.ReadAllText(Path.Combine(root, "src/Overseer.Web/Services/GameSession.cs"));
        var client = File.ReadAllText(Path.Combine(root, "src/Overseer.Web.Client/Services/GameSession.cs"));

        Assert.Contains("RosterCompositionRules.Sample(", client);
        Assert.Contains("SeededCrewRosterGenerator.Generate(rosterSeed, composition.CrewCount)", client);
        Assert.Contains("robotCount: composition.RobotCount", client);
        Assert.Contains("RosterCompositionRules.ConstraintsFor(scenario, crew.Count)", client);
        Assert.DoesNotContain("stationConstraints: scenario.StationConstraints", client);

        Assert.Contains("RosterCompositionRules.Sample(", server);
        Assert.Contains("CrewRosterScalingSystem.FitToSize(", server);
        Assert.DoesNotContain("EnsureTargetSize(", server);
        Assert.DoesNotContain(".StationConstraints,", server);
        Assert.Equal(
            CountOf(server, "robotCount: "),
            CountOf(server, "RosterCompositionRules.ConstraintsFor("));
        // Every server station creation passes the sampled robot count.
        Assert.Equal(
            CountOf(server, "FacilitySeeder.CreateDefault(\n") + CountOf(server, "FacilitySeeder.CreateDefault(\r\n"),
            CountOf(server, "robotCount: "));
    }

    [Fact]
    public void WithPlannedCrewCountCopiesEveryConstraintAndLeavesTheScenarioUntouched()
    {
        var original = ScenarioCatalog.ContainmentTransfer.StationConstraints!;
        var plannedBefore = original.PlannedCrewCount;

        var copy = original.WithPlannedCrewCount(9);

        Assert.Equal(9, copy.PlannedCrewCount);
        Assert.Equal(plannedBefore, original.PlannedCrewCount);
        Assert.NotSame(original, copy);

        // Guard against drift: every public property must round-trip. A new
        // property that WithPlannedCrewCount forgets to copy fails here.
        foreach (var property in typeof(StationGenerationConstraints).GetProperties())
        {
            if (property.Name == nameof(StationGenerationConstraints.PlannedCrewCount))
                continue;

            var before = property.GetValue(original);
            var after = property.GetValue(copy);
            if (before is System.Collections.IEnumerable sequence and not string)
            {
                Assert.NotSame(before, after);
                Assert.Equal(
                    sequence.Cast<object>().Select(item => item?.ToString()).Order(),
                    ((System.Collections.IEnumerable)after!).Cast<object>().Select(item => item?.ToString()).Order());
            }
            else
            {
                Assert.Equal(before, after);
            }
        }

        copy.RequiredRoomIds.Add("mutation-probe");
        Assert.DoesNotContain("mutation-probe", original.RequiredRoomIds);
    }

    [Fact]
    public void WithPlannedCrewCountCopiesPopulatedCollectionsOfEveryKind()
    {
        var original = new StationGenerationConstraints { RequiredRobotCount = 2, FullyAuthoredGeometry = true };
        original.ForbiddenRoomIds.Add("gym");
        original.RequiredAirlockRoomIds.Add("airlock");
        original.RequiredRobotRoomIds.Add("engineering");
        original.AllowedCropKinds.Add(Enum.GetValues<CropKind>()[0]);
        original.InitiallyAccessibleRoomIds.Add("control");
        original.EnvironmentOverrides["medical"] = new StationRoomEnvironmentOverride { OxygenPercent = 19 };

        var copy = original.WithPlannedCrewCount(5);

        Assert.Equal(2, copy.RequiredRobotCount);
        Assert.True(copy.FullyAuthoredGeometry);
        Assert.Contains("gym", copy.ForbiddenRoomIds);
        Assert.Equal(["airlock"], copy.RequiredAirlockRoomIds);
        Assert.Equal(["engineering"], copy.RequiredRobotRoomIds);
        Assert.Single(copy.AllowedCropKinds);
        Assert.Contains("control", copy.InitiallyAccessibleRoomIds);
        Assert.Equal(19, copy.EnvironmentOverrides["MEDICAL"].OxygenPercent);
    }

    [Fact]
    public void ConstraintsForSizesProvisioningToTheRosterAboard()
    {
        var scenario = ScenarioCatalog.SecureContinuity;

        var constraints = RosterCompositionRules.ConstraintsFor(scenario, 5);

        Assert.Equal(5, constraints.PlannedCrewCount);
        Assert.Equal(12, scenario.StationConstraints!.PlannedCrewCount);
        Assert.Equal(
            scenario.StationConstraints.RequiredRoomIds.Order(),
            constraints.RequiredRoomIds.Order());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void FreshStationReportsProvisioningForItsActualCrew(int crewCount)
    {
        var crew = SeededCrewRosterGenerator.Generate(31, crewCount);
        var state = FacilitySeeder.CreateDefault(
            crew,
            stationSeed: 4_321,
            stationConstraints: RosterCompositionRules.ConstraintsFor(ScenarioCatalog.SecureContinuity, crew.Count),
            robotCount: 1);

        var required = StationProvisionRules.RequiredHydroponicsCapacity(crewCount);
        Assert.Contains(
            state.EventLog,
            line => line.Contains($"HYDROPONICS CAPACITY") && line.Contains($"required for {crewCount} crew."));
        Assert.True(state.CropBeds.Sum(bed => bed.Capacity) + 0.001 >= required);
    }

    [Fact]
    public void ExplicitSeedsGenerateAtEverySampledRosterSize()
    {
        // Pages' "regenerate with seed" derives the roster composition from
        // the same seed and treats a generation failure as a hard error, so
        // every crew size must pack on ordinary seeds.
        foreach (var seed in Enumerable.Range(1, 12).Select(index => index * 7_919))
        {
            foreach (var crewCount in new[] { 4, 7, 12 })
            {
                var crew = SeededCrewRosterGenerator.Generate(seed, crewCount);
                var state = FacilitySeeder.CreateDefault(
                    crew,
                    stationSeed: seed,
                    stationConstraints: RosterCompositionRules.ConstraintsFor(ScenarioCatalog.SecureContinuity, crew.Count),
                    robotCount: RosterCompositionRules.MaxRobots);
                Assert.Equal(crewCount, state.Crew.Count);
            }
        }
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
