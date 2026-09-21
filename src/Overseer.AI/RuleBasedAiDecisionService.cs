using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.AI;

public sealed class RuleBasedAiDecisionService : IAiDecisionService
{
    public Task<NpcIntent> DecideAsync(
        Npc npc,
        GameState state,
        CancellationToken cancellationToken = default)
    {
        NpcIntent intent;
        var room = state.Facility.Rooms[npc.CurrentRoomId];

        if (CrewEnvironmentSafety.IsDangerous(room))
        {
            var saferRoom = FindSaferRoom(state, npc, room);

            if (saferRoom is not null)
            {
                intent = Create(
                    npc,
                    state,
                    ActionKind.Move,
                    saferRoom.Id,
                    $"Get to {saferRoom.Name}.",
                    "The atmosphere or temperature here is becoming dangerous.",
                    96);
            }
            else if (FindAdjacentBlockedDoor(state, npc) is { } blockedDoor
                && BestCounterplayScore(npc) >= 50)
            {
                intent = Create(
                    npc,
                    state,
                    ActionKind.ForceDoor,
                    blockedDoor.Id,
                    $"Get {blockedDoor.Id} open.",
                    "A blocked hatch may be the only route out of this dangerous area.",
                    98);
            }
            else
            {
                intent = Create(
                    npc,
                    state,
                    ActionKind.Idle,
                    null,
                    "Shelter and call for emergency help.",
                    "The environment is dangerous and I cannot identify a safer reachable room.",
                    98);
            }
        }
        else if (FindSecurityMalwareResponse(state, npc) is { } malwareResponse)
        {
            intent = malwareResponse;
        }
        else if (FindTurretCountermeasure(state, npc) is { } turretCountermeasure)
        {
            intent = turretCountermeasure;
        }
        else if (FindRobotCountermeasure(state, npc) is { } robotCountermeasure)
        {
            intent = robotCountermeasure;
        }
        else if (FindPerceivedUnsafeAirlock(state, npc) is { } unsafeAirlock)
        {
            intent = Create(
                npc,
                state,
                ActionKind.SecureAirlock,
                unsafeAirlock.Id,
                $"Secure {unsafeAirlock.Name}.",
                "I can see the airlock safety state is compromised and I know the emergency controls.",
                94);
        }
        else if (FindAdjacentDamagedDoor(state, npc) is { } damagedDoor && BestRepairScore(npc) >= 55)
        {
            intent = Create(npc, state, ActionKind.RepairDoor, damagedDoor.Id,
                $"Repair {damagedDoor.Id}.",
                "This hatch has visible structural or bypass damage and I can repair it locally.", 72);
        }
        else if (!state.LifeSupport.IsOnline && BestRepairScore(npc) >= 55)
        {
            intent = Create(
                npc,
                state,
                ActionKind.RestoreSystem,
                "life-support",
                "Restore primary life support.",
                "The crew need life support and I have enough technical ability to try.",
                88);
        }
        // Basic survival comes before curiosity, matching BrowserMindSystem.
        // With investigation first, a suspicious crew member would investigate
        // while starving and oscillate against the routine steering them to food.
        else if (npc.Hunger >= 62)
        {
            intent = Create(npc, state, ActionKind.Eat, null,
                "Get something to eat.",
                "I am hungry enough that food is becoming difficult to ignore.",
                80);
        }
        else if (npc.Fatigue >= 72)
        {
            intent = Create(npc, state, ActionKind.Sleep, null,
                "Get some sleep.",
                "I am too tired to keep working effectively.",
                78);
        }
        else if (npc.BladderNeed >= 72)
        {
            intent = Create(npc, state, ActionKind.UseToilet, null,
                "Use the washroom.",
                "I need the toilet and should deal with that now.",
                84);
        }
        else if (MostPressingMissingConcern(npc) is { } missingConcern
            && FindMissingSearchRoom(state, npc, missingConcern) is { } searchRoom)
        {
            intent = Create(
                npc,
                state,
                ActionKind.Investigate,
                searchRoom.Id,
                $"Look for {missingConcern.PersonName} in {searchRoom.Name}.",
                MissingConcernReason(state, missingConcern),
                missingConcern.Stage == MissingPersonConcernStage.Escalated ? 80 : 52);
        }
        else if (npc.PendingShutdownTeamInvitation is { } invitation
            && ShouldJoinShutdownTeam(npc, invitation))
        {
            intent = Create(
                npc,
                state,
                ActionKind.JoinShutdownTeam,
                invitation.TeamId,
                "Join the proposed Overseer isolation team.",
                $"{invitation.FromNpcName} asked for coordinated help and I take the claim seriously enough to join.",
                90);
        }
        else if (npc.OverseerSuspicion >= 38
            && FindInvestigationLead(state, npc) is { } investigationLead)
        {
            var leadRoom = state.Facility.Rooms[investigationLead.RoomId];
            intent = Create(
                npc,
                state,
                ActionKind.Investigate,
                leadRoom.Id,
                $"Investigate {leadRoom.Name}.",
                investigationLead.Description,
                npc.OverseerSuspicion >= 60 ? 91 : 74);
        }
        else if (npc.OverseerSuspicion >= 65
            && FindKnownShutdownMechanism(state, npc) is { } knownMechanism)
        {
            var team = FindShutdownTeam(state, npc, knownMechanism);

            if (team is null || team.MemberIds.Count < knownMechanism.RequiredCrewCount)
            {
                var recruit = FindShutdownRecruit(state, npc, team);
                intent = recruit is null
                    ? Create(
                        npc,
                        state,
                        ActionKind.Idle,
                        null,
                        "Wait for a trustworthy opportunity to coordinate.",
                        "I know where the isolation hardware is, but I do not yet have a viable team.",
                        86)
                    : Create(
                        npc,
                        state,
                        ActionKind.RecruitShutdownAlly,
                        recruit.Name,
                        $"Recruit {recruit.Name} to help isolate Overseer.",
                        $"I verified {knownMechanism.Label}, but operating it safely requires coordinated crew.",
                        94);
            }
            else
            {
                intent = Create(
                    npc,
                    state,
                    ActionKind.ShutdownOverseer,
                    knownMechanism.Id,
                    $"Reach {knownMechanism.Label} with the team and isolate Overseer.",
                    "I verified the hardware and enough crew have committed to the same plan.",
                    98);
            }
        }
        else if (HasLocalRestorableProblem(room) && BestRepairScore(npc) >= 55)
        {
            intent = Create(
                npc,
                state,
                ActionKind.RestoreSystem,
                room.Id,
                $"Restore {room.Name}.",
                "A local system is disabled and I can probably bring it back.",
                62);
        }
        else if (npc.HygieneNeed >= 65)
        {
            intent = Create(npc, state, ActionKind.Shower, null,
                "Take a shower.",
                "I need to clean up before I can comfortably focus.",
                66);
        }
        else if (npc.RecreationNeed >= 62)
        {
            intent = Create(npc, state, ActionKind.Recreate, null,
                "Take a break.",
                "I need some recreation before I burn out.",
                52);
        }
        else
        {
            var worstRelationship = npc.Relationships.Values
                .OrderByDescending(r => r.Resentment)
                .FirstOrDefault();

            if (worstRelationship is { Resentment: >= 55 })
            {
                intent = Create(npc, state, ActionKind.Argue, worstRelationship.PersonName,
                    $"Confront {worstRelationship.PersonName}.",
                    $"My resentment toward {worstRelationship.PersonName} has been building.",
                    65);
            }
            else
            {
                var bestRelationship = npc.Relationships.Values
                    .OrderByDescending(r => r.Trust + r.Affinity)
                    .FirstOrDefault();

                if (bestRelationship is not null
                    && npc.Personality.Sociability >= 55
                    && npc.SocialNeed >= 68)
                {
                    intent = Create(npc, state, ActionKind.Socialize, bestRelationship.PersonName,
                        $"Spend time with {bestRelationship.PersonName}.",
                        $"I trust {bestRelationship.PersonName} and would rather not be alone.",
                        38);
                }
                else
                {
                    intent = Create(npc, state, ActionKind.Idle, null,
                        "Keep an eye on things.",
                        "Nothing feels urgent enough to justify changing what I am doing.",
                        20);
                }
            }
        }

        CognitionTelemetrySystem.Record(
            state,
            npc,
            "Rule-based AI",
            intent);

        return Task.FromResult(intent);
    }

