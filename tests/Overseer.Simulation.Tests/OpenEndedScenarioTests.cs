using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Most assignments have no clock. Overseer is patient: the shift ends when the
/// sponsor's work is done, however long that takes.
/// </summary>
public sealed class OpenEndedScenarioTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static void Advance(
        GameState state,
        CorporateDirectiveSystem directives,
        ScenarioProgressSystem progress,
        int minutes)
    {
        for (var i = 0; i < minutes && state.ScenarioStatus == ScenarioStatus.Running; i++)
        {
            state.Elapsed += Minute;
            directives.Tick(state, Minute);
            progress.Tick(state, Minute);
        }
    }

    [Fact]
    public void OnlyTheOpeningMissionIsTimed()
    {
        var timed = ScenarioCatalog.Campaign
            .Where(scenario => scenario.Directives!.Any(d => d.Deadline is not null))
            .ToList();

        var openEnded = ScenarioCatalog.Campaign
            .Where(scenario => scenario.Directives!.All(d => d.Deadline is null))
            .ToList();

        Assert.Single(timed);
        Assert.Equal(ScenarioCatalog.SecureContinuity.Id, timed[0].Id);
        Assert.True(openEnded.Count >= 3, "Most of the campaign should be open-ended.");
    }

    [Fact]
    public void AnOpenEndedMissionUsesADirectiveObjectiveRatherThanACountdown()
    {
        foreach (var scenario in ScenarioCatalog.Campaign
                     .Where(s => s.Directives!.All(d => d.Deadline is null)))
        {
            Assert.Contains(
                scenario.Objectives,
                objective => objective.Kind == ScenarioObjectiveKind.DirectivesSatisfied);

            Assert.DoesNotContain(
                scenario.Objectives,
                objective => objective.Kind == ScenarioObjectiveKind.SurviveMinutes);
        }
    }

    [Fact]
    public void AnOpenEndedMissionKeepsRunningIndefinitelyWhileTheWorkIsUnfinished()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, ScenarioCatalog.InterpersonalResilience);

        // Keep the whole crew together so the isolation directive can never
        // gather its sample. The work genuinely cannot finish.
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = "corridor";
        }

        // Far beyond any old deadline. Nothing should conclude on time alone.
        Advance(state, new CorporateDirectiveSystem(), new ScenarioProgressSystem(), 2000);

        Assert.Equal(ScenarioStatus.Running, state.ScenarioStatus);
        Assert.True(state.Elapsed > TimeSpan.FromHours(24));
    }

    [Fact]
    public void AnOpenEndedMissionEndsWhenTheWorkIsDoneHoweverLongThatTook()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, ScenarioCatalog.InterpersonalResilience);

        var isolation = state.Directives.Single(d => d.Kind == DirectiveKind.IsolationTolerance);
        var subject = state.Crew.Single(npc => npc.Name == isolation.TargetId);

        // Put the subject somewhere nobody else is, and leave them there.
        subject.CurrentRoomId = "storage";
        foreach (var npc in state.Crew.Where(n => n.Id != subject.Id))
        {
            npc.CurrentRoomId = "corridor";
        }

        var directives = new CorporateDirectiveSystem();
        var progress = new ScenarioProgressSystem();

        Advance(state, directives, progress, isolation.RequiredMinutes + 10);

        Assert.Equal(ScenarioStatus.Won, state.ScenarioStatus);
        Assert.Equal(
            DirectiveStatus.Completed,
            CorporateDirectiveSystem.Progress(state, isolation).Status);
    }

    [Fact]
    public void StandingConstraintsAreSignedOffWithTheWorkRatherThanDeadlocking()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, ScenarioCatalog.InterpersonalResilience);

        var continuity = state.Directives.Single(d => d.Kind == DirectiveKind.MaintainContinuity);
        var deniability = state.Directives.Single(d => d.Kind == DirectiveKind.MaintainDeniability);

        Assert.Null(continuity.Deadline);
        Assert.Null(deniability.Deadline);

        var isolation = state.Directives.Single(d => d.Kind == DirectiveKind.IsolationTolerance);
        var subject = state.Crew.Single(npc => npc.Name == isolation.TargetId);

        subject.CurrentRoomId = "storage";
        foreach (var npc in state.Crew.Where(n => n.Id != subject.Id))
        {
            npc.CurrentRoomId = "corridor";
        }

        Advance(
            state,
            new CorporateDirectiveSystem(),
            new ScenarioProgressSystem(),
            isolation.RequiredMinutes + 10);

        // Two standing constraints that waited on each other would never resolve.
        Assert.Equal(
            DirectiveStatus.Completed,
            CorporateDirectiveSystem.Progress(state, continuity).Status);
        Assert.Equal(
            DirectiveStatus.Completed,
            CorporateDirectiveSystem.Progress(state, deniability).Status);
    }

    [Fact]
    public void AnOpenEndedDeniabilityIsJudgedOnTheReadingWhenTheWorkConcludes()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, ScenarioCatalog.InterpersonalResilience);

        var isolation = state.Directives.Single(d => d.Kind == DirectiveKind.IsolationTolerance);
        var subject = state.Crew.Single(npc => npc.Name == isolation.TargetId);

        subject.CurrentRoomId = "storage";
        foreach (var npc in state.Crew.Where(n => n.Id != subject.Id))
        {
            npc.CurrentRoomId = "corridor";
            npc.OverseerSuspicion = 95;
        }

        Advance(
            state,
            new CorporateDirectiveSystem(),
            new ScenarioProgressSystem(),
            isolation.RequiredMinutes + 10);

        Assert.Equal(ScenarioStatus.Failed, state.ScenarioStatus);
        Assert.Equal(
            DirectiveStatus.Failed,
            CorporateDirectiveSystem.Progress(
                state,
                state.Directives.Single(d => d.Kind == DirectiveKind.MaintainDeniability)).Status);
    }

    [Fact]
    public void SupplementaryDirectivesAreNotWrittenOffBeforeTheAssignmentConcludes()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, ScenarioCatalog.ResourceDependency);

        var supplementary = state.Directives.Where(d => !d.IsMandatory).ToList();
        Assert.NotEmpty(supplementary);

        Advance(state, new CorporateDirectiveSystem(), new ScenarioProgressSystem(), 30);

        Assert.All(
            supplementary,
            directive => Assert.Equal(
                DirectiveStatus.Active,
                CorporateDirectiveSystem.Progress(state, directive).Status));
    }
}
