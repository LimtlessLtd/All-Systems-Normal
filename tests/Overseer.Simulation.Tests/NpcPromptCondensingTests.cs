using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

// Owner idea #97: the prompt only offers actions that have a valid target for
// this person right now. Validation of every action is unchanged.
public sealed class NpcPromptCondensingTests
{
    [Fact]
    public void FreshStation_LeavesOutActionsThatHaveNoTargetYet()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        var prompt = NpcPromptBuilder.Build(npc, state);

        foreach (var absent in new[]
                 {
                     "- ShutdownRobot [robot]",
                     "- DisarmTurret [turret]",
                     "- IsolateSecurityController [security-controller]",
                     "- ShutdownOverseer [shutdown-control]",
                     "- JoinShutdownTeam [team]",
                     "- AcceptPact [pact-proposal]",
                     "- FulfillPact [pact]",
                     "- AssumeRole [vacant-post]",
                     "- SecureAirlock [airlock]",
                     "- RestoreSystem [system]",
                     "For ShutdownRobot/",
                     "For DisarmTurret/",
                     "For AssumeRole,",
                 })
        {
            Assert.DoesNotContain(absent, prompt);
        }

        // Actions that always have targets stay.
        Assert.Contains("- Move [room]", prompt);
        Assert.Contains("- Talk [crew]", prompt);
        Assert.Contains("- OpenDoor [adjacent-door]", prompt);
        Assert.Contains("- ProposePact [crew]", prompt);
        Assert.Contains("- FightFire [room]", prompt);
    }

    [Fact]
    public void RobotInTheRoom_OffersRobotActionsAndTheirTargetContract()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        state.Robots.Add(new StationRobot
        {
            Id = "robot-1",
            Name = "Maintenance Bot",
            CurrentRoomId = npc.CurrentRoomId,
            Policy = RobotPolicy.Hostile
        });

        var prompt = NpcPromptBuilder.Build(npc, state);

        Assert.Contains("- ShutdownRobot [robot]", prompt);
        Assert.Contains("- DamageRobot [robot]", prompt);
        Assert.Contains("For ShutdownRobot/DamageRobot/ReprogramRobot, TargetId must", prompt);
        Assert.DoesNotContain("- DisarmTurret [turret]", prompt);
    }

    [Fact]
    public void PendingPactProposal_OffersAcceptPact()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var proposer = state.Crew[1];
        npc.PendingPactProposal = new PactProposal(
            proposer.Id,
            proposer.Name,
            CrewPactKind.Other,
            "I'll cover your shift if you keep quiet.",
            TriggerAt: null,
            Deadline: null,
            OfferedAt: state.Elapsed);

        var prompt = NpcPromptBuilder.Build(npc, state);

        Assert.Contains("- AcceptPact [pact-proposal]", prompt);
        Assert.Contains("For AcceptPact, TargetId must", prompt);
    }

    [Fact]
    public void UnfilteredCatalog_StillListsEveryAffordance()
    {
        var catalog = CrewAffordanceSystem.PromptCatalog();

        foreach (var entry in CrewAffordanceSystem.Catalog)
        {
            Assert.Contains($"- {entry.Action} [{entry.TargetType}]", catalog);
        }
    }
}