    private static NpcIntent? FindSecurityMalwareResponse(GameState state, Npc npc)
    {
        if (!state.SecurityMalware.IsActive
            || !SecurityMalwareSystem.HasMalwareEvidence(npc))
        {
            return null;
        }

        var technical = CrewCounterplaySystem.BestTechnicalSkill(npc);
        var controller = state.Facility.Rooms[SecurityMalwareSystem.ControllerRoomId];

        if (state.SecurityMalware.Stage == SecurityMalwareStage.Active)
        {
            if (SecurityMalwareSystem.CanIsolate(state, npc))
            {
                return Create(
                    npc,
                    state,
                    ActionKind.IsolateSecurityController,
                    SecurityMalwareSystem.ControllerTargetId,
                    "Physically isolate the compromised MR/ST security controller.",
                    "I physically diagnosed malicious MR/ST commands and need to contain their controller before cleanup.",
                    99);
            }

            if (technical >= 55
                && !npc.CurrentRoomId.Equals(
                    SecurityMalwareSystem.ControllerRoomId,
                    StringComparison.OrdinalIgnoreCase)
                && ReachableRooms(state, npc, npc.CurrentRoomId)
                    .Contains(SecurityMalwareSystem.ControllerRoomId))
            {
                return Create(
                    npc,
                    state,
                    ActionKind.Move,
                    controller.Id,
                    $"Reach {controller.Name} to diagnose and contain the security-controller anomaly.",
                    "I witnessed suspicious MR/ST control behaviour and need physical controller access before assuming its cause or scope.",
                    96);
            }
        }

        if (state.SecurityMalware.Stage == SecurityMalwareStage.Isolated)
        {
            if (SecurityMalwareSystem.CanPurge(state, npc))
            {
                return Create(
                    npc,
                    state,
                    ActionKind.PurgeSecurityController,
                    SecurityMalwareSystem.ControllerTargetId,
                    "Purge and reimage the isolated MR/ST security controller.",
                    "I diagnosed the compromise and the controller is physically isolated, so a clean local reimage is now appropriate.",
                    98);
            }

            if (technical >= 65
                && !npc.CurrentRoomId.Equals(
                    SecurityMalwareSystem.ControllerRoomId,
                    StringComparison.OrdinalIgnoreCase)
                && ReachableRooms(state, npc, npc.CurrentRoomId)
                    .Contains(SecurityMalwareSystem.ControllerRoomId))
            {
                return Create(
                    npc,
                    state,
                    ActionKind.Move,
                    controller.Id,
                    $"Reach {controller.Name} to finish security-controller recovery.",
                    "The incident is contained, but I need physical access to verify and reimage the controller.",
                    93);
            }
        }

        return null;
    }

