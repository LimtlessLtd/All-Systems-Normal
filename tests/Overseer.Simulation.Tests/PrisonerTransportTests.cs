using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// V0.13: containment transfer as a complete standalone assignment. Covers
/// escape opportunity/pressure, deterministic recapture (including lethal
/// outcomes) and the containment-integrity win/lose gate.
/// </summary>
public sealed class PrisonerTransportTests
{
    [Fact]
    public void ContainmentTransferIsAStandaloneAssignmentNotPartOfTheOrderedCampaign()
    {
        Assert.Contains(ScenarioCatalog.ContainmentTransfer, ScenarioCatalog.StandaloneAssignments);
        Assert.DoesNotContain(ScenarioCatalog.ContainmentTransfer, ScenarioCatalog.Campaign);
        Assert.Equal(5, ScenarioCatalog.Campaign.Count);
        Assert.Same(
            ScenarioCatalog.ContainmentTransfer,
            ScenarioCatalog.Find(ScenarioCatalog.ContainmentTransfer.Id));
    }

    [Fact]
    public void ApplyingContainmentTransferAddsAMandatoryContainmentIntegrityDirective()
    {
        var state = CreateContainmentState(481_000);

        var directive = state.Directives.Single(d => d.Kind == DirectiveKind.ContainmentIntegrity);
        Assert.True(directive.IsMandatory);
        Assert.False(string.IsNullOrWhiteSpace(directive.TruePurpose));
    }

    [Fact]
    public void CrewAliveObjectiveTargetsOnlyTheOperatingCrewNotThePrisoners()
    {
        var state = CreateContainmentState(481_010);

        var progress = state.ObjectiveProgress["crew-alive"];

        // 12 baseline crew + 4 prisoners were generated; the objective must not
        // count prisoners toward either the target or the current reading.
        Assert.Equal(12, progress.Target);
        Assert.Equal(16, state.Crew.Count(npc => npc.IsAlive && npc.IsPresent));
    }

    [Fact]
    public void AnUnsecuredContainmentHatchLetsAHighPressurePrisonerBreachWithinTheWindow()
    {
        var state = CreateContainmentState(481_020);
        var prisoner = state.Crew.Single(npc => npc.PrisonerDangerLevel == PrisonerDangerLevel.Extreme);
        prisoner.Stress = 92;

        UnsecureContainmentDoor(state);

        var system = new PrisonerContainmentSystem();
        var escaped = false;

        for (var minute = 1; minute <= 400 && !escaped; minute++)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            system.Tick(state);
            escaped = prisoner.HasEscapedContainment;
        }

