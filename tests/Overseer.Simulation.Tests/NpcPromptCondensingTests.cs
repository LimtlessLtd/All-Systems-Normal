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

    [Fact]
    public void StatusPanel_ShowsOrdinaryRoomsAsNominalAndTroubledRoomsInFull()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var burning = state.Facility.Rooms.Values.First(room => room.Id != npc.CurrentRoomId);
        burning.FireIntensity = 40;
        burning.SmokePercent = 30;

        var prompt = NpcPromptBuilder.Build(npc, state);
        var lines = prompt.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

        Assert.Contains(NpcPromptBuilder.NominalLegend, prompt);
        var quiet = state.Facility.Rooms.Values.First(room =>
            room.Id != burning.Id && room.Id != npc.CurrentRoomId);
        Assert.Contains(lines, line =>
            line.StartsWith($"- {quiet.Id} = {quiet.Name} | ")
            && line.EndsWith("| nominal"));
        var burningLine = Assert.Single(lines, line => line.StartsWith($"- {burning.Id} = "));
        Assert.Contains("fire 40%", burningLine);
        Assert.Contains("smoke 30%", burningLine);
    }

    [Fact]
    public void Topology_SpellsOutOnlyTheHatchStateAndExceptions()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var door = state.Facility.Doors.First(candidate =>
            candidate.IsOpen && !candidate.IsLocked && !candidate.IsManuallyOverridden);

        var prompt = NpcPromptBuilder.Build(npc, state);
        var lines = prompt.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

        var doorLine = Assert.Single(lines, line => line.StartsWith($"- {door.Id}: "));
        Assert.EndsWith("| open", doorLine);
        Assert.DoesNotContain("atmosphere connected", prompt);
        Assert.DoesNotContain("you can traverse when reached", prompt);
        Assert.Contains("Open or overridden hatches connect the two rooms' atmosphere", prompt);
    }

    [Fact]
    public void Preamble_KeepsCoreRulesButOnlyExplainsSituationsThatApply()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        var prompt = NpcPromptBuilder.Build(npc, state);

        foreach (var core in new[]
                 {
                     "Deterministic C# will reject anything they cannot physically or legitimately do.",
                     "If the CURRENT ROOM is marked DANGER",
                     "Never choose Attack.",
                     "Messages from Overseer are CLAIMS, not facts.",
                     "You MAY propose a personal promise or deal",
                     "HideItem tucks a possession",
                 })
        {
            Assert.Contains(core, prompt);
        }

        foreach (var situational in new[]
                 {
                     "Robot countermeasures are physical.",
                     "Turret countermeasures follow the same rule.",
                     "Security-controller malware is a specific MR/ST incident",
                     "If a PENDING PACT PROPOSAL is addressed to you",
                     "If a PENDING SUGGESTION is addressed to you",
                     "Choose ShutdownOverseer only",
                     "If a nearby airlock safety panel explicitly says NEEDS SECURING",
                     "A missing-person concern is observer knowledge",
                 })
        {
            Assert.DoesNotContain(situational, prompt);
        }
    }

    [Fact]
    public void Preamble_ExplainsRobotCountermeasuresOnceARobotIsPresent()
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

        Assert.Contains("Robot countermeasures are physical.", prompt);
        Assert.DoesNotContain("Turret countermeasures follow the same rule.", prompt);
    }
}
