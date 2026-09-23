using System.Text.Json;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.AI;

public sealed class OllamaAiDecisionService(
    IChatClient chatClient,
    RuleBasedAiDecisionService fallback) : IAiDecisionService
{
    // NpcPromptBuilder emits every action plus every room's atmosphere, which
    // runs close to Ollama's 2048-token default context window; a model would
    // silently drop the rules at the top of the prompt without this.
    private const int ContextWindowTokens = 8192;

    private const string RetryInstruction = """


        RETRY: your previous response could not be parsed as the required JSON decision schema. Respond again with ONLY a single JSON object matching the schema, no other text.
        """;

    private readonly IChatClient _chatClient = chatClient;
    private readonly RuleBasedAiDecisionService _fallback = fallback;

    public async Task<NpcIntent> DecideAsync(
        Npc npc,
        GameState state,
        CancellationToken cancellationToken = default)
    {
        string? prompt = null;
        string? rawResponse = null;

        try
        {
            var modelPrompt = NpcPromptBuilder.Build(npc, state);
            var options = new ChatOptions
            {
                Temperature = 0.7f,
                MaxOutputTokens = 240
            }.AddOllamaOption(OllamaOption.NumCtx, ContextWindowTokens);

            var attempt = await RequestDecisionAsync(
                modelPrompt,
                options,
                cancellationToken);
            prompt = attempt.PromptTrace;
            rawResponse = attempt.RawResponse;
            var decision = attempt.Decision;

            if (decision is null)
            {
                // Invalid/unparseable output is usually a one-off formatting
                // slip; one corrective retry recovers most of these instead of
                // silently collapsing straight to Idle/fallback.
                var retry = await RequestDecisionAsync(
                    modelPrompt + RetryInstruction,
                    options,
                    cancellationToken);
                prompt = retry.PromptTrace;
                rawResponse = retry.RawResponse;
                decision = retry.Decision;
            }

            if (decision is null)
            {
                throw new InvalidOperationException(
                    "The model did not return a valid structured decision after a retry.");
            }

            var intent = Validate(npc, state, decision);
            CognitionTelemetrySystem.Record(
                state,
                npc,
                "Ollama",
                intent,
                prompt,
                rawResponse);

            return intent;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            CognitionTelemetrySystem.Record(
                state,
                npc,
                "Ollama",
                intent: null,
                prompt,
                rawResponse,
                $"MODEL/FALLBACK: {exception.GetType().Name}: {exception.Message}");

            return await _fallback.DecideAsync(npc, state, cancellationToken);
        }
    }

    private async Task<(NpcMindDecision? Decision, string PromptTrace, string? RawResponse)> RequestDecisionAsync(
        string modelPrompt,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        // Keep the exact prompt that crosses the IChatClient boundary,
        // together with the options used for the call, so local Ollama play
        // can be debugged from /debug without guessing what the model saw.
        var promptTrace = BuildRequestTrace(modelPrompt, options);

        var response = await _chatClient.GetResponseAsync<NpcMindDecision>(
            modelPrompt,
            options: options,
            useJsonSchemaResponseFormat: true,
            cancellationToken: cancellationToken);

        var responseText = response.Text;
        var rawResponse = BuildRawResponseTrace(
            response.RawRepresentation,
            responseText);

        if (!response.TryGetResult(out var decision) || decision is null)
        {
            decision = TryParse(responseText);
        }

        return (decision, promptTrace, rawResponse);
    }

    private static string BuildRequestTrace(
        string modelPrompt,
        ChatOptions options) =>
        $"""
        ICHATCLIENT REQUEST OPTIONS
        temperature: {options.Temperature}
        max_output_tokens: {options.MaxOutputTokens}
        num_ctx: {ContextWindowTokens}
        response_format: json-schema (NpcMindDecision)

        EXACT PROMPT SENT TO OLLAMA
        {modelPrompt}
        """;

    private static string? BuildRawResponseTrace(
        object? rawRepresentation,
        string? responseText)
    {
        if (rawRepresentation is null)
        {
            return responseText;
        }

        string raw;
        try
        {
            raw = rawRepresentation is string text
                ? text
                : JsonSerializer.Serialize(
                    rawRepresentation,
                    rawRepresentation.GetType(),
                    new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception)
        {
            raw = rawRepresentation.ToString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(responseText)
            || raw.Contains(responseText, StringComparison.Ordinal))
        {
            return raw;
        }

        return $"""
        RAW PROVIDER RESPONSE ({rawRepresentation.GetType().FullName})
        {raw}

        ICHATCLIENT RESPONSE TEXT
        {responseText}
        """;
    }

    private static NpcMindDecision? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<NpcMindDecision>(
                text,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NpcIntent Validate(
        Npc npc,
        GameState state,
        NpcMindDecision decision)
    {
        if (!Enum.TryParse<ActionKind>(decision.Action, true, out var action)
            || !CrewAffordanceSystem.IsCognitionAction(action))
        {
            action = ActionKind.Idle;
        }

        string? target = decision.TargetId?.Trim();

        if (CrewAffordanceSystem.IsRoomTarget(action)
            || CrewAffordanceSystem.IsCrewTarget(action)
            || CrewAffordanceSystem.IsDoorOperation(action))
        {
            if (!CrewAffordanceSystem.TryNormalizeTarget(
                    state,
                    npc,
                    action,
                    target,
                    out target))
            {
                action = ActionKind.Idle;
                target = null;
            }
        }
        else if (action == ActionKind.ForceDoor)
        {
            var door = state.Facility.Doors.FirstOrDefault(candidate =>
                candidate.Id.Equals(target, StringComparison.OrdinalIgnoreCase)
                && !candidate.IsPassable
                && candidate.CanBeForced
                && (candidate.RoomAId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                    || candidate.RoomBId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)));

            if (door is null)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = door.Id;
            }
        }
        else if (action is ActionKind.RepairDoor or ActionKind.WeldDoor or ActionKind.BarricadeDoor)
        {
            var door = state.Facility.Doors.FirstOrDefault(candidate =>
                candidate.Id.Equals(target, StringComparison.OrdinalIgnoreCase)
                && (candidate.RoomAId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                    || candidate.RoomBId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)));
            if (door is null)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = door.Id;
            }
        }
        else if (action == ActionKind.RestoreSystem)
        {
            if (target is null || !IsRestorableTarget(state, target))
            {
                action = ActionKind.Idle;
                target = null;
            }
        }
        else if (action == ActionKind.SecureAirlock)
        {
            if (target is null
                || !state.Facility.Rooms.TryGetValue(target, out var airlock)
                || airlock.Type != RoomType.Airlock
                || !airlock.HasExteriorHatch
                || !AirlockSafetyRules.NeedsCrewSecuring(state, airlock)
                || !AirlockSafetyRules.CanCrewSecure(npc)
                || !AirlockSafetyRules.CanPerceiveSafetyState(
                    state,
                    npc,
                    airlock))
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = airlock.Id;
            }
        }
        else if (action == ActionKind.JoinShutdownTeam)
        {
            var invitation = npc.PendingShutdownTeamInvitation;
            if (invitation is null
                || target is null
                || !invitation.TeamId.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = invitation.TeamId;
            }
        }
        else if (action == ActionKind.AcceptPact)
        {
            var proposal = npc.PendingPactProposal;
            if (proposal is null
                || target is null
                || !proposal.FromNpcName.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = proposal.FromNpcName;
            }
        }
        else if (action is ActionKind.FulfillPact or ActionKind.BreakPact)
        {
            var pact = target is null
                ? null
                : CrewPactSystem.ActiveFor(state, npc.Id).FirstOrDefault(candidate =>
                    candidate.Id.Equals(target, StringComparison.OrdinalIgnoreCase)
                    && candidate.PromisorId == npc.Id);

            if (pact is null)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = pact.Id;
            }
        }
        else if (action is ActionKind.ShutdownRobot
            or ActionKind.DamageRobot
            or ActionKind.ReprogramRobot
            or ActionKind.IsolateRobotNetwork
            or ActionKind.DisableRobotCharging)
        {
            var robot = RobotCountermeasureSystem.FindRobot(state, target);
            var hasThreatEvidence = robot is not null
                && RobotCountermeasureSystem.HasHostileRobotEvidence(npc, robot);

            if (robot is null || robot.IsDestroyed)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else if (action is ActionKind.ShutdownRobot or ActionKind.DamageRobot)
            {
                if (!RobotCountermeasureSystem.IsCoLocated(npc, robot)
                    || robot.Policy != RobotPolicy.Hostile
                    || !robot.IsOperational)
                {
                    action = ActionKind.Idle;
                    target = null;
                }
                else
                {
                    target = robot.Id;
                }
            }
            else if (action == ActionKind.ReprogramRobot)
            {
                if (!RobotCountermeasureSystem.IsCoLocated(npc, robot)
                    || robot.IsOperational
                    || robot.Policy != RobotPolicy.Hostile)
                {
                    action = ActionKind.Idle;
                    target = null;
                }
                else
                {
                    target = robot.Id;
                }
            }
            else if (!hasThreatEvidence)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = robot.Id;
            }
        }
        else if (action is ActionKind.DisarmTurret
            or ActionKind.DamageTurret
            or ActionKind.ReprogramTurret
            or ActionKind.IsolateTurretNetwork
            or ActionKind.DisableTurretPower)
        {
            var turret = TurretCountermeasureSystem.FindTurret(state, target);
            var hasThreatEvidence = turret is not null
                && TurretCountermeasureSystem.HasHostileTurretEvidence(npc, turret);

            if (turret is null || turret.IsDestroyed)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else if (action is ActionKind.DisarmTurret or ActionKind.DamageTurret)
            {
                if (!TurretCountermeasureSystem.IsCoLocated(npc, turret)
                    || !turret.IsArmed
                    || turret.Policy == TurretPolicy.Safe)
                {
                    action = ActionKind.Idle;
                    target = null;
                }
                else
                {
                    target = turret.Id;
                }
            }
            else if (action == ActionKind.ReprogramTurret)
            {
                if (!TurretCountermeasureSystem.IsCoLocated(npc, turret)
                    || turret.IsArmed
                    || turret.Policy == TurretPolicy.Safe)
                {
                    action = ActionKind.Idle;
                    target = null;
                }
                else
                {
                    target = turret.Id;
                }
            }
            else if (!hasThreatEvidence)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = turret.Id;
            }
        }
        else if (action is ActionKind.IsolateSecurityController
            or ActionKind.PurgeSecurityController)
        {
            var allowed = action switch
            {
                ActionKind.IsolateSecurityController =>
                    SecurityMalwareSystem.CanIsolate(state, npc),
                ActionKind.PurgeSecurityController =>
                    SecurityMalwareSystem.CanPurge(state, npc),
                _ => false
            };

            if (!allowed)
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = SecurityMalwareSystem.ControllerTargetId;
            }
        }
        else if (action == ActionKind.ShutdownOverseer)
        {
            var mechanism = state.ShutdownMechanisms.FirstOrDefault(candidate =>
                candidate.IsOnline
                && candidate.Id.Equals(target, StringComparison.OrdinalIgnoreCase)
                && npc.KnownShutdownMechanismIds.Contains(candidate.Id));

            var team = mechanism is null
                ? null
                : state.ShutdownTeams.FirstOrDefault(candidate =>
                    candidate.IsActive
                    && candidate.MechanismId.Equals(
                        mechanism.Id,
                        StringComparison.OrdinalIgnoreCase)
                    && candidate.MemberIds.Contains(npc.Id));

            if (mechanism is null
                || npc.OverseerSuspicion < 65
                || mechanism.RequiredCrewCount > 1
                    && (team is null
                        || team.MemberIds.Count < mechanism.RequiredCrewCount))
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = mechanism.Id;
            }
        }
        else
        {
            target = null;
        }

        var goal = Clean(decision.Goal, "Decide what to do next.");
        var reason = Clean(decision.Reason, "I need a moment to decide what matters.");

        // Captured now rather than re-derived on arrival: the asker may walk
        // several ticks to reach the askee, during which a different concern
        // could become more pressing and silently swap the subject.
        var subjectId = action == ActionKind.AskAboutLocation
            ? MissingPersonSystem.MostPressingAskableConcern(npc)?.PersonId
            : null;

        return new NpcIntent(
            action,
            target,
            goal,
            reason,
            Math.Clamp(decision.Urgency, 0, 100),
            "Ollama",
            state.Elapsed,
            subjectId);
    }

    private static bool IsRestorableTarget(GameState state, string target)
    {
        if (target.Equals("life-support", StringComparison.OrdinalIgnoreCase))
        {
            return !state.LifeSupport.IsOnline;
        }

        return state.Facility.Rooms.TryGetValue(target, out var room)
            && (!room.IsPowered
                || !room.CameraOnline
                || !room.LightsOn
                || (room.HasTemperatureControl && !room.TemperatureControlOnline)
                || (room.HasVentilationControl && !room.VentilationEnabled));
    }

    private static string Clean(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var clean = value.Trim();
        return clean.Length <= 220 ? clean : clean[..220];
    }
}
