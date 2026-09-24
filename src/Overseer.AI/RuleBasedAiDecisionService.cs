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

        // Same order as BrowserMindSystem: every room behind an open hatch is
        // draining, so sealing the hatch toward the breach comes before flight.
        if (DecompressionContainmentRules.FindHatchTowardBreach(state, npc) is { } hatch)
        {
            intent = Create(
                npc,
                state,
                ActionKind.CloseDoor,
                hatch.Id,
                DecompressionContainmentRules.Goal(hatch),
                DecompressionContainmentRules.Reason(state, npc, hatch),
                99);
        }
        else if (StationHazardSystem.FindRepairableBreachForResponder(
                     state,
                     npc,
                     new NavigationSystem()) is { } breachedRoom)
        {
            intent = Create(
                npc,
                state,
                ActionKind.PatchHull,
                breachedRoom.Id,
                $"Patch the hull breach in {breachedRoom.Name}.",
                "The fire is out, the compartment is open to space, and I have the repair skill to seal it using emergency EVA gear.",
                99);
        }
        else if (CrewEnvironmentSafety.IsDangerous(room))
        {
            var fightFire = room.FireIntensity > 0 && StationHazardSystem.ShouldFightFire(npc, room);
            var saferRoom = fightFire ? null : FindSaferRoom(state, npc, room);

            if (fightFire)
            {
                intent = Create(
                    npc,
                    state,
                    ActionKind.FightFire,
                    room.Id,
                    $"Fight the fire in {room.Name}.",
                    "The compartment is burning and I think staying to suppress it is worth the risk.",
                    98);
            }
            else if (saferRoom is not null)
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
        // A viable station fire is an immediate survival emergency. The
        // fallback mind still chooses FightFire; deterministic systems only
        // expose and validate the grounded response.
        else if (StationHazardSystem.FindRemoteFireForResponder(
                     state,
                     npc,
                     new NavigationSystem()) is { } remoteFire)
        {
            intent = Create(
                npc,
                state,
                ActionKind.FightFire,
                remoteFire.Id,
                $"Respond to the fire in {remoteFire.Name}.",
                "The station status panel shows an unattended reachable fire and I am capable of helping suppress it.",
                94);
        }
        // Critical bodily needs supersede ordinary technical/social plans, but
        // not a viable station emergency that this person can safely address.
        else if (npc.Hunger >= CrewNeedThresholds.HungerCritical)
        {
            intent = Create(npc, state, ActionKind.Eat, DiningSeatRules.FallbackDiningTarget(state, npc),
                "Find food now.",
                "I am hungry enough that continuing to ignore it is dangerous.",
                92);
        }
        else if (npc.Fatigue >= CrewNeedThresholds.FatigueCritical)
        {
            intent = Create(npc, state, ActionKind.Sleep, null,
                "Get sleep now.",
                "I am dangerously exhausted and need to stop.",
                90);
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
        else if (FindAdjacentDamagedDoor(state, npc) is { } damagedDoor && CrewCounterplaySystem.BestRepairSkill(npc) >= 55)
        {
            intent = Create(npc, state, ActionKind.RepairDoor, damagedDoor.Id,
                $"Repair {damagedDoor.Id}.",
                "This hatch has visible structural or bypass damage and I can repair it locally.", 72);
        }
        else if (CrewCounterplaySystem.HasSwitchedOffLifeSupport(state) && CrewCounterplaySystem.BestRepairSkill(npc) >= 55)
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
        // Owner idea #74: the model-citizen baseline covers an empty post it can
        // be spared for. It takes no travel, so it sits ahead of routine needs.
        else if (RoleSuccessionRules.FallbackPostToTake(state, npc) is { } post)
        {
            intent = Create(npc, state, ActionKind.AssumeRole, post.ToString(),
                $"Take over as {post}.",
                "Nobody is left in that post, I can do the job and my own post is still covered.",
                70);
        }
        // Basic survival comes before curiosity, matching BrowserMindSystem.
        // With investigation first, a suspicious crew member would investigate
        // while starving and oscillate against the routine steering them to food.
        else if (ScheduledSleepRules.ShouldKeepScheduledSleep(npc, state.Elapsed))
        {
            intent = ScheduledSleepRules.NeedsToiletBreak(npc)
                ? Create(npc, state, ActionKind.UseToilet, null,
                    "Use the washroom.",
                    "I need the toilet.",
                    82)
                : Create(npc, state, ActionKind.Sleep, null,
                "Sleep through my rest period.",
                "It is my sleep period and nothing needs me badly enough to get up.",
                60);
        }
        else if (npc.Hunger >= CrewNeedThresholds.HungerElevated)
        {
            intent = Create(npc, state, ActionKind.Eat, DiningSeatRules.FallbackDiningTarget(state, npc),
                "Get something to eat.",
                "I am hungry enough that food is becoming difficult to ignore.",
                75);
        }
        else if (npc.Fatigue >= CrewNeedThresholds.FatigueElevated)
        {
            intent = Create(npc, state, ActionKind.Sleep, null,
                "Get some sleep.",
                "I am too tired to keep working effectively.",
                72);
        }
        else if (npc.BladderNeed >= CrewNeedThresholds.BladderNeed)
        {
            intent = Create(npc, state, ActionKind.UseToilet, null,
                "Use the washroom.",
                "I need the toilet and should deal with that now.",
                82);
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
        else if (HasLocalRestorableProblem(room) && CrewCounterplaySystem.BestRepairSkill(npc) >= 55)
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
        else if (npc.HygieneNeed >= CrewNeedThresholds.HygieneNeed)
        {
            intent = Create(npc, state, ActionKind.Shower, null,
                "Take a shower.",
                "I need to clean up before I can comfortably focus.",
                64);
        }
        else if (npc.RecreationNeed >= CrewNeedThresholds.RecreationNeed)
        {
            intent = Create(npc, state, ActionKind.Recreate, RecreationActivityRules.FallbackChoice(state, npc, "lounge"),
                "Take a break.",
                "I need some recreation before I burn out.",
                55);
        }
        else
        {
            var worstRelationship = npc.Relationships.Values
                .OrderByDescending(r => r.Resentment)
                .FirstOrDefault();

            if (worstRelationship is { Resentment: >= CrewNeedThresholds.ResentmentArgue })
            {
                intent = Create(npc, state, ActionKind.Argue, worstRelationship.PersonName,
                    $"Confront {worstRelationship.PersonName}.",
                    $"My resentment toward {worstRelationship.PersonName} has been building.",
                    62);
            }
            else
            {
                var bestRelationship = npc.Relationships.Values
                    .OrderByDescending(r => r.Trust + r.Affinity)
                    .FirstOrDefault();

                if (bestRelationship is not null
                    && npc.Personality.Sociability >= CrewNeedThresholds.SociabilityForSocialize
                    && npc.SocialNeed >= CrewNeedThresholds.SocialNeed)
                {
                    intent = Create(npc, state, ActionKind.Socialize, bestRelationship.PersonName,
                        $"Spend time with {bestRelationship.PersonName}.",
                        $"I trust {bestRelationship.PersonName} and would rather not be alone.",
                        45);
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

    private static NpcIntent? FindTurretCountermeasure(GameState state, Npc npc) =>
        TurretCountermeasureSystem.FindCountermeasure(state, npc) is { } decision
            ? Create(npc, state, decision.Action, decision.TargetId, decision.Goal, decision.Reason, decision.Urgency)
            : null;

    private static NpcIntent? FindRobotCountermeasure(GameState state, Npc npc) =>
        RobotCountermeasureSystem.FindCountermeasure(state, npc) is { } decision
            ? Create(npc, state, decision.Action, decision.TargetId, decision.Goal, decision.Reason, decision.Urgency)
            : null;

    private static bool ShouldJoinShutdownTeam(
        Npc npc,
        ShutdownTeamInvitation invitation) =>
        ShutdownCoordinationSystem.ShouldJoinTeam(npc, invitation);

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
        ShutdownCoordinationSystem.FindKnownMechanism(state, npc);

    private static ShutdownTeam? FindShutdownTeam(
        GameState state,
        Npc npc,
        ShutdownMechanism mechanism) =>
        ShutdownCoordinationSystem.FindTeamFor(state, npc, mechanism);

    private static Npc? FindShutdownRecruit(
        GameState state,
        Npc npc,
        ShutdownTeam? team) =>
        ShutdownCoordinationSystem.FindRecruit(state, npc, team);

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
        MissingPersonSystem.MostPressingConcern(npc);

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
        MissingPersonConcern concern) =>
        MissingPersonSystem.ReasonFor(state, concern);

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

    /// <summary>
    /// Converged with <c>BrowserMindSystem.FindSaferRoom</c> (P1 ladder
    /// convergence): costs each candidate via a real crew-traversable path
    /// (<see cref="NavigationSystem.FindPathForCrew"/>) rather than a BFS
    /// reachable-set membership test, so both minds tie-break on genuine
    /// shortest-path length instead of possibly disagreeing on a tie.
    /// </summary>
    private static Room? FindSaferRoom(GameState state, Npc npc, Room currentRoom)
    {
        var currentRisk = CrewEnvironmentSafety.RiskScore(currentRoom);
        var navigation = new NavigationSystem();

        return state.Facility.Rooms.Values
            .Where(room =>
                room.Id != currentRoom.Id
                && room.Type != RoomType.Corridor)
            .Select(room => new
            {
                Room = room,
                Risk = CrewEnvironmentSafety.RiskScore(room),
                Path = navigation.FindPathForCrew(state, npc, currentRoom.Id, room.Id)
            })
            .Where(candidate =>
                candidate.Path.Count >= 2
                && candidate.Risk + 0.1 < currentRisk)
            .OrderBy(candidate => CrewEnvironmentSafety.IsHabitable(candidate.Room) ? 0 : 1)
            .ThenBy(candidate => candidate.Risk)
            .ThenBy(candidate => candidate.Path.Count)
            .ThenBy(candidate =>
                Math.Abs(candidate.Room.MapX - currentRoom.MapX)
                + Math.Abs(candidate.Room.MapY - currentRoom.MapY))
            .ThenBy(candidate => candidate.Room.Id)
            .Select(candidate => candidate.Room)
            .FirstOrDefault();
    }

    private static HashSet<string> ReachableRooms(GameState state, Npc npc, string startRoomId) =>
        new NavigationSystem().ReachableRoomsForCrew(state, npc, startRoomId);

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
