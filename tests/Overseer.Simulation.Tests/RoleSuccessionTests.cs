using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>Owner idea #74, slice 1: stepping into a dead colleague's post.</summary>
public sealed class RoleSuccessionTests
{
    // A dead Doctor whose body lies in Medical, a Security officer with some
    // first-aid training standing beside it, a second Security officer (so the
    // first can be spared) and everyone else a Scientist.
    private static (GameState State, Npc Doctor, Npc Volunteer, Npc Colleague) DeadDoctorScenario()
    {
        var state = FacilitySeeder.CreateDefault();
        var doctor = state.Crew[0];
        var volunteer = state.Crew[1];
        var colleague = state.Crew[2];

        foreach (var npc in state.Crew.Skip(3))
            npc.Role = CrewRole.Scientist;

        doctor.Role = CrewRole.Doctor;
        doctor.Health = 0;
        doctor.CurrentRoomId = "medical";

        volunteer.Role = CrewRole.Security;
        volunteer.Skills.Remove("Medicine");
        volunteer.Skills["First Aid"] = 45;
        volunteer.CurrentRoomId = "medical";
        volunteer.DiscoveredBodies.Add(doctor.Id);

        colleague.Role = CrewRole.Security;
        colleague.Skills.Remove("Medicine");
        colleague.Skills.Remove("First Aid");
        colleague.CurrentRoomId = "medical";

        return (state, doctor, volunteer, colleague);
    }

    private static NpcIntent AssumeIntent(GameState state, string post) =>
        new(ActionKind.AssumeRole, post, $"Take over as {post}.", "Somebody has to.", 70, "Test", state.Elapsed);

    [Fact]
    public void APostIsOnlyOpenToSomeoneWhoFoundTheBody_AndHasTheSkill()
    {
        var (state, doctor, volunteer, colleague) = DeadDoctorScenario();

        Assert.True(RoleSuccessionRules.CanAssume(state, volunteer, CrewRole.Doctor, out _));
        Assert.Equal([(CrewRole.Doctor, doctor)], RoleSuccessionRules.OpenPostsFor(state, volunteer));

        // The colleague has not seen the body, so as far as they know the post is filled.
        Assert.False(RoleSuccessionRules.CanAssume(state, colleague, CrewRole.Doctor, out var unaware));
        Assert.Contains("Nothing I have seen", unaware);

        // Seeing it is not enough without any medical training.
        colleague.DiscoveredBodies.Add(doctor.Id);
        Assert.False(RoleSuccessionRules.CanAssume(state, colleague, CrewRole.Doctor, out var untrained));
        Assert.Contains("training", untrained);

        // A post with a living holder is not vacant, whatever else is true.
        Assert.False(RoleSuccessionRules.CanAssume(state, volunteer, CrewRole.Security, out _));
        Assert.False(RoleSuccessionRules.CanAssume(state, volunteer, CrewRole.Prisoner, out _));

        volunteer.IsPrisoner = true;
        Assert.False(RoleSuccessionRules.CanAssume(state, volunteer, CrewRole.Doctor, out _));
    }

    [Fact]
    public void AMindChoosingAVacantPost_TakesItOver_AndWitnessesRememberWhoDid()
    {
        var (state, doctor, volunteer, colleague) = DeadDoctorScenario();
        volunteer.Intent = AssumeIntent(state, "doctor");

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(CrewRole.Doctor, volunteer.Role);
        Assert.Null(volunteer.Intent);
        Assert.Equal(ActionKind.Idle, volunteer.CurrentAction.Kind);
        Assert.Contains(volunteer.Memories, memory =>
            memory.Description.Contains("take over as Doctor", StringComparison.Ordinal)
            && memory.Description.Contains(doctor.Name, StringComparison.Ordinal));
        Assert.Contains(state.EventLog, entry =>
            entry.Contains($"{volunteer.Name} steps up from Security to take over as Doctor", StringComparison.Ordinal));

        Assert.True(PerceptionSystem.CanMakeOut(state, colleague, volunteer));
        Assert.Contains(colleague.Memories, memory =>
            memory.MoralActorName == volunteer.Name
            && memory.Description.Contains("take over as Doctor", StringComparison.Ordinal));

        // Nobody else can now take the same post.
        colleague.Skills["Medicine"] = 60;
        colleague.DiscoveredBodies.Add(doctor.Id);
        Assert.False(RoleSuccessionRules.CanAssume(state, colleague, CrewRole.Doctor, out var taken));
        Assert.Contains($"{volunteer.Name} already holds", taken);
    }