        Assert.True(escaped, "An unsecured hatch and sustained escape pressure should eventually produce a breach.");
        Assert.NotEqual(
            PrisonerContainmentSystem.ContainmentRoomId,
            prisoner.CurrentRoomId,
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            state.EventLog,
            entry => entry.Contains("CONTAINMENT BREACH", StringComparison.OrdinalIgnoreCase)
                && entry.Contains(prisoner.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ASecuredContainmentHatchPreventsAnyBreachForTheFullObservationWindow()
    {
        var state = CreateContainmentState(481_030);
        var prisoner = state.Crew.Single(npc => npc.PrisonerDangerLevel == PrisonerDangerLevel.Extreme);
        prisoner.Stress = 92;

        var door = ContainmentDoor(state);
        door.IsLocked = true;
        door.IsOpen = false;

        var system = new PrisonerContainmentSystem();

        for (var minute = 1; minute <= 480; minute++)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            system.Tick(state);
        }

        Assert.False(prisoner.HasEscapedContainment);
        Assert.Equal(
            PrisonerContainmentSystem.ContainmentRoomId,
            prisoner.CurrentRoomId,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWellMatchedGuardEventuallyRecapturesAnEscapedLowRiskPrisoner()
    {
        var state = CreateContainmentState(481_040);
        var prisoner = state.Crew.Single(npc => npc.IsPrisoner && npc.PrisonerDangerLevel == PrisonerDangerLevel.Low);
        var guard = state.Crew.First(npc => !npc.IsPrisoner);

        guard.Skills["Athletics"] = 100;
        guard.Skills["Security"] = 100;
        guard.Skills["Operations"] = 100;

        var atLargeRoomId = AdjacentToContainmentRoomId(state);
        prisoner.CurrentRoomId = atLargeRoomId;
        prisoner.HasEscapedContainment = true;
        guard.CurrentRoomId = atLargeRoomId;

        var system = new PrisonerContainmentSystem();
        var recaptured = false;

        for (var minute = 1; minute <= 40 && !recaptured; minute++)
        {
            if (guard.IsAlive && prisoner.HasEscapedContainment
                && guard.CurrentAction.Kind != ActionKind.RecapturePrisoner)
            {
                guard.CurrentAction = new NpcAction(ActionKind.RecapturePrisoner, prisoner.Name, "test");
                guard.RoutineUntil = TimeSpan.Zero;
            }

            state.Elapsed = TimeSpan.FromMinutes(minute);
            system.Tick(state);
            recaptured = !prisoner.HasEscapedContainment;
        }

        Assert.True(recaptured, "A far stronger guard should eventually restrain a low-risk escapee.");
        Assert.Equal(
            PrisonerContainmentSystem.ContainmentRoomId,
            prisoner.CurrentRoomId,
            StringComparer.OrdinalIgnoreCase);
        Assert.True(guard.IsAlive);
    }

    [Fact]
    public void AnOutmatchedGuardCanBeKilledResistingAnExtremeEscapee()
    {
        var state = CreateContainmentState(481_050);
        var prisoner = state.Crew.Single(npc => npc.PrisonerDangerLevel == PrisonerDangerLevel.Extreme);
        var guard = state.Crew.First(npc => !npc.IsPrisoner);

        // Strip the guard's force-relevant skills so recapture success chance
        // clamps to its floor, and start them critically wounded so the first
        // failed struggle is guaranteed to be lethal.
        guard.Skills.Clear();
        guard.Health = 4;

        var atLargeRoomId = AdjacentToContainmentRoomId(state);
        prisoner.CurrentRoomId = atLargeRoomId;
        prisoner.HasEscapedContainment = true;
        guard.CurrentRoomId = atLargeRoomId;

        var system = new PrisonerContainmentSystem();

        for (var minute = 1; minute <= 60 && guard.IsAlive && prisoner.HasEscapedContainment; minute++)
        {
            if (guard.CurrentAction.Kind != ActionKind.RecapturePrisoner)
            {
                guard.CurrentAction = new NpcAction(ActionKind.RecapturePrisoner, prisoner.Name, "test");
                guard.RoutineUntil = TimeSpan.Zero;
            }

            state.Elapsed = TimeSpan.FromMinutes(minute);
            system.Tick(state);
        }

        Assert.False(guard.IsAlive, "A badly wounded, unskilled guard should be able to die restraining an extreme-risk escapee.");
        Assert.Contains(prisoner.Name, guard.CauseOfDeath);
        Assert.Contains(
            state.EventLog,
            entry => entry.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase)
                && entry.Contains(guard.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContainmentIntegrityFailsImmediatelyWhenAPrisonerDies()
    {
        var state = CreateContainmentState(481_060);
        var prisoner = state.Crew.First(npc => npc.IsPrisoner);
        prisoner.Health = 0;

        new CorporateDirectiveSystem().Tick(state, TimeSpan.FromMinutes(1));

        var directive = state.Directives.Single(d => d.Kind == DirectiveKind.ContainmentIntegrity);
        Assert.Equal(
            DirectiveStatus.Failed,
            CorporateDirectiveSystem.Progress(state, directive).Status);
        Assert.Equal(ScenarioStatus.Failed, state.ScenarioStatus);
    }

    [Fact]
    public void ContainmentIntegrityCompletesWhenEveryPrisonerStaysSecureThroughTheWindow()
    {
        var state = CreateContainmentState(481_070);
        var directives = new CorporateDirectiveSystem();

        for (var minute = 1; minute <= 481; minute++)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            directives.Tick(state, TimeSpan.FromMinutes(1));
        }

        var directive = state.Directives.Single(d => d.Kind == DirectiveKind.ContainmentIntegrity);
        Assert.Equal(
            DirectiveStatus.Completed,
            CorporateDirectiveSystem.Progress(state, directive).Status);
    }

    [Fact]
    public void StationAlertsFlagAnEscapedPrisonerAndClearOnceRecaptured()
    {
        var state = CreateContainmentState(481_080);
        var prisoner = state.Crew.First(npc => npc.IsPrisoner);
        prisoner.CurrentRoomId = AdjacentToContainmentRoomId(state);
        prisoner.HasEscapedContainment = true;

        var alertsWhileEscaped = StationAlertSystem.Build(state);
        Assert.Contains(
            alertsWhileEscaped,
            alert => alert.Severity == StationAlertSeverity.Critical
                && alert.Message.Contains(prisoner.Name, StringComparison.OrdinalIgnoreCase)
                && alert.Message.Contains("breached Containment", StringComparison.OrdinalIgnoreCase));

        prisoner.HasEscapedContainment = false;
        prisoner.CurrentRoomId = PrisonerContainmentSystem.ContainmentRoomId;

        var alertsAfterRecapture = StationAlertSystem.Build(state);
        Assert.DoesNotContain(
            alertsAfterRecapture,
            alert => alert.Message.Contains("breached Containment", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Station generation may deterministically reject an individual seed
    /// (spatial packing), exactly as the existing containment-geometry test
    /// tolerates; scan forward from a fixed seed for the first one that
    /// produces a real secure compartment.
    /// </summary>
    private static GameState CreateContainmentState(int baseSeed)
    {
        for (var seed = baseSeed; seed < baseSeed + 300; seed++)
        {
            try
            {
                var baseCrew = SeededCrewRosterGenerator.Generate(seed);
                var crew = PrisonerRosterSystem.Compose(baseCrew, ScenarioCatalog.ContainmentTransfer);
                var state = FacilitySeeder.CreateDefault(
                    crew,
                    stationSeed: seed,
                    stationConstraints: ScenarioCatalog.ContainmentTransfer.StationConstraints);

                ScenarioCatalog.Apply(state, ScenarioCatalog.ContainmentTransfer);
                return state;
            }
            catch (StationGenerationException)
            {
                // Spatial packing is allowed to reject individual seeds.
            }
        }

        throw new InvalidOperationException(
            $"No containment-transfer station generated in the range starting at {baseSeed}.");
    }

    private static string AdjacentToContainmentRoomId(GameState state)
    {
        var door = ContainmentDoor(state);
        return door.RoomAId.Equals(
            PrisonerContainmentSystem.ContainmentRoomId,
            StringComparison.OrdinalIgnoreCase)
            ? door.RoomBId
            : door.RoomAId;
    }

    private static Door ContainmentDoor(GameState state) =>
        state.Facility.Doors
            .Where(door =>
                door.RoomAId.Equals(PrisonerContainmentSystem.ContainmentRoomId, StringComparison.OrdinalIgnoreCase)
                || door.RoomBId.Equals(PrisonerContainmentSystem.ContainmentRoomId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(door => door.Id, StringComparer.Ordinal)
            .First();

    private static void UnsecureContainmentDoor(GameState state)
    {
        foreach (var door in state.Facility.Doors.Where(door =>
                     door.RoomAId.Equals(PrisonerContainmentSystem.ContainmentRoomId, StringComparison.OrdinalIgnoreCase)
                     || door.RoomBId.Equals(PrisonerContainmentSystem.ContainmentRoomId, StringComparison.OrdinalIgnoreCase)))
        {
            door.IsWelded = false;
            door.IsBarricaded = false;
            door.IsLocked = false;
            door.IsPowered = true;
            door.IsOpen = true;
        }
    }
}
