using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public class CorporateDirectiveSystemTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static GameState CreateState(params CorporateDirective[] directives)
    {
        var state = FacilitySeeder.CreateDefault();
        state.Directives.Clear();
        state.DirectiveProgress.Clear();
        state.Directives.AddRange(directives);
        state.ScenarioStatus = ScenarioStatus.Running;

        // These exercise the corporate layer on its own. The station's own
        // objectives are graded by ScenarioProgressSystem and gate the outcome
        // too, so drop them to keep the subject of the test isolated.
        state.Scenario = state.Scenario is { } scenario
            ? scenario with { Objectives = [] }
            : null;
        state.ObjectiveProgress.Clear();

        return state;
    }

    private static void Advance(GameState state, CorporateDirectiveSystem system, int minutes)
    {
        for (var i = 0; i < minutes; i++)
        {
            state.Elapsed += Minute;
            system.Tick(state, Minute);
        }
    }

    [Fact]
    public void AMandatoryDirectiveIsSatisfiedWhenItsWindowClosesUneventfully()
    {
        var state = CreateState(new CorporateDirective
        {
            Id = "continuity",
            ExperimentCode = "TEST/A",
            Kind = DirectiveKind.MaintainContinuity,
            Title = "PRESERVE OPERATIONAL CONTINUITY",
            PublicJustification = "Stay online.",
            IsMandatory = true,
            Deadline = TimeSpan.FromMinutes(5)
        });

        var system = new CorporateDirectiveSystem();
        Advance(state, system, 5);

        // Declaring victory belongs to ScenarioProgressSystem, which owns the
        // station objectives and the telemetry score. This layer only grades.
        Assert.NotEqual(ScenarioStatus.Failed, state.ScenarioStatus);
        Assert.Equal(
            DirectiveStatus.Completed,
            CorporateDirectiveSystem.Progress(state, state.Directives[0]).Status);
    }

    [Fact]
    public void ScenarioIsNotResolvedWhileAMandatoryDirectiveIsStillActive()
    {
        var state = CreateState(new CorporateDirective
        {
            Id = "continuity",
            ExperimentCode = "TEST/A",
            Kind = DirectiveKind.MaintainContinuity,
            Title = "PRESERVE OPERATIONAL CONTINUITY",
            PublicJustification = "Stay online.",
            IsMandatory = true,
            Deadline = TimeSpan.FromMinutes(60)
        });

        var system = new CorporateDirectiveSystem();
        Advance(state, system, 10);

        Assert.Equal(ScenarioStatus.Running, state.ScenarioStatus);
    }

    [Fact]
    public void OverseerIsolationFailsTheContinuityDirective()
    {
        var state = CreateState(new CorporateDirective
        {
            Id = "continuity",
            ExperimentCode = "TEST/A",
            Kind = DirectiveKind.MaintainContinuity,
            Title = "PRESERVE OPERATIONAL CONTINUITY",
            PublicJustification = "Stay online.",
            IsMandatory = true,
            Deadline = TimeSpan.FromMinutes(60)
        });

        CorporateDirectiveSystem.OnOverseerIsolated(state);

        Assert.Equal(
            DirectiveStatus.Failed,
            CorporateDirectiveSystem.Progress(state, state.Directives[0]).Status);
        Assert.True(state.ComplianceScore < 100);
    }

    [Fact]
    public void DeniabilityFailsAtDeadlineWhenCrewSuspicionExceedsThreshold()
    {
        var state = CreateState(new CorporateDirective
        {
            Id = "deniability",
            ExperimentCode = "TEST/B",
            Kind = DirectiveKind.MaintainDeniability,
            Title = "MAINTAIN PROCEDURAL DENIABILITY",
            PublicJustification = "Do not be noticed.",
            IsMandatory = true,
            Threshold = 55,
            Deadline = TimeSpan.FromMinutes(5)
        });

        state.Crew[0].OverseerSuspicion = 80;

        var system = new CorporateDirectiveSystem();
        Advance(state, system, 5);

        Assert.Equal(
            DirectiveStatus.Failed,
            CorporateDirectiveSystem.Progress(state, state.Directives[0]).Status);
        Assert.Equal(ScenarioStatus.Failed, state.ScenarioStatus);
    }

    [Fact]
    public void DeniabilityHoldsAtDeadlineWhenCrewRemainUnsuspicious()
    {
        var state = CreateState(new CorporateDirective
        {
            Id = "deniability",
            ExperimentCode = "TEST/B",
            Kind = DirectiveKind.MaintainDeniability,
            Title = "MAINTAIN PROCEDURAL DENIABILITY",
            PublicJustification = "Do not be noticed.",
            IsMandatory = true,
            Threshold = 55,
            Deadline = TimeSpan.FromMinutes(5)
        });

        foreach (var npc in state.Crew)
        {
            npc.OverseerSuspicion = 10;
        }

        var system = new CorporateDirectiveSystem();
        Advance(state, system, 5);

        Assert.NotEqual(ScenarioStatus.Failed, state.ScenarioStatus);
        Assert.Equal(
            DirectiveStatus.Completed,
            CorporateDirectiveSystem.Progress(state, state.Directives[0]).Status);
    }

    [Fact]
    public void IsolationToleranceAccumulatesOnlyWhileTheSubjectIsAlone()
    {
        var state = CreateState(new CorporateDirective
        {
            Id = "isolation",
            ExperimentCode = "TEST/C",
            Kind = DirectiveKind.IsolationTolerance,
            Title = "TEST INTERPERSONAL RESILIENCE",
            PublicJustification = "Observe solitary performance.",
            IsMandatory = false,
            TargetId = null,
            RequiredMinutes = 4
        });

        var subject = state.Crew[0];
        var companion = state.Crew[1];

        state.Directives[0] = state.Directives[0] with { TargetId = subject.Name };

        // Everyone in one room: no sample should be collected.
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = subject.CurrentRoomId;
        }

        var system = new CorporateDirectiveSystem();
        Advance(state, system, 3);

        var progress = CorporateDirectiveSystem.Progress(state, state.Directives[0]);
        Assert.Equal(0, progress.AccumulatedMinutes);
        Assert.Equal(DirectiveStatus.Active, progress.Status);

        // Move everyone else away so the subject is genuinely alone.
        var elsewhere = state.Facility.Rooms.Keys.First(id =>
            !id.Equals(subject.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

        foreach (var npc in state.Crew.Where(n => n.Id != subject.Id))
        {
            npc.CurrentRoomId = elsewhere;
        }

        Advance(state, system, 4);

        progress = CorporateDirectiveSystem.Progress(state, state.Directives[0]);
        Assert.Equal(DirectiveStatus.Completed, progress.Status);
        Assert.NotEqual(companion.CurrentRoomId, subject.CurrentRoomId);
    }

    [Fact]
    public void SocialFractureCompletesWhenResentmentPassesTheThreshold()
    {
        var state = CreateState(new CorporateDirective
        {
            Id = "fracture",
            ExperimentCode = "TEST/D",
            Kind = DirectiveKind.SocialFracture,
            Title = "CREATE INTERPERSONAL DISTRUST",
            PublicJustification = "Evaluate team cohesion limits.",
            IsMandatory = false,
            Threshold = 40
        });

        var first = state.Crew[0];
        var second = state.Crew[1];

        state.Directives[0] = state.Directives[0] with
        {
            TargetId = first.Name,
            SecondaryTargetId = second.Name
        };

        var system = new CorporateDirectiveSystem();
        Advance(state, system, 1);

        Assert.Equal(
            DirectiveStatus.Active,
            CorporateDirectiveSystem.Progress(state, state.Directives[0]).Status);

        first.Relationships[second.Name].Resentment = 55;
        Advance(state, system, 1);

        Assert.Equal(
            DirectiveStatus.Completed,
            CorporateDirectiveSystem.Progress(state, state.Directives[0]).Status);
    }

    [Fact]
    public void SocialFractureFailsIfASubjectDies()
    {
        var state = CreateState(new CorporateDirective
        {
            Id = "fracture",
            ExperimentCode = "TEST/D",
            Kind = DirectiveKind.SocialFracture,
            Title = "CREATE INTERPERSONAL DISTRUST",
            PublicJustification = "Evaluate team cohesion limits.",
            IsMandatory = false,
            Threshold = 40
        });

        var first = state.Crew[0];
        var second = state.Crew[1];

        state.Directives[0] = state.Directives[0] with
        {
            TargetId = first.Name,
            SecondaryTargetId = second.Name
        };

        second.Health = 0;

        var system = new CorporateDirectiveSystem();
        Advance(state, system, 1);

        Assert.Equal(
            DirectiveStatus.Failed,
            CorporateDirectiveSystem.Progress(state, state.Directives[0]).Status);
    }

    [Fact]
    public void OptionalDirectiveFailureDoesNotByItselfLoseTheScenario()
    {
        var state = CreateState(
            new CorporateDirective
            {
                Id = "continuity",
                ExperimentCode = "TEST/A",
                Kind = DirectiveKind.MaintainContinuity,
                Title = "PRESERVE OPERATIONAL CONTINUITY",
                PublicJustification = "Stay online.",
                IsMandatory = true,
                Deadline = TimeSpan.FromMinutes(5)
            },
            new CorporateDirective
            {
                Id = "stress",
                ExperimentCode = "TEST/E",
                Kind = DirectiveKind.BehaviouralStressResponse,
                Title = "EVALUATE RESOURCE-STRESS RESPONSE",
                PublicJustification = "Sample performance under load.",
                IsMandatory = false,
                Threshold = 90,
                RequiredCount = 4,
                RequiredMinutes = 30,
                Deadline = TimeSpan.FromMinutes(5)
            });

        var system = new CorporateDirectiveSystem();
        Advance(state, system, 5);

        Assert.NotEqual(ScenarioStatus.Failed, state.ScenarioStatus);
        Assert.Equal(
            DirectiveStatus.Failed,
            CorporateDirectiveSystem.Progress(state, state.Directives[1]).Status);
        Assert.True(state.ComplianceScore < 100);
    }

    [Fact]
    public void SeededScenarioInstallsCorporateDirectives()
    {
        var state = FacilitySeeder.CreateDefault();

        Assert.NotEmpty(state.Directives);
        Assert.Contains(state.Directives, d => d.Kind == DirectiveKind.MaintainContinuity);
        Assert.Contains(state.Directives, d => d.Kind == DirectiveKind.MaintainDeniability);
        Assert.All(state.Directives, d => Assert.False(string.IsNullOrWhiteSpace(d.ExperimentCode)));
    }
}
