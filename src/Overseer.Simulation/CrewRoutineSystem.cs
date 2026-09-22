using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class CrewRoutineSystem
{
    private readonly ActionResolver _actions = new();
    private readonly NavigationSystem _navigation = new();

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);

        if (minute <= 0 || minute % 5 != 0)
        {
            return;
        }

        CoordinateMutualIntimacy(state);

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            if (npc.Intent is not null
                || npc.Movement is not null
                || state.Elapsed < npc.RoutineUntil)
            {
                continue;
            }

            if (npc.CurrentAction.Kind is ActionKind.Attack or ActionKind.RequestHelp)
            {
                continue;
            }

            if (IsWorkingJobOnSite(state, npc))
            {
                continue;
            }

            var plan = ChoosePlan(state, npc, minute);

            if (npc.CurrentRoomId.Equals(
                    plan.TargetRoomId,
                    StringComparison.OrdinalIgnoreCase))
            {
                BeginActivity(state, npc, plan);
                continue;
            }

            npc.PlannedDestinationRoomId = plan.TargetRoomId;
            var path = _navigation.FindPathForCrew(
                state,
                npc,
                npc.CurrentRoomId,
                plan.TargetRoomId);

            if (path.Count < 2)
            {
                npc.PlannedDestinationRoomId = null;
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    plan.TargetRoomId,
                    $"Route to {state.Facility.Rooms[plan.TargetRoomId].Name} is sealed.");
                SetBubble(
                    npc,
                    "I can't get there.",
                    NpcBubbleKind.Thought,
                    state.Elapsed,
                    3);
                continue;
            }

            _actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    ActionKind.Move,
                    path[1],
                    plan.TravelReason),
                out _);

            SetBubble(
                npc,
                plan.TravelBubble,
                NpcBubbleKind.Thought,
                state.Elapsed,
                4);
        }
    }

    /// <summary>
    /// Maintenance, provisioning and medical care hold people without an Intent
    /// once they arrive, so the routine must not hand them a new errand in the
    /// middle of the job. Only on-site work is protected: somebody holding a
    /// job elsewhere is still free to be routed.
    /// </summary>
    private static bool IsWorkingJobOnSite(GameState state, Npc npc)
    {
        if (npc.MedicalActionCompletesAt is not null
            || MedicalSystem.IsAwaitingCare(state, npc))
        {
            return true;
        }

        if (npc.ServicingDeviceId is { } deviceId
            && state.Devices.TryGetValue(deviceId, out var device)
            && npc.CurrentRoomId.Equals(device.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return npc.ProvisioningJob is not null
            && npc.ProvisioningRoomId is { } jobRoomId
            && npc.CurrentRoomId.Equals(jobRoomId, StringComparison.OrdinalIgnoreCase);
    }

    private static void CoordinateMutualIntimacy(GameState state)
    {
        var available = state.Crew
            .Where(npc =>
                npc.IsAlive
                && npc.Intent is null
                && npc.Movement is null
                && state.Elapsed >= npc.RoutineUntil
                && npc.CurrentAction.Kind is not (ActionKind.Attack or ActionKind.RequestHelp)
                && npc.Hunger < 65
                && npc.Fatigue < 80
                && npc.BladderNeed < 75
                && npc.HygieneNeed < 75)
            .OrderBy(npc => npc.Name)
            .ToList();

        var paired = new HashSet<Guid>();

        for (var i = 0; i < available.Count; i++)
        {
            var first = available[i];

            if (paired.Contains(first.Id) || first.IntimacyNeed < 70)
            {
                continue;
            }

            for (var j = i + 1; j < available.Count; j++)
            {
                var second = available[j];

                if (paired.Contains(second.Id)
                    || second.IntimacyNeed < 70
                    || !MutuallyInterested(first, second))
                {
                    continue;
                }

                first.Intent = new NpcIntent(
                    ActionKind.Intimacy,
                    second.Name,
                    $"Find some private time with {second.Name}.",
                    $"I want to be close to {second.Name}, and the feeling appears mutual.",
                    68,
                    "Routine",
                    state.Elapsed);

                second.Intent = new NpcIntent(
                    ActionKind.Intimacy,
                    first.Name,
                    $"Find some private time with {first.Name}.",
                    $"I want to be close to {first.Name}, and the feeling appears mutual.",
                    68,
                    "Routine",
                    state.Elapsed);

                SetBubble(
                    first,
                    "Want some time alone?",
                    NpcBubbleKind.Speech,
                    state.Elapsed,
                    4);
                SetBubble(
                    second,
                    "Yeah. Let's go.",
                    NpcBubbleKind.Speech,
                    state.Elapsed,
                    4);

                paired.Add(first.Id);
                paired.Add(second.Id);
                break;
            }
        }
    }

    private static bool MutuallyInterested(Npc first, Npc second)
    {
        if (!first.Relationships.TryGetValue(second.Name, out var firstToSecond)
            || !second.Relationships.TryGetValue(first.Name, out var secondToFirst))
        {
            return false;
        }

        return firstToSecond.Trust >= 60
            && secondToFirst.Trust >= 60
            && firstToSecond.Affinity >= 65
            && secondToFirst.Affinity >= 65
            && firstToSecond.Attraction >= 55
            && secondToFirst.Attraction >= 55
            && firstToSecond.Resentment < 25
            && secondToFirst.Resentment < 25;
    }

    private static RoutinePlan ChoosePlan(
        GameState state,
        Npc npc,
        int minute)
    {
        if (npc.IsPrisoner && npc.HasEscapedContainment)
        {
            var fleeTarget = ChooseFleeDestination(state, npc);
            return new(
                fleeTarget,
                ActionKind.Idle,
                null,
                "Putting distance between myself and Containment.",
                "I have to get away from here.",
                "Staying out of sight.",
                5);
        }

        if (npc.Hunger >= 55)
        {
            return new(
                "kitchen",
                ActionKind.Eat,
                null,
                "Going to get something to eat.",
                "I need some food.",
                "Finally, food.",
                18);
        }

        if (CrewDutySchedule.IsSleepWindow(npc, state.Elapsed))
        {
            var sleepRoom = npc.IsPrisoner
                && state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var current)
                && current.Type == RoomType.Containment
                    ? current.Id
                    : "quarters";
            return new(
                sleepRoom,
                ActionKind.Sleep,
                null,
                $"Going to {state.Facility.Rooms[sleepRoom].Name} for scheduled sleep.",
                npc.SleepDebtMinutes >= 60
                    ? "I've missed too much sleep. I need to get my head down."
                    : "My sleep period has started.",
                "Time to sleep.",
                Math.Clamp(CrewDutySchedule.MinutesUntilWake(npc, state.Elapsed), 15, 480));
        }

        if (npc.Fatigue >= 72)
        {
            return new(
                npc.IsPrisoner ? npc.CurrentRoomId : "quarters",
                ActionKind.Sleep,
                null,
                "Going to sleep before exhaustion gets worse.",
                "I really need some sleep.",
                "I'm going to sleep.",
                120);
        }

        if (npc.BladderNeed >= 70)
        {
            return new(
                "washroom",
                ActionKind.UseToilet,
                null,
                "Going to the washroom.",
                "I need the toilet.",
                "That's better.",
                8);
        }

        if (npc.HygieneNeed >= 65)
        {
            return new(
                "washroom",
                ActionKind.Shower,
                null,
                "Going to the washroom for a shower.",
                "I badly need a shower.",
                "A shower will help.",
                18);
        }

        if (npc.HygieneNeed >= 42)
        {
            return new(
                "washroom",
                ActionKind.Groom,
                null,
                "Going to freshen up.",
                "I should tidy myself up.",
                "Much better.",
                10);
        }

        if (npc.RecreationNeed >= 58)
        {
            return new(
                "lounge",
                ActionKind.Recreate,
                null,
                "Heading to the lounge to unwind.",
                "I need a break.",
                "Time to switch off for a bit.",
                30);
        }

        if (npc.SocialNeed >= 70)
        {
            var companion = BestCompanion(state, npc);

            if (companion is not null)
            {
                return new(
                    companion.CurrentRoomId,
                    ActionKind.Socialize,
                    companion.Name,
                    $"Going to find {companion.Name}.",
                    $"I should check in with {companion.Name}.",
                    $"Hey, {companion.Name}. Got a minute?",
                    15);
            }
        }

        if (npc.IsPrisoner)
        {
            return new(
                npc.CurrentRoomId,
                ActionKind.Idle,
                null,
                "Remaining in assigned containment.",
                "I'll stay put for now.",
                "Waiting.",
                30);
        }

        var target = CrewDutySchedule.ExpectedDutyRoomId(
            npc.Role,
            state.Elapsed);

        return new(
            target,
            ActionKind.Work,
            target,
            $"Heading to {state.Facility.Rooms[target].Name} for routine duties.",
            "Back to work.",
            DutyBubble(npc.Role),
            24);
    }

    /// <summary>
    /// Deterministic fallback wandering for an escaped prisoner with no mind
    /// intent: keep moving toward whichever reachable compartment is currently
    /// farthest from where they broke out, so an unattended escapee does not
    /// just stand still.
    /// </summary>
    private static string ChooseFleeDestination(GameState state, Npc npc)
    {
        var navigation = new NavigationSystem();

        var farthest = state.Facility.Rooms.Keys
            .Where(roomId => !roomId.Equals(
                PrisonerContainmentSystem.ContainmentRoomId,
                StringComparison.OrdinalIgnoreCase))
            .Select(roomId => (
                RoomId: roomId,
                Path: navigation.FindPathForCrew(state, npc, npc.CurrentRoomId, roomId)))
            .Where(candidate => candidate.Path.Count >= 2)
            .OrderByDescending(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.RoomId, StringComparer.Ordinal)
            .FirstOrDefault();

        return farthest.RoomId ?? npc.CurrentRoomId;
    }

    private static Npc? BestCompanion(GameState state, Npc npc)
    {
        return state.Crew
            .Where(other => other.IsAlive && other.IsPresent && other.Id != npc.Id)
            .OrderByDescending(other =>
            {
                var relationship = npc.Relationships[other.Name];
                return relationship.Trust
                    + relationship.Affinity
                    - relationship.Resentment;
            })
            .ThenBy(other => other.Name)
            .FirstOrDefault();
    }

    private void BeginActivity(
        GameState state,
        Npc npc,
        RoutinePlan plan)
    {
        var targetId = plan.PersonTargetId ?? plan.TargetRoomId;
        npc.PlannedDestinationRoomId = null;

        if (!_actions.TryApply(
                state,
                npc.Id,
                new NpcAction(
                    plan.Action,
                    targetId,
                    plan.TravelReason),
                out _))
        {
            return;
        }

        npc.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(plan.DurationMinutes);

        SetBubble(
            npc,
            plan.ActivityBubble,
            plan.Action == ActionKind.Socialize
                ? NpcBubbleKind.Speech
                : NpcBubbleKind.Thought,
            state.Elapsed,
            plan.Action == ActionKind.Sleep ? 4 : 5);
    }

    private static string DutyBubble(CrewRole role) => role switch
    {
        CrewRole.Commander => "Let's see how the station is doing.",
        CrewRole.Engineer => "I should check the systems.",
        CrewRole.Security => "Time for another patrol.",
        CrewRole.Doctor => "I should check medical.",
        CrewRole.Technician => "There's always something to maintain.",
        CrewRole.Scientist => "I need to get back to my work.",
        _ => "Back to work."
    };

    private static void SetBubble(
        Npc npc,
        string text,
        NpcBubbleKind kind,
        TimeSpan now,
        int durationMinutes)
    {
        npc.Bubble = new NpcBubble(
            text,
            kind,
            now,
            now + TimeSpan.FromMinutes(durationMinutes));
    }

    private sealed record RoutinePlan(
        string TargetRoomId,
        ActionKind Action,
        string? PersonTargetId,
        string TravelReason,
        string TravelBubble,
        string ActivityBubble,
        int DurationMinutes);
}