    private static NpcIntent? FindTurretCountermeasure(GameState state, Npc npc)
    {
        var visibleTurret = state.Turrets.FirstOrDefault(turret =>
            !turret.IsDestroyed
            && turret.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

        if (visibleTurret is not null)
        {
            var technical = CrewCounterplaySystem.BestTechnicalSkill(npc);
            var force = CrewCounterplaySystem.BestForceSkill(npc);

            if (!visibleTurret.IsArmed
                && visibleTurret.Policy != TurretPolicy.Safe
                && technical >= 65)
            {
                return Create(
                    npc,
                    state,
                    ActionKind.ReprogramTurret,
                    visibleTurret.Id,
                    $"Reprogram {visibleTurret.Name} to safe targeting.",
                    "The security turret is disarmed in front of me and its local service controller is accessible.",
                    96);
            }

            if (visibleTurret.IsArmed
                && visibleTurret.Policy != TurretPolicy.Safe
                && TurretSystem.HasPower(state, visibleTurret))
            {
                return technical >= 45
                    ? Create(
                        npc,
                        state,
                        ActionKind.DisarmTurret,
                        visibleTurret.Id,
                        $"Use {visibleTurret.Name}'s local safing controls.",
                        "An armed hostile security turret is physically here; I want to safe it locally.",
                        100)
                    : force >= 40
                        ? Create(
                            npc,
                            state,
                            ActionKind.DamageTurret,
                            visibleTurret.Id,
                            $"Physically sabotage {visibleTurret.Name}.",
                            "The armed turret is an immediate local threat and physical sabotage is the countermeasure I can attempt.",
                            100)
                        : null;
            }
        }

        var knownThreat = state.Turrets.FirstOrDefault(turret =>
            !turret.IsDestroyed
            && TurretCountermeasureSystem.HasHostileTurretEvidence(npc, turret));

        if (knownThreat is null)
        {
            return null;
        }

        var technicalSkill = CrewCounterplaySystem.BestTechnicalSkill(npc);
        if (!knownThreat.IsNetworkIsolated && technicalSkill >= 55)
        {
            return Create(
                npc,
                state,
                ActionKind.IsolateTurretNetwork,
                knownThreat.Id,
                $"Isolate {knownThreat.Name} from Overseer's security network.",
                "I have direct evidence the turret is dangerous and Engineering has a physical network isolation control.",
                95);
        }

        if (knownThreat.PowerFeedEnabled && technicalSkill >= 45)
        {
            return Create(
                npc,
                state,
                ActionKind.DisableTurretPower,
                knownThreat.Id,
                $"Cut the dedicated power feed to {knownThreat.Name}.",
                "I have direct evidence the turret is dangerous and can deny its security power feed from Engineering.",
                92);
        }

        return null;
    }

    private static NpcIntent? FindRobotCountermeasure(GameState state, Npc npc)
    {
        var visibleRobot = state.Robots.FirstOrDefault(robot =>
            !robot.IsDestroyed
            && robot.CurrentRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

        if (visibleRobot is not null)
        {
            if (!visibleRobot.IsOperational
                && visibleRobot.Policy == RobotPolicy.Hostile
                && CrewCounterplaySystem.BestTechnicalSkill(npc) >= 65)
            {
                return Create(
                    npc,
                    state,
                    ActionKind.ReprogramRobot,
                    visibleRobot.Id,
                    $"Reprogram and reboot {visibleRobot.Name}.",
                    "The hostile robot is shut down in front of me and I can reach its local service port.",
                    96);
            }

            if (visibleRobot.Policy == RobotPolicy.Hostile && visibleRobot.IsOperational)
            {
                var technical = CrewCounterplaySystem.BestTechnicalSkill(npc);
                var force = CrewCounterplaySystem.BestForceSkill(npc);

                return technical >= 45
                    ? Create(
                        npc,
                        state,
                        ActionKind.ShutdownRobot,
                        visibleRobot.Id,
                        $"Use {visibleRobot.Name}'s manual shutdown.",
                        "A hostile robot is physically here; I want to stop it at the local emergency cutoff.",
                        100)
                    : Create(
                        npc,
                        state,
                        ActionKind.DamageRobot,
                        visibleRobot.Id,
                        $"Physically disable {visibleRobot.Name}.",
                        force >= 40
                            ? "A hostile robot is physically here and force is the countermeasure I can attempt."
                            : "The robot is an immediate threat; I have no safer technical option.",
                        100);
            }
        }

        var knownThreat = state.Robots.FirstOrDefault(robot =>
            !robot.IsDestroyed
            && RobotCountermeasureSystem.HasHostileRobotEvidence(npc, robot));

        if (knownThreat is null)
        {
            return null;
        }

        var technicalSkill = CrewCounterplaySystem.BestTechnicalSkill(npc);
        if (!knownThreat.IsNetworkIsolated && technicalSkill >= 55)
        {
            return Create(
                npc,
                state,
                ActionKind.IsolateRobotNetwork,
                knownThreat.Id,
                $"Isolate {knownThreat.Name} from Overseer's control link.",
                "I have direct evidence the robot is dangerous and Engineering has a physical network isolation control.",
                94);
        }

        if (knownThreat.ChargingEnabled && technicalSkill >= 45)
        {
            return Create(
                npc,
                state,
                ActionKind.DisableRobotCharging,
                knownThreat.Id,
                $"Cut power to {knownThreat.Name}'s charging circuit.",
                "I have direct evidence the robot is dangerous and can deny its charger from Engineering.",
                88);
        }

        return null;
    }

    private static bool ShouldJoinShutdownTeam(
        Npc npc,
        ShutdownTeamInvitation invitation)
    {
        var trust = npc.Relationships.TryGetValue(
            invitation.FromNpcName,
            out var relationship)
            ? relationship.Trust
            : 50;

        return npc.OverseerSuspicion >= 50 && trust >= 35;
    }

    private static InvestigationLead? FindInvestigationLead(
        GameState state,
        Npc npc)
    {
        var reachable = ReachableRooms(state, npc, npc.CurrentRoomId);
        return npc.InvestigationLeads.Values
            .Where(lead =>
                lead.Stage == InvestigationLeadStage.Open
                && state.Facility.Rooms.ContainsKey(lead.RoomId)
                && reachable.Contains(lead.RoomId))
            .OrderByDescending(lead =>
                lead.Id.Contains("team-claim", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(lead =>
                lead.Id.Contains("shutdown", StringComparison.OrdinalIgnoreCase))
            .ThenBy(lead => lead.CreatedAt)
            .FirstOrDefault();
    }

    private static ShutdownMechanism? FindKnownShutdownMechanism(
        GameState state,
        Npc npc) =>
        state.ShutdownMechanisms
            .Where(mechanism =>
                mechanism.IsOnline
                && npc.KnownShutdownMechanismIds.Contains(mechanism.Id))
            .OrderBy(mechanism => mechanism.Id)
            .FirstOrDefault();

    private static ShutdownTeam? FindShutdownTeam(
        GameState state,
        Npc npc,
        ShutdownMechanism mechanism) =>
        state.ShutdownTeams.FirstOrDefault(team =>
            team.IsActive
            && team.MechanismId.Equals(mechanism.Id, StringComparison.OrdinalIgnoreCase)
            && team.MemberIds.Contains(npc.Id));

    private static Npc? FindShutdownRecruit(
        GameState state,
        Npc npc,
        ShutdownTeam? team)
    {
        var excluded = team?.MemberIds ?? new HashSet<Guid>();
        return state.Crew
            .Where(other =>
                other.IsAlive
                && other.IsPresent
                && other.Id != npc.Id
                && !excluded.Contains(other.Id)
                && (team is null || !team.InvitedNpcIds.Contains(other.Id)))
            .OrderByDescending(other =>
                npc.Relationships.TryGetValue(other.Name, out var relation)
                    ? relation.Trust + relation.Affinity
                    : 100)
            .ThenBy(other => other.Name)
            .FirstOrDefault();
    }

    private static Room? FindPerceivedUnsafeAirlock(
        GameState state,
        Npc npc) =>
        state.Facility.Rooms.Values
            .Where(room =>
                room.Type == RoomType.Airlock
                && room.HasExteriorHatch
                && AirlockSafetyRules.NeedsCrewSecuring(state, room)
                && AirlockSafetyRules.CanCrewSecure(npc)
                && AirlockSafetyRules.CanPerceiveSafetyState(state, npc, room))
            .OrderBy(room => room.Id)
            .FirstOrDefault();

    private static MissingPersonConcern? MostPressingMissingConcern(Npc npc) =>
        npc.MissingPersonConcerns.Values
            .Where(concern => concern.Stage != MissingPersonConcernStage.Concerned)
            .OrderByDescending(concern => concern.Stage)
            .ThenBy(concern => concern.FirstConcernAt)
            .FirstOrDefault();

    private static Room? FindMissingSearchRoom(
        GameState state,
        Npc npc,
        MissingPersonConcern concern)
    {
        var reachable = ReachableRooms(state, npc, npc.CurrentRoomId);
        var candidateIds = new[]
        {
            concern.ExpectedRoomId,
            concern.LastKnownRoomId,
            "quarters",
            "kitchen",
            "lounge",
            "medical",
            "control"
        };

        foreach (var roomId in candidateIds
                     .Where(roomId => !string.IsNullOrWhiteSpace(roomId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (roomId is null
                || concern.CheckedRoomIds.Contains(roomId)
                || !reachable.Contains(roomId)
                || !state.Facility.Rooms.TryGetValue(roomId, out var room))
            {
                continue;
            }

            return room;
        }

        return null;
    }

    private static string MissingConcernReason(
        GameState state,
        MissingPersonConcern concern)
    {
        var expected = state.Facility.Rooms[concern.ExpectedRoomId].Name;

        return concern.LastSeenAt is { } seenAt
            ? $"I last saw {concern.PersonName} at T+{seenAt:hh\\:mm}; they missed expected duty around {expected}."
            : $"I have not seen {concern.PersonName} this shift and they missed expected duty around {expected}.";
    }

    private static Door? FindAdjacentDamagedDoor(GameState state, Npc npc) =>
        state.Facility.Doors.FirstOrDefault(door =>
            (door.IsDamaged || door.IsTechnicallyBypassed)
            && (door.RoomAId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                || door.RoomBId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)));

    // An ordinary closed hatch is opened while walking through, never forced.
    private static Door? FindAdjacentBlockedDoor(GameState state, Npc npc) =>
        state.Facility.Doors.FirstOrDefault(door =>
            !door.IsPassable
            && door.CanBeForced
            && !CrewDoorInteractionSystem.CanOpenForTraversal(state, npc, door)
            && (door.RoomAId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                || door.RoomBId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)));

    private static bool HasLocalRestorableProblem(Room room) =>
        !room.IsPowered
        || !room.CameraOnline
        || !room.LightsOn
        || (room.HasTemperatureControl && !room.TemperatureControlOnline)
        || (room.HasVentilationControl && !room.VentilationEnabled);

    private static int BestCounterplayScore(Npc npc)
    {
        var baseSkill = new[] { "Engineering", "Electrical", "Security", "Operations", "Athletics" }
            .Select(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Clamp(
            baseSkill
            + Math.Max(
                CrewTraitMath.Modifier(npc, TraitEffectKind.Force),
                CrewTraitMath.Modifier(npc, TraitEffectKind.Technical)),
            0,
            120);
    }

    private static int BestRepairScore(Npc npc)
    {
        var baseSkill = new[] { "Engineering", "Electrical", "Operations", "Reactor" }
            .Select(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Clamp(
            baseSkill
            + CrewTraitMath.Modifier(npc, TraitEffectKind.Technical)
            + CrewTraitMath.Modifier(npc, TraitEffectKind.Repair),
            0,
            130);
    }

    private static Room? FindSaferRoom(GameState state, Npc npc, Room currentRoom)
    {
        var currentRisk = CrewEnvironmentSafety.RiskScore(currentRoom);
        var reachable = ReachableRooms(state, npc, currentRoom.Id);

        return state.Facility.Rooms.Values
            .Where(room =>
                room.Id != currentRoom.Id
                && room.Type != RoomType.Corridor
                && reachable.Contains(room.Id)
                && CrewEnvironmentSafety.RiskScore(room) + 0.1 < currentRisk)
            .OrderBy(room => CrewEnvironmentSafety.IsHabitable(room) ? 0 : 1)
            .ThenBy(CrewEnvironmentSafety.RiskScore)
            .ThenBy(room => Math.Abs(room.MapX - currentRoom.MapX) + Math.Abs(room.MapY - currentRoom.MapY))
            .ThenBy(room => room.Id)
            .FirstOrDefault();
    }

    private static HashSet<string> ReachableRooms(GameState state, Npc npc, string startRoomId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            startRoomId
        };
        var queue = new Queue<string>();
        queue.Enqueue(startRoomId);

        while (queue.TryDequeue(out var current))
        {
            foreach (var door in state.Facility.Doors.Where(door =>
                         CrewDoorInteractionSystem.CanTraverseWhenReached(state, npc, door)
                         && (door.RoomAId.Equals(current, StringComparison.OrdinalIgnoreCase)
                             || door.RoomBId.Equals(current, StringComparison.OrdinalIgnoreCase))))
            {
                var next = door.RoomAId.Equals(current, StringComparison.OrdinalIgnoreCase)
                    ? door.RoomBId
                    : door.RoomAId;

                if (visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return visited;
    }

    private static NpcIntent Create(
        Npc npc,
        GameState state,
        ActionKind action,
        string? targetId,
        string goal,
        string reason,
        int urgency) =>
        new(
            action,
            targetId,
            goal,
            reason,
            urgency,
            "Fallback",
            state.Elapsed);
}
