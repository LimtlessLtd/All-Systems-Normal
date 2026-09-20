using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class InvestigationAndScenarioTests
{
    [Fact]
    public void Investigation_DoesNotDiscoverRemoteShutdownHardware()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var mechanism = Assert.Single(state.ShutdownMechanisms);

        sarah.CurrentRoomId = "engineering";
        sarah.CurrentAction = new NpcAction(
            ActionKind.Investigate,
            mechanism.RoomId,
            "Check isolation.");

        new InvestigationSystem().Tick(state);

        Assert.Empty(sarah.KnownShutdownMechanismIds);
        Assert.False(sarah.KnowsShutdownControl);
        Assert.Equal(ActionKind.Idle, sarah.CurrentAction.Kind);
    }

    [Fact]
    public void Investigation_RequiresTimeAndPhysicallyDiscoversShutdownHardware()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var mechanism = Assert.Single(state.ShutdownMechanisms);

        sarah.CurrentRoomId = mechanism.RoomId;
        sarah.CurrentAction = new NpcAction(
            ActionKind.Investigate,
            mechanism.RoomId,
            "Inspect the emergency hardware.");

        var system = new InvestigationSystem();
        system.Tick(state);

        Assert.Empty(sarah.KnownShutdownMechanismIds);
        Assert.True(sarah.RoutineUntil > state.Elapsed);

        state.Elapsed += TimeSpan.FromMinutes(InvestigationSystem.InvestigationMinutes);
        system.Tick(state);

        Assert.Contains(mechanism.Id, sarah.KnownShutdownMechanismIds);
        Assert.True(sarah.KnowsShutdownControl);
        Assert.Contains(
            sarah.Discoveries,
            discovery => discovery.Id == $"shutdown:{mechanism.Id}");
        Assert.Equal(1, state.Telemetry.ShutdownControlsDiscovered);
        Assert.Equal(1, state.Telemetry.InvestigationsCompleted);
    }

    [Fact]
    public void RedundantScenario_FirstDiscoveryCreatesLeadNotKnowledgeForSecondControl()
    {
        var state = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(
            state,
            new ScenarioDefinition(
                "redundant",
                "REDUNDANT",
                "",
                ShutdownAccessVariant.Redundant,
                []));

        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var first = state.ShutdownMechanisms.Single(mechanism => mechanism.Id == "shutdown-a");
        var second = state.ShutdownMechanisms.Single(mechanism => mechanism.Id == "shutdown-b");

        sarah.CurrentRoomId = first.RoomId;
        sarah.CurrentAction = new NpcAction(
            ActionKind.Investigate,
            first.RoomId,
            "Inspect isolation.");

        var investigations = new InvestigationSystem();
        investigations.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(InvestigationSystem.InvestigationMinutes);
        investigations.Tick(state);

        Assert.Contains(first.Id, sarah.KnownShutdownMechanismIds);
        Assert.DoesNotContain(second.Id, sarah.KnownShutdownMechanismIds);
        Assert.Contains(
            sarah.InvestigationLeads.Values,
            lead => lead.RoomId == second.RoomId
                && lead.Stage == InvestigationLeadStage.Open);
    }

    [Fact]
    public void TestimonyPreservesProvenanceAndDoesNotGrantPhysicalControlKnowledge()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var mechanism = Assert.Single(state.ShutdownMechanisms);

        sarah.CurrentRoomId = "engineering";
        david.CurrentRoomId = "engineering";
        sarah.OverseerSuspicion = 70;
        sarah.KnownShutdownMechanismIds.Add(mechanism.Id);
        sarah.KnowsShutdownControl = true;

        SuspicionSystem.AddEvidence(
            state,
            sarah,
            "I directly saw Overseer seal the isolation route.",
            20,
            origin: EvidenceOrigin.DirectObservation,
            locationId: "hall-isolation",
            evidenceId: "root-sealed-route");

        sarah.Relationships[david.Name].Trust = 80;
        david.Relationships[sarah.Name].Trust = 80;

        new SuspicionSystem().Tick(state);

        var testimony = Assert.Single(
            david.OverseerEvidence,
            evidence => evidence.Origin == EvidenceOrigin.Testimony);

        Assert.Equal("Sarah Chen", testimony.SourceNpcName);
        Assert.Equal("root-sealed-route", testimony.SourceEvidenceId);
        Assert.DoesNotContain(mechanism.Id, david.KnownShutdownMechanismIds);
        Assert.Contains(
            david.InvestigationLeads.Values,
            lead => lead.RoomId == mechanism.RoomId);
        Assert.Equal(1, state.Telemetry.EvidenceShared);
    }

    [Fact]
    public void WitnessedLocalSystemDisruption_CreatesLeadWithoutRemoteKnowledge()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var engineering = state.Facility.Rooms["engineering"];

        sarah.CurrentRoomId = engineering.Id;
        david.CurrentRoomId = "control";

        new SuspicionSystem().ObservePlayerRoomSystemChange(
            state,
            engineering,
            "ventilation",
            becameDisruptive: true,
            weight: 9);

        Assert.Single(sarah.OverseerEvidence);
        Assert.Contains(
            sarah.InvestigationLeads.Values,
            lead => lead.RoomId == engineering.Id);
        Assert.Empty(david.OverseerEvidence);
        Assert.DoesNotContain(
            david.InvestigationLeads.Values,
            lead => lead.RoomId == engineering.Id);
    }

    [Fact]
    public void RecruitmentAndJoin_CreateTeamButDoNotTransferHardwareKnowledge()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var mechanism = Assert.Single(state.ShutdownMechanisms);

        sarah.CurrentRoomId = "engineering";
        david.CurrentRoomId = "engineering";
        sarah.OverseerSuspicion = 90;
        david.OverseerSuspicion = 70;
        sarah.KnownShutdownMechanismIds.Add(mechanism.Id);
        sarah.KnowsShutdownControl = true;

        sarah.CurrentAction = new NpcAction(
            ActionKind.RecruitShutdownAlly,
            david.Name,
            "Ask David to help.");

        var coordination = new ShutdownCoordinationSystem();
        coordination.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(1);
        coordination.Tick(state);

        Assert.NotNull(david.PendingShutdownTeamInvitation);
        Assert.Empty(david.KnownShutdownMechanismIds);

        var invitation = david.PendingShutdownTeamInvitation!;
        david.CurrentAction = new NpcAction(
            ActionKind.JoinShutdownTeam,
            invitation.TeamId,
            "Join the plan.");

        coordination.Tick(state);

        var team = Assert.Single(state.ShutdownTeams);
        Assert.Contains(sarah.Id, team.MemberIds);
        Assert.Contains(david.Id, team.MemberIds);
        Assert.Empty(david.KnownShutdownMechanismIds);
        Assert.Equal(team.Id, david.ShutdownTeamId);
        Assert.Equal(1, state.Telemetry.ShutdownTeamsFormed);
    }

    [Fact]
    public void ScenarioProgress_CompletesPrimaryAndScoresOptionalObjectives()
    {
        var state = FacilitySeeder.CreateDefault();
        var system = new ScenarioProgressSystem();

        // This exercises the station objective layer on its own. Corporate
        // directives are graded by CorporateDirectiveSystem and gate the
        // outcome too, so drop them to keep the subject of the test isolated.
        state.Directives.Clear();
        state.DirectiveProgress.Clear();

        var window = ScenarioCatalog.ObservationWindow;

        state.Elapsed = window - TimeSpan.FromMinutes(1);
        system.Tick(state, state.Elapsed);

        Assert.Equal(ScenarioStatus.Running, state.ScenarioStatus);
        Assert.False(state.ObjectiveProgress["survive"].IsComplete);

        state.Elapsed = window;
        system.Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(ScenarioStatus.Won, state.ScenarioStatus);
        Assert.True(state.ObjectiveProgress["survive"].IsComplete);
        Assert.True(state.ObjectiveProgress["crew-alive"].IsComplete);
        Assert.True(state.ObjectiveProgress["life-support"].IsComplete);
        Assert.True(state.Telemetry.Score > 0);
        Assert.Contains(
            "optional objectives",
            state.ScenarioOutcome!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OllamaRejectsInventedShutdownControlAndAcceptsVerifiedCoordinatedOne()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var mechanism = Assert.Single(state.ShutdownMechanisms);
        sarah.OverseerSuspicion = 90;

        using (var inventedClient = new StubChatClient(
            """
            {
              "Action": "ShutdownOverseer",
              "TargetId": "shutdown-imaginary",
              "Goal": "Shut Overseer down.",
              "Reason": "I somehow know about a hidden control.",
              "Urgency": 100
            }
            """))
        {
            var service = new OllamaAiDecisionService(
                inventedClient,
                new RuleBasedAiDecisionService());
            var intent = await service.DecideAsync(sarah, state);

            Assert.Equal(ActionKind.Idle, intent.Action);
            Assert.Null(intent.TargetId);
        }

        sarah.KnownShutdownMechanismIds.Add(mechanism.Id);
        sarah.KnowsShutdownControl = true;
        var team = new ShutdownTeam
        {
            Id = "verified-team",
            MechanismId = mechanism.Id,
            LeaderId = sarah.Id,
            FormedAt = state.Elapsed
        };
        team.MemberIds.Add(sarah.Id);
        team.MemberIds.Add(david.Id);
        state.ShutdownTeams.Add(team);
        sarah.ShutdownTeamId = team.Id;

        using var verifiedClient = new StubChatClient(
            $$"""
            {
              "Action": "ShutdownOverseer",
              "TargetId": "{{mechanism.Id}}",
              "Goal": "Reach the verified isolation control.",
              "Reason": "My team has committed and I personally verified the hardware.",
              "Urgency": 100
            }
            """);

        var verifiedService = new OllamaAiDecisionService(
            verifiedClient,
            new RuleBasedAiDecisionService());
        var verifiedIntent = await verifiedService.DecideAsync(sarah, state);

        Assert.Equal(ActionKind.ShutdownOverseer, verifiedIntent.Action);
        Assert.Equal(mechanism.Id, verifiedIntent.TargetId);
    }

    [Fact]
    public void Prompt_SeparatesInvestigationLeadsClaimsAndVerifiedControls()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var mechanism = Assert.Single(state.ShutdownMechanisms);

        sarah.InvestigationLeads.Clear();
        sarah.InvestigationLeads["lead-test"] = new InvestigationLead
        {
            Id = "lead-test",
            Description = "A suspicious sign points toward isolation.",
            RoomId = mechanism.RoomId,
            CreatedAt = state.Elapsed
        };

        var unverifiedPrompt = NpcPromptBuilder.Build(sarah, state);

        Assert.Contains("OPEN INVESTIGATION LEADS", unverifiedPrompt);
        Assert.Contains("lead-test", unverifiedPrompt);
        Assert.Contains("VERIFIED SHUTDOWN CONTROLS", unverifiedPrompt);
        Assert.Contains("none personally verified", unverifiedPrompt);
        Assert.DoesNotContain(
            $"{mechanism.Id}: {mechanism.Label}",
            unverifiedPrompt);

        sarah.KnownShutdownMechanismIds.Add(mechanism.Id);
        sarah.KnowsShutdownControl = true;

        var verifiedPrompt = NpcPromptBuilder.Build(sarah, state);

        Assert.Contains(
            $"{mechanism.Id}: {mechanism.Label}",
            verifiedPrompt);
    }

    private sealed class StubChatClient : Microsoft.Extensions.AI.IChatClient
    {
        private readonly string _json;

        public StubChatClient(string json) => _json = json;

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        _json)));

        public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate>
            GetStreamingResponseAsync(
                IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
                Microsoft.Extensions.AI.ChatOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this)
                ? this
                : null;

        public void Dispose()
        {
        }
    }
}
