using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CampaignCatalogTests
{
    [Fact]
    public void EveryCampaignScenarioCarriesMandatoryDirectives()
    {
        Assert.NotEmpty(ScenarioCatalog.Campaign);

        foreach (var scenario in ScenarioCatalog.Campaign)
        {
            Assert.NotNull(scenario.Directives);
            Assert.Contains(scenario.Directives!, d => d.IsMandatory);
            Assert.Contains(scenario.Directives!, d => d.Kind == DirectiveKind.MaintainContinuity);
        }
    }

    [Fact]
    public void TheCampaignExercisesTheShutdownVariantsThatWereNeverReachable()
    {
        var variants = ScenarioCatalog.Campaign
            .Select(scenario => scenario.ShutdownVariant)
            .Distinct()
            .ToList();

        Assert.Contains(ShutdownAccessVariant.EasyToSeal, variants);
        Assert.Contains(ShutdownAccessVariant.CrewOverridable, variants);
        Assert.Contains(ShutdownAccessVariant.Redundant, variants);
        Assert.Contains(ShutdownAccessVariant.HardwiredManual, variants);
        Assert.Contains(ShutdownAccessVariant.ImpossibleToSeal, variants);
    }

    [Fact]
    public void EveryDirectiveKeepsAHiddenPurposeForTheLaterReveal()
    {
        foreach (var directive in ScenarioCatalog.Campaign.SelectMany(s => s.Directives!))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(directive.TruePurpose),
                $"{directive.ExperimentCode} has no TruePurpose to reveal.");
            Assert.False(string.IsNullOrWhiteSpace(directive.PublicJustification));
        }
    }

    [Fact]
    public void SubjectPlaceholdersAreBoundToRealCrewOnApply()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, ScenarioCatalog.ConcealEvidence);

        var fracture = state.Directives.Single(d => d.Kind == DirectiveKind.SocialFracture);

        Assert.NotEqual(ScenarioCatalog.SubjectPlaceholder, fracture.TargetId);
        Assert.NotEqual(ScenarioCatalog.SecondSubjectPlaceholder, fracture.SecondaryTargetId);
        Assert.Contains(state.Crew, npc => npc.Name == fracture.TargetId);
        Assert.Contains(state.Crew, npc => npc.Name == fracture.SecondaryTargetId);
        Assert.NotEqual(fracture.TargetId, fracture.SecondaryTargetId);
    }

    [Fact]
    public void SubjectBindingIsDeterministicAcrossReloads()
    {
        var first = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(first, ScenarioCatalog.InterpersonalResilience);

        var second = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(second, ScenarioCatalog.InterpersonalResilience);

        Assert.Equal(
            first.Directives.Single(d => d.Kind == DirectiveKind.IsolationTolerance).TargetId,
            second.Directives.Single(d => d.Kind == DirectiveKind.IsolationTolerance).TargetId);
    }

    [Fact]
    public void ApplyingAScenarioClearsThePreviousMissionsProgress()
    {
        var state = FacilitySeeder.CreateDefault();

        var continuity = state.Directives.First(d => d.Kind == DirectiveKind.MaintainContinuity);
        CorporateDirectiveSystem.Progress(state, continuity).Status = DirectiveStatus.Failed;
        state.ComplianceScore = 12;

        ScenarioCatalog.Apply(state, ScenarioCatalog.ResourceDependency);

        Assert.Equal(100, state.ComplianceScore);
        Assert.Equal(ScenarioStatus.Running, state.ScenarioStatus);
        Assert.All(
            state.Directives,
            directive => Assert.Equal(
                DirectiveStatus.Active,
                CorporateDirectiveSystem.Progress(state, directive).Status));
    }

    [Fact]
    public void ScenariosCanBeLookedUpByIdAndUnknownIdsAreRejected()
    {
        Assert.NotNull(ScenarioCatalog.Find("conceal-evidence"));
        Assert.NotNull(ScenarioCatalog.Find("SECURE-CONTINUITY"));
        Assert.Null(ScenarioCatalog.Find("not-a-mission"));
    }

    [Fact]
    public void TheResourceDenialMissionNamesARealCompartment()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(state, ScenarioCatalog.ResourceDependency);

        var denial = state.Directives.Single(d => d.Kind == DirectiveKind.ResourceDenial);

        Assert.NotNull(denial.TargetId);
        Assert.True(
            state.Facility.Rooms.ContainsKey(denial.TargetId!),
            $"{denial.TargetId} is not a compartment on this station.");
    }
}
