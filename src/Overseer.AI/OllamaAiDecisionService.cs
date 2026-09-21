using System.Text.Json;
using Microsoft.Extensions.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.AI;

public sealed class OllamaAiDecisionService(
    IChatClient chatClient,
    RuleBasedAiDecisionService fallback) : IAiDecisionService
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly RuleBasedAiDecisionService _fallback = fallback;

    private static readonly HashSet<ActionKind> AllowedActions =
    [
        ActionKind.Idle,
        ActionKind.Move,
        ActionKind.Rest,
        ActionKind.Sleep,
        ActionKind.Eat,
        ActionKind.Recreate,
        ActionKind.Groom,
        ActionKind.Shower,
        ActionKind.UseToilet,
        ActionKind.Work,
        ActionKind.Investigate,
        ActionKind.Repair,
        ActionKind.Talk,
        ActionKind.Socialize,
        ActionKind.Argue,
        ActionKind.RequestHelp,
        ActionKind.RecruitShutdownAlly,
        ActionKind.JoinShutdownTeam,
        ActionKind.ShutdownOverseer,
        ActionKind.ForceDoor,
        ActionKind.RestoreSystem,
        ActionKind.SecureAirlock,
        ActionKind.RepairDoor,
        ActionKind.WeldDoor,
        ActionKind.BarricadeDoor,
        ActionKind.ShutdownRobot,
        ActionKind.IsolateRobotNetwork,
        ActionKind.DisableRobotCharging,
        ActionKind.DamageRobot,
        ActionKind.ReprogramRobot,
        ActionKind.DisarmTurret,
        ActionKind.IsolateTurretNetwork,
        ActionKind.DisableTurretPower,
        ActionKind.DamageTurret,
        ActionKind.ReprogramTurret
    ];

    public async Task<NpcIntent> DecideAsync(
        Npc npc,
        GameState state,
        CancellationToken cancellationToken = default)
    {
        string? prompt = null;
        string? rawResponse = null;

        try
        {
            prompt = NpcPromptBuilder.Build(npc, state);

            var response = await _chatClient.GetResponseAsync<NpcMindDecision>(
                prompt,
                options: new ChatOptions
                {
                    Temperature = 0.7f,
                    MaxOutputTokens = 240
                },
                useJsonSchemaResponseFormat: true,
                cancellationToken: cancellationToken);

            rawResponse = response.Text;
            NpcMindDecision? decision = null;

            if (!response.TryGetResult(out decision) || decision is null)
            {
                decision = TryParse(rawResponse);
            }

            if (decision is null)
            {
                throw new InvalidOperationException(
                    "The model did not return a valid structured decision.");
            }

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                rawResponse = JsonSerializer.Serialize(decision);
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
            || !AllowedActions.Contains(action))
        {
            action = ActionKind.Idle;
        }

        string? target = decision.TargetId?.Trim();

        if (action is ActionKind.Move or ActionKind.Investigate or ActionKind.Repair or ActionKind.Work)
        {
            if (target is null || !state.Facility.Rooms.ContainsKey(target))
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
        else if (action is ActionKind.Talk
            or ActionKind.Socialize
            or ActionKind.Argue
            or ActionKind.RequestHelp
            or ActionKind.RecruitShutdownAlly)
        {
            var person = state.Crew.FirstOrDefault(other =>
                other.IsAlive
                && other.IsPresent
                && other.Id != npc.Id
                && other.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (person is null
                || (action == ActionKind.RecruitShutdownAlly
                    && (npc.OverseerSuspicion < 65
                        || npc.KnownShutdownMechanismIds.Count == 0)))
            {
                action = ActionKind.Idle;
                target = null;
            }
            else
            {
                target = person.Name;
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

        return new NpcIntent(
            action,
            target,
            goal,
            reason,
            Math.Clamp(decision.Urgency, 0, 100),
            "Ollama",
            state.Elapsed);
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
