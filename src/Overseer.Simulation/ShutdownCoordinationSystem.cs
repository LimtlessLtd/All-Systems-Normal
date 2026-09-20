using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Turns explicit NPC recruitment/join intentions into shared plans. Recruitment
/// communicates a claim about a control; it does not grant direct physical
/// knowledge of hardware the listener has never inspected.
/// </summary>
public sealed class ShutdownCoordinationSystem
{
    private const int RecruitmentMinutes = 1;

    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        ExpireOldInvitations(state);

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.RecruitShutdownAlly))
        {
            ProcessRecruitment(state, npc);
        }

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.JoinShutdownTeam))
        {
            ProcessJoin(state, npc);
        }
    }

    private static void ProcessRecruitment(GameState state, Npc recruiter)
    {
        var target = state.Crew.FirstOrDefault(candidate =>
            candidate.IsAlive
            && candidate.IsPresent
            && candidate.Id != recruiter.Id
            && candidate.Name.Equals(
                recruiter.CurrentAction.TargetId,
                StringComparison.OrdinalIgnoreCase)
            && candidate.CurrentRoomId.Equals(
                recruiter.CurrentRoomId,
                StringComparison.OrdinalIgnoreCase));

        var mechanism = ChooseKnownMechanism(state, recruiter);

        if (target is null || mechanism is null || recruiter.OverseerSuspicion < 65)
        {
            ClearAction(recruiter, "The recruitment attempt no longer has a grounded target.");
            return;
        }

        if (recruiter.RoutineUntil == TimeSpan.Zero)
        {
            recruiter.RoutineUntil =
                state.Elapsed + TimeSpan.FromMinutes(RecruitmentMinutes);
            recruiter.Bubble = new NpcBubble(
                $"I need your help. I found a way to isolate Overseer.",
                NpcBubbleKind.Speech,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(3));
            Log(state, $"{recruiter.Name} asks {target.Name} to help with an Overseer isolation plan.");
            return;
        }

        if (state.Elapsed < recruiter.RoutineUntil)
            return;

        var team = GetOrCreateTeam(state, recruiter, mechanism);
        team.InvitedNpcIds.Add(target.Id);

        target.PendingShutdownTeamInvitation = new ShutdownTeamInvitation(
            team.Id,
            mechanism.Id,
            recruiter.Name,
            mechanism.RoomId,
            state.Elapsed);
        target.NeedsMindReconsideration = true;
        target.Memories.Add(new Memory(
            $"{recruiter.Name} asked me to help isolate Overseer and said the relevant hardware is in {state.Facility.Rooms[mechanism.RoomId].Name}.",
            state.Elapsed,
            .72));

        target.InvestigationLeads[$"team-claim:{team.Id}"] = new InvestigationLead
        {
            Id = $"team-claim:{team.Id}",
            Description =
                $"{recruiter.Name} claims they physically found Overseer isolation hardware here. I have not personally verified it.",
            RoomId = mechanism.RoomId,
            CreatedAt = state.Elapsed
        };

        recruiter.RoutineUntil = TimeSpan.Zero;
        recruiter.CurrentAction = new NpcAction(
            ActionKind.Idle,
            null,
            $"Waiting for {target.Name} to decide whether to join.");
        recruiter.Intent = null;

        ConversationPacingSystem.Schedule(
            target,
            "You want me to help shut the AI out?",
            NpcBubbleKind.Speech,
            state.Elapsed + TimeSpan.FromMinutes(1),
            2);

        Log(state, $"{target.Name} receives a grounded shutdown-team invitation from {recruiter.Name}.");
    }

    private static void ProcessJoin(GameState state, Npc npc)
    {
        var invitation = npc.PendingShutdownTeamInvitation;
        if (invitation is null
            || !invitation.TeamId.Equals(
                npc.CurrentAction.TargetId,
                StringComparison.OrdinalIgnoreCase))
        {
            ClearAction(npc, "There is no active team invitation to accept.");
            return;
        }

        var team = state.ShutdownTeams.FirstOrDefault(candidate =>
            candidate.IsActive
            && candidate.Id.Equals(invitation.TeamId, StringComparison.OrdinalIgnoreCase)
            && candidate.MechanismId.Equals(
                invitation.MechanismId,
                StringComparison.OrdinalIgnoreCase));

        if (team is null)
        {
            npc.PendingShutdownTeamInvitation = null;
            ClearAction(npc, "That shutdown plan is no longer active.");
            return;
        }

        var before = team.MemberIds.Count;
        team.MemberIds.Add(npc.Id);
        team.InvitedNpcIds.Remove(npc.Id);
        npc.ShutdownTeamId = team.Id;
        npc.PendingShutdownTeamInvitation = null;
        npc.CurrentAction = new NpcAction(
            ActionKind.Idle,
            null,
            "Joined the coordinated isolation plan.");
        npc.Intent = null;
        npc.RoutineUntil = TimeSpan.Zero;
        npc.NeedsMindReconsideration = true;

        if (before < 2 && team.MemberIds.Count >= 2)
        {
            state.Telemetry.ShutdownTeamsFormed++;
            AudioCueSystem.Emit(
                state,
                AudioCueKind.Important,
                npc.Id.ToString(),
                npc.CurrentRoomId);
        }

        npc.Memories.Add(new Memory(
            $"I agreed to join {invitation.FromNpcName}'s plan to reach the claimed isolation hardware.",
            state.Elapsed,
            .82));

        npc.Bubble = new NpcBubble(
            "All right. I'm with you.",
            NpcBubbleKind.Speech,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(3));

        Log(state, $"{npc.Name} joins shutdown team {team.Id}.");
    }

    private static ShutdownMechanism? ChooseKnownMechanism(
        GameState state,
        Npc npc)
    {
        var known = state.ShutdownMechanisms
            .Where(mechanism =>
                mechanism.IsOnline
                && SuspicionSystem.KnowsMechanism(npc, mechanism))
            .ToList();

        if (known.Count == 0)
            return null;

        return known
            .OrderBy(mechanism =>
                mechanism.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(mechanism => mechanism.Id)
            .First();
    }

    private static ShutdownTeam GetOrCreateTeam(
        GameState state,
        Npc recruiter,
        ShutdownMechanism mechanism)
    {
        if (!string.IsNullOrWhiteSpace(recruiter.ShutdownTeamId))
        {
            var existing = state.ShutdownTeams.FirstOrDefault(team =>
                team.IsActive
                && team.Id.Equals(
                    recruiter.ShutdownTeamId,
                    StringComparison.OrdinalIgnoreCase)
                && team.MechanismId.Equals(
                    mechanism.Id,
                    StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
                return existing;
        }

        var team = new ShutdownTeam
        {
            Id = $"team:{mechanism.Id}:{recruiter.Id:N}",
            MechanismId = mechanism.Id,
            LeaderId = recruiter.Id,
            FormedAt = state.Elapsed
        };
        team.MemberIds.Add(recruiter.Id);
        recruiter.ShutdownTeamId = team.Id;
        state.ShutdownTeams.Add(team);
        return team;
    }

    private static void ExpireOldInvitations(GameState state)
    {
        foreach (var npc in state.Crew.Where(npc =>
                     npc.PendingShutdownTeamInvitation is not null))
        {
            var invitation = npc.PendingShutdownTeamInvitation!;
            if (state.Elapsed - invitation.OfferedAt <= TimeSpan.FromMinutes(15))
                continue;

            var team = state.ShutdownTeams.FirstOrDefault(candidate =>
                candidate.Id.Equals(invitation.TeamId, StringComparison.OrdinalIgnoreCase));
            team?.InvitedNpcIds.Remove(npc.Id);
            npc.PendingShutdownTeamInvitation = null;
        }
    }

    private static void ClearAction(Npc npc, string reason)
    {
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, reason);
        npc.RoutineUntil = TimeSpan.Zero;
        npc.Intent = null;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