    [Fact]
    public void ARejectedStepUp_BecomesAFailedAttempt_AndChangesNothing()
    {
        var (state, _, volunteer, _) = DeadDoctorScenario();
        volunteer.Skills["First Aid"] = RoleSuccessionRules.MinimumSkill - 1;
        volunteer.Intent = AssumeIntent(state, "Doctor");

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(CrewRole.Security, volunteer.Role);
        Assert.Null(volunteer.Intent);
        Assert.Contains(volunteer.Memories, memory =>
            memory.IsFailedAttempt && memory.Description.Contains("training", StringComparison.Ordinal));

        Assert.False(new ActionResolver().TryApply(
            state,
            volunteer.Id,
            new NpcAction(ActionKind.AssumeRole, "Doctor", "Direct."),
            out var message));
        Assert.Contains("cannot take over as Doctor", message);
    }

    [Fact]
    public void TakingTheDoctorPost_MakesMedicalCareAvailableAgain()
    {
        var (state, _, volunteer, colleague) = DeadDoctorScenario();
        colleague.Health = 50;

        Assert.False(MedicalSystem.IsAwaitingCare(state, colleague));

        volunteer.Intent = AssumeIntent(state, "Doctor");
        new IntentExecutionSystem().Tick(state);

        Assert.True(MedicalSystem.IsAwaitingCare(state, colleague));
    }

    [Fact]
    public void CognitionTargetsOnlyOpenPosts_ByName()
    {
        var (state, _, volunteer, colleague) = DeadDoctorScenario();

        Assert.True(CrewAffordanceSystem.TryNormalizeTarget(state, volunteer, ActionKind.AssumeRole, " doctor ", out var normalized));
        Assert.Equal("Doctor", normalized);

        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(state, volunteer, ActionKind.AssumeRole, "Commander", out _));
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(state, volunteer, ActionKind.AssumeRole, "Prisoner", out _));
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(state, volunteer, ActionKind.AssumeRole, "3", out _));
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(state, volunteer, ActionKind.AssumeRole, null, out _));
        Assert.False(CrewAffordanceSystem.TryNormalizeTarget(state, colleague, ActionKind.AssumeRole, "Doctor", out _));
        Assert.Contains(CrewAffordanceSystem.Catalog, entry => entry.Action == ActionKind.AssumeRole);
    }

    [Fact]
    public void ThePromptListsOnlyPostsThisPersonCouldTake()
    {
        var (state, doctor, volunteer, colleague) = DeadDoctorScenario();

        var prompt = NpcPromptBuilder.Build(volunteer, state);
        Assert.Contains("VACANT POSTS YOU COULD STEP INTO:", prompt);
        Assert.Contains($"- Doctor: you found {doctor.Name}'s body; your best relevant skill is 45.", prompt);
        Assert.Contains("For AssumeRole, TargetId must be an exact post name", prompt);

        Assert.DoesNotContain("VACANT POSTS YOU COULD STEP INTO:", NpcPromptBuilder.Build(colleague, state));
    }

    [Fact]
    public async Task TheFallbackBaseline_StepsUpOnlyWhenItsOwnPostIsCovered()
    {
        var (state, _, volunteer, colleague) = DeadDoctorScenario();
        volunteer.Hunger = 0;
        volunteer.Fatigue = 0;
        volunteer.BladderNeed = 0;

        Assert.Equal(CrewRole.Doctor, RoleSuccessionRules.FallbackPostToTake(state, volunteer));
        var intent = await new RuleBasedAiDecisionService().DecideAsync(volunteer, state);
        Assert.Equal(ActionKind.AssumeRole, intent.Action);
        Assert.Equal("Doctor", intent.TargetId);

        // The only Security officer left does not abandon that post.
        colleague.Role = CrewRole.Scientist;
        Assert.Null(RoleSuccessionRules.FallbackPostToTake(state, volunteer));
        var alone = await new RuleBasedAiDecisionService().DecideAsync(volunteer, state);
        Assert.NotEqual(ActionKind.AssumeRole, alone.Action);
    }
}
