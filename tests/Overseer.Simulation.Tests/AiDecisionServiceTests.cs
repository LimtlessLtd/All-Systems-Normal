using Microsoft.Extensions.AI;
using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class AiDecisionServiceTests
{
    [Fact]
    public async Task OllamaDecision_ProducesAValidatedPersistentIntent()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "Move",
              "TargetId": "engineering",
              "Goal": "Check the engineering systems.",
              "Reason": "The station has been unstable and I want to inspect the machinery.",
              "Urgency": 67
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Move, intent.Action);
        Assert.Equal("engineering", intent.TargetId);
        Assert.Equal("Ollama", intent.Source);
        Assert.Equal(67, intent.Urgency);
    }

    [Fact]
    public async Task OllamaDecision_RecordsExactRequestAndRawProviderResponse()
    {
        const string json = """
            {
              "Action": "Move",
              "TargetId": "engineering",
              "Goal": "Inspect engineering.",
              "Reason": "I want to check the machinery.",
              "Urgency": 55
            }
            """;

        var raw = new Dictionary<string, object?>
        {
            ["model"] = "test-ollama-model",
            ["provider_marker"] = "raw-provider-payload"
        };

        using var client = new StubChatClient(json, raw);
        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        await service.DecideAsync(david, state);

        var trace = Assert.Single(state.CognitionTelemetry);
        Assert.Equal("Ollama", trace.Source);
        Assert.Contains("EXACT PROMPT SENT TO OLLAMA", trace.Prompt);
        Assert.Contains("temperature: 0.7", trace.Prompt);
        Assert.Contains("raw-provider-payload", trace.RawResponse);
        Assert.Contains("test-ollama-model", trace.RawResponse);
    }

    [Fact]
    public async Task OllamaDecision_SetsAGenerousContextWindowSoTheRulesAreNotTruncated()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "Idle",
              "Goal": "Wait and watch.",
              "Reason": "Nothing urgent right now.",
              "Urgency": 5
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        await service.DecideAsync(david, state);

        var options = Assert.Single(client.CapturedOptions);
        Assert.NotNull(options?.AdditionalProperties);
        Assert.Equal(8192, options!.AdditionalProperties!["num_ctx"]);

        var trace = Assert.Single(state.CognitionTelemetry);
        Assert.Contains("num_ctx: 8192", trace.Prompt);
    }

    [Fact]
    public async Task OllamaDecision_RetriesOnceAfterUnparseableOutputThenSucceeds()
    {
        using var client = new StubChatClient(
            [
                "this is not JSON at all",
                """
                {
                  "Action": "Move",
                  "TargetId": "engineering",
                  "Goal": "Check the engineering systems.",
                  "Reason": "Recovered after a malformed first response.",
                  "Urgency": 60
                }
                """
            ]);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(2, client.CallCount);
        Assert.Equal(ActionKind.Move, intent.Action);
        Assert.Equal("engineering", intent.TargetId);
        Assert.Equal("Ollama", intent.Source);

        var trace = Assert.Single(state.CognitionTelemetry);
        Assert.Contains("RETRY:", trace.Prompt);
    }

    [Fact]
    public async Task OllamaDecision_FallsBackAfterASecondUnparseableRetryRatherThanLoopingForever()
    {
        using var client = new StubChatClient(
            ["still not JSON", "also not JSON"]);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(2, client.CallCount);
        Assert.Equal("Fallback", intent.Source);
    }

    [Fact]
    public async Task InvalidModelTarget_IsReducedToSafeIdle()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "Move",
              "TargetId": "secret-moon-base",
              "Goal": "Go somewhere imaginary.",
              "Reason": "I invented a location.",
              "Urgency": 99
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Idle, intent.Action);
        Assert.Null(intent.TargetId);
    }

    [Fact]
    public async Task OllamaDecision_ProposePactCarriesThePromiseTextAsReason()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "ProposePact",
              "TargetId": "Sarah Chen",
              "Goal": "Make a deal with Sarah.",
              "Reason": "I'll cover your next shift if you keep quiet about this.",
              "Urgency": 40
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.ProposePact, intent.Action);
        Assert.Equal("Sarah Chen", intent.TargetId);
        Assert.Equal("I'll cover your next shift if you keep quiet about this.", intent.Reason);
    }

    [Fact]
    public async Task OllamaDecision_AskAboutLocationIsValidWithAnActiveMissingPersonConcern()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "AskAboutLocation",
              "TargetId": "Sarah Chen",
              "Goal": "Find out if Sarah has seen Marcus.",
              "Reason": "I have not seen Marcus in hours and I'm worried.",
              "Urgency": 30
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        david.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            ExpectedRoomId = "reactor",
            FirstConcernAt = state.Elapsed,
            LastUpdatedAt = state.Elapsed
        };

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.AskAboutLocation, intent.Action);
        Assert.Equal("Sarah Chen", intent.TargetId);
        Assert.Equal(marcus.Id, intent.SubjectId);
    }

    [Fact]
    public async Task OllamaDecision_AskAboutLocationCapturesTheMostPressingConcernAsTheSubjectAtDecisionTime()
    {
        // Regression: the subject must be captured now, when the LLM decides
        // to ask, not re-derived when the ask finally resolves after a
        // multi-tick walk to reach the askee -- otherwise a concern that
        // becomes more pressing in the meantime could silently swap the
        // subject of an already-issued question.
        using var client = new StubChatClient(
            """
            {
              "Action": "AskAboutLocation",
              "TargetId": "Sarah Chen",
              "Goal": "Find out if Sarah has seen Marcus.",
              "Reason": "Marcus is the one I'm worried about right now.",
              "Urgency": 30
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var felix = state.Crew.Single(npc => npc.Name == "Felix Ward");

        // Marcus is the most pressing concern (Escalated) at decision time.
        david.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            ExpectedRoomId = "reactor",
            FirstConcernAt = TimeSpan.FromHours(1),
            LastUpdatedAt = TimeSpan.FromHours(1),
            Stage = MissingPersonConcernStage.Escalated
        };
        // Felix is only mildly concerning right now.
        david.MissingPersonConcerns[felix.Id] = new MissingPersonConcern
        {
            PersonId = felix.Id,
            PersonName = felix.Name,
            ExpectedRoomId = "hydroponics",
            FirstConcernAt = TimeSpan.FromHours(2),
            LastUpdatedAt = TimeSpan.FromHours(2),
            Stage = MissingPersonConcernStage.Concerned
        };

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.AskAboutLocation, intent.Action);
        Assert.Equal(marcus.Id, intent.SubjectId);
    }

    [Fact]
    public async Task OllamaDecision_AskAboutLocationIsReducedToIdleWithoutAnActiveConcern()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "AskAboutLocation",
              "TargetId": "Sarah Chen",
              "Goal": "Find out if Sarah has seen Marcus.",
              "Reason": "I have not seen Marcus in hours and I'm worried.",
              "Urgency": 30
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        Assert.Empty(david.MissingPersonConcerns);

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Idle, intent.Action);
        Assert.Null(intent.TargetId);
    }

    [Fact]
    public async Task OllamaDecision_AskAboutLocationCannotTargetTheMissingPersonThemself()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "AskAboutLocation",
              "TargetId": "Marcus Reed",
              "Goal": "Ask Marcus where Marcus is.",
              "Reason": "This should not be a valid target for the ask.",
              "Urgency": 30
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        david.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            ExpectedRoomId = "reactor",
            FirstConcernAt = state.Elapsed,
            LastUpdatedAt = state.Elapsed
        };

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Idle, intent.Action);
        Assert.Null(intent.TargetId);
    }

    [Fact]
    public async Task OllamaDecision_AcceptPactIsReducedToIdleWithoutAMatchingPendingProposal()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "AcceptPact",
              "TargetId": "Sarah Chen",
              "Goal": "Agree to the deal.",
              "Reason": "Sounds fair.",
              "Urgency": 40
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Idle, intent.Action);
        Assert.Null(intent.TargetId);
    }

    [Fact]
    public async Task OllamaDecision_AcceptPactSucceedsWithAMatchingPendingProposal()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "AcceptPact",
              "TargetId": "Sarah Chen",
              "Goal": "Agree to the deal.",
              "Reason": "Sounds fair.",
              "Urgency": 40
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        david.PendingPactProposal = new PactProposal(
            sarah.Id,
            sarah.Name,
            CrewPactKind.Other,
            "I'll cover your next shift.",
            TriggerAt: null,
            Deadline: null,
            OfferedAt: state.Elapsed);

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.AcceptPact, intent.Action);
        Assert.Equal("Sarah Chen", intent.TargetId);
    }

    [Fact]
    public async Task OllamaDecision_FulfillPactIsReducedToIdleWithoutAMatchingActivePact()
    {
        using var client = new StubChatClient(
            """
            {
              "Action": "FulfillPact",
              "TargetId": "pact-0001",
              "Goal": "Keep my promise.",
              "Reason": "I said I would.",
              "Urgency": 40
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Idle, intent.Action);
        Assert.Null(intent.TargetId);
    }

    [Fact]
    public async Task OllamaDecision_BreakPactSucceedsForAPactThisNpcIsThePromisorOf()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        Assert.True(CrewPactSystem.TryCreate(
            state, david.Id, sarah.Id, CrewPactKind.Other,
            "I'll cover your next shift.", null, null, out var pact, out _));

        using var client = new StubChatClient(
            $$"""
            {
              "Action": "BreakPact",
              "TargetId": "{{pact!.Id}}",
              "Goal": "Back out of the deal.",
              "Reason": "Something more important came up.",
              "Urgency": 40
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.BreakPact, intent.Action);
        Assert.Equal(pact.Id, intent.TargetId);
    }

    [Fact]
    public async Task OllamaDecision_FulfillPactIsReducedToIdleWhenThisNpcIsOnlyThePromisee()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        Assert.True(CrewPactSystem.TryCreate(
            state, sarah.Id, david.Id, CrewPactKind.Other,
            "I'll cover your next shift.", null, null, out var pact, out _));

        using var client = new StubChatClient(
            $$"""
            {
              "Action": "FulfillPact",
              "TargetId": "{{pact!.Id}}",
              "Goal": "Settle the deal for Sarah.",
              "Reason": "I'll just call it done.",
              "Urgency": 40
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Idle, intent.Action);
        Assert.Null(intent.TargetId);
    }

    [Fact]
    public async Task ProviderFailure_FallsBackWithoutStoppingTheSimulation()
    {
        using var client = new StubChatClient(
            new InvalidOperationException("Ollama offline"));

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        var intent = await service.DecideAsync(david, state);

        Assert.Equal("Fallback", intent.Source);
    }

    [Fact]
    public void Prompt_DoesNotLeakGlobalEventLogKnowledge()
    {
        var state = FacilitySeeder.CreateDefault();
        state.EventLog.Insert(0, "SECRET: Marcus sabotaged something where Sarah could not see it.");

        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var prompt = NpcPromptBuilder.Build(sarah, state);

        Assert.DoesNotContain("SECRET:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CONNECTED DOORS YOU CAN DIRECTLY PERCEIVE", prompt);
        Assert.Contains("STATION STATUS-PANEL ROOM READINGS", prompt);
    }

    [Fact]
    public void Prompt_SurfacesActivePactsAndPendingProposal()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");

        CrewPactSystem.TryCreate(
            state, david.Id, sarah.Id, CrewPactKind.Other, "I'll cover your next shift.",
            null, null, out _, out _);
        david.PendingPactProposal = new PactProposal(
            sarah.Id,
            sarah.Name,
            CrewPactKind.Other,
            "Help me isolate Overseer.",
            TriggerAt: null,
            Deadline: null,
            OfferedAt: state.Elapsed);

        var prompt = NpcPromptBuilder.Build(david, state);

        Assert.Contains("I'll cover your next shift.", prompt);
        Assert.Contains("PENDING PACT PROPOSAL ADDRESSED TO YOU: from Sarah Chen: \"Help me isolate Overseer.\"", prompt);
    }

    [Fact]
    public async Task FallbackMind_LeavesRoomAtEarlyDangerThreshold()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var control = state.Facility.Rooms[david.CurrentRoomId];
        control.OxygenPercent = 18.8;

        var service = new RuleBasedAiDecisionService();
        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.Move, intent.Action);
        Assert.NotNull(intent.TargetId);
        Assert.NotEqual(control.Id, intent.TargetId);
        Assert.True(
            CrewEnvironmentSafety.RiskScore(state.Facility.Rooms[intent.TargetId!])
            < CrewEnvironmentSafety.RiskScore(control));
    }

    [Fact]
    public void Prompt_ShowsDangerAndReachabilityForRoomChoice()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var control = state.Facility.Rooms["control"];
        control.TemperatureC = 31;

        var sealedDoor = state.Facility.FindDoorBetween("engineering", "hall-engineering")!;
        sealedDoor.IsOpen = false;
        sealedDoor.IsLocked = true;

        var prompt = NpcPromptBuilder.Build(david, state);

        Assert.Contains("control = Control Room", prompt);
        Assert.Contains("DANGER", prompt);
        Assert.Contains("engineering = Engineering", prompt);
        Assert.Contains("route sealed", prompt);
        Assert.Contains("survival should normally override", prompt);
        Assert.Contains("ForceDoor", prompt);
        Assert.Contains("RestoreSystem", prompt);
        Assert.Contains("MAIN TRAITS", prompt);
    }

    [Fact]
    public async Task FallbackMind_UsesTheSharedRepairSkillFormulaForDoorRepairDecisions()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        david.Skills["Engineering"] = 100;
        david.Skills["Electrical"] = 0;
        david.Skills["Operations"] = 0;
        david.Skills["Reactor"] = 0;
        david.Traits.Clear();
        david.Traits.Add(new CrewTrait(
            "Test Rig",
            "Deliberately unbalanced for this test.",
            [
                new CrewTraitEffect(TraitEffectKind.Technical, 40),
                new CrewTraitEffect(TraitEffectKind.Repair, -70)
            ]));

        // CrewCounterplaySystem.BestRepairSkill clamps the technical component to 120
        // before applying the repair modifier: clamp(100 + 40, 0, 120) - 70 = 50.
        // A naive single-sum formula (100 + 40 - 70 = 70) would cross the >= 55 repair
        // threshold below and choose RepairDoor; the converged formula must not.
        Assert.Equal(50, CrewCounterplaySystem.BestRepairSkill(david));

        var door = state.Facility.FindDoorBetween("control", "hall-control")!;
        door.IsDamaged = true;

        var intent = await new RuleBasedAiDecisionService().DecideAsync(david, state);

        Assert.NotEqual(ActionKind.RepairDoor, intent.Action);
    }

    [Fact]
    public async Task ModelCanChooseAdjacentBlockedDoorCounterplay()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var door = state.Facility.FindDoorBetween("control", "hall-control")!;
        door.IsOpen = false;
        door.IsLocked = true;

        var json = """
            {
              "Action": "ForceDoor",
              "TargetId": "__DOOR_ID__",
              "Goal": "Get this hatch open.",
              "Reason": "I need to get through despite the lock.",
              "Urgency": 88
            }
            """.Replace("__DOOR_ID__", door.Id, StringComparison.Ordinal);

        using var client = new StubChatClient(json);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.ForceDoor, intent.Action);
        Assert.Equal(door.Id, intent.TargetId);
    }

    [Fact]
    public async Task ModelCanChooseToRestoreActuallyDisabledSystem()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        state.Facility.Rooms["control"].LightsOn = false;

        using var client = new StubChatClient(
            """
            {
              "Action": "RestoreSystem",
              "TargetId": "control",
              "Goal": "Restore the control room systems.",
              "Reason": "The outage is interfering with station operations.",
              "Urgency": 72
            }
            """);

        var service = new OllamaAiDecisionService(
            client,
            new RuleBasedAiDecisionService());

        var intent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.RestoreSystem, intent.Action);
        Assert.Equal("control", intent.TargetId);
    }

    [Fact]
    public void Prompt_MissingCrewConcernDoesNotLeakDeathOrEjectionTruth()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        marcus.Health = 0;
        marcus.IsPresent = false;
        marcus.CauseOfDeath = "Lost to space; no body remains aboard.";

        sarah.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            ExpectedRoomId = "quarters",
            FirstConcernAt = TimeSpan.FromMinutes(30),
            LastUpdatedAt = TimeSpan.FromMinutes(30),
            Stage = MissingPersonConcernStage.Concerned
        };

        var prompt = NpcPromptBuilder.Build(sarah, state);

        Assert.Contains("MISSING-PERSON CONCERNS", prompt);
        Assert.Contains(marcus.Name, prompt);
        Assert.Contains("KNOWN CREW ROSTER", prompt);
        Assert.DoesNotContain("Lost to space", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no body remains aboard", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FallbackMind_InvestigatesAKnownMissingPersonConcern()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        sarah.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            ExpectedRoomId = "quarters",
            FirstConcernAt = TimeSpan.FromMinutes(30),
            LastUpdatedAt = TimeSpan.FromMinutes(30),
            Stage = MissingPersonConcernStage.Searching
        };

        var intent = await new RuleBasedAiDecisionService().DecideAsync(sarah, state);

        Assert.Equal(ActionKind.Investigate, intent.Action);
        Assert.Equal("quarters", intent.TargetId);
        Assert.Contains(marcus.Name, intent.Goal);
    }

    [Fact]
    public void Prompt_ExposesOnlyNearbyAirlockSafetyState()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var airlock = state.Facility.Rooms["airlock"];

        airlock.AirlockSafetyInterlocksEnabled = false;
        david.CurrentRoomId = "hall-airlock";

        var nearbyPrompt = NpcPromptBuilder.Build(david, state);

        Assert.Contains("NEARBY AIRLOCK SAFETY PANELS", nearbyPrompt);
        Assert.Contains("NEEDS SECURING", nearbyPrompt);
        Assert.Contains("SecureAirlock", nearbyPrompt);
        Assert.Contains("BYPASSED", nearbyPrompt);

        david.CurrentRoomId = "control";
        var distantPrompt = NpcPromptBuilder.Build(david, state);

        Assert.Contains(
            "- none currently visible from here",
            distantPrompt);
        Assert.DoesNotContain(
            "airlock = Airlock | pressure",
            distantPrompt,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ModelCanChooseSecureAirlockOnlyFromGroundedNearbyPanel()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var airlock = state.Facility.Rooms["airlock"];

        airlock.AirlockSafetyInterlocksEnabled = false;
        david.CurrentRoomId = "hall-airlock";

        using var nearbyClient = new StubChatClient(
            """
            {
              "Action": "SecureAirlock",
              "TargetId": "airlock",
              "Goal": "Secure the compromised airlock.",
              "Reason": "The safety interlocks are bypassed and I am at the emergency controls.",
              "Urgency": 96
            }
            """);

        var nearbyService = new OllamaAiDecisionService(
            nearbyClient,
            new RuleBasedAiDecisionService());

        var nearbyIntent = await nearbyService.DecideAsync(david, state);

        Assert.Equal(ActionKind.SecureAirlock, nearbyIntent.Action);
        Assert.Equal("airlock", nearbyIntent.TargetId);

        david.CurrentRoomId = "control";

        using var distantClient = new StubChatClient(
            """
            {
              "Action": "SecureAirlock",
              "TargetId": "airlock",
              "Goal": "Secure the compromised airlock remotely.",
              "Reason": "I somehow know it is unsafe.",
              "Urgency": 96
            }
            """);

        var distantService = new OllamaAiDecisionService(
            distantClient,
            new RuleBasedAiDecisionService());

        var distantIntent = await distantService.DecideAsync(david, state);

        Assert.Equal(ActionKind.Idle, distantIntent.Action);
        Assert.Null(distantIntent.TargetId);
    }

    [Fact]
    public async Task FallbackMind_UsesTheSharedElevatedHungerThresholdMatchingBrowserMind()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        // BrowserMindSystem's elevated-hunger tier fires at 58; the fallback
        // ladder used to require 62, so a value in between the two used to
        // silently disagree about whether eating was already the priority.
        david.Hunger = CrewNeedThresholds.HungerElevated;

        var intent = await new RuleBasedAiDecisionService().DecideAsync(david, state);

        Assert.Equal(ActionKind.Eat, intent.Action);
    }

    [Fact]
    public async Task FallbackMind_TreatsCriticalHungerAsAnEmergencyThatSupersedesTechnicalWorkMatchingBrowserMind()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");

        david.Hunger = CrewNeedThresholds.HungerCritical + 3;
        state.LifeSupport.IsOnline = false;
        david.Skills["Engineering"] = 100;
        david.Skills["Electrical"] = 100;
        david.Skills["Operations"] = 100;
        david.Skills["Reactor"] = 100;

        // Without the critical-hunger branch (which BrowserMindSystem already
        // checks before any technical work), the ladder would otherwise pick
        // RestoreSystem here: repair skill is comfortably above the threshold.
        Assert.True(CrewCounterplaySystem.BestRepairSkill(david) >= 55);

        var intent = await new RuleBasedAiDecisionService().DecideAsync(david, state);

        Assert.Equal(ActionKind.Eat, intent.Action);
    }

    [Fact]
    public async Task FallbackMind_UsesTheSharedResentmentThresholdMatchingBrowserMind()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");

        // BrowserMindSystem's argue threshold fires at 48; the fallback ladder
        // used to require 55, so a resentment value in between the two used
        // to silently disagree about whether confronting them was warranted.
        david.Relationships[sarah.Name] = new Relationship
        {
            PersonName = sarah.Name,
            Resentment = CrewNeedThresholds.ResentmentArgue
        };

        var intent = await new RuleBasedAiDecisionService().DecideAsync(david, state);

        Assert.Equal(ActionKind.Argue, intent.Action);
        Assert.Equal(sarah.Name, intent.TargetId);
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly Queue<string>? _jsonResponses;
        private readonly Exception? _exception;
        private readonly object? _rawRepresentation;
        private string? _lastJson;

        public int CallCount { get; private set; }

        public List<ChatOptions?> CapturedOptions { get; } = [];

        public StubChatClient(string json, object? rawRepresentation = null)
            : this([json], rawRepresentation)
        {
        }

        public StubChatClient(IEnumerable<string> jsonResponses, object? rawRepresentation = null)
        {
            _jsonResponses = new Queue<string>(jsonResponses);
            _rawRepresentation = rawRepresentation;
        }

        public StubChatClient(Exception exception) => _exception = exception;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CapturedOptions.Add(options);

            if (_exception is not null)
            {
                return Task.FromException<ChatResponse>(_exception);
            }

            // Repeats the final queued response for any call beyond the
            // number of responses supplied, so a test can assert exactly
            // how many attempts a retry made without pre-sizing the queue.
            _lastJson = _jsonResponses!.Count > 0 ? _jsonResponses.Dequeue() : _lastJson;

            return Task.FromResult(
                new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, _lastJson!))
                {
                    RawRepresentation = _rawRepresentation
                });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
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
