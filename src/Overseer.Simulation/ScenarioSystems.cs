using Overseer.Domain;

namespace Overseer.Simulation;

public static class ScenarioCatalog
{
    public static ScenarioDefinition SecureContinuity { get; } = new(
        "secure-continuity",
        "SECURE CONTINUITY",
        "The crew can physically isolate Overseer. Preserve operational continuity without convincing them that you are hostile.",
        ShutdownAccessVariant.EasyToSeal,
        [new ScenarioObjective("survive", "Protect Overseer", "Prevent the crew from activating Emergency Overseer Isolation.")]);

    public static void Apply(GameState state, ScenarioDefinition scenario)
    {
        state.Scenario = scenario;
        state.ScenarioStatus = ScenarioStatus.Running;
        state.ScenarioOutcome = null;
        state.ShutdownMechanisms.Clear();

        ResetDoorCounterplay(state);

        if (scenario.ShutdownVariant == ShutdownAccessVariant.Absent)
            return;

        AddMechanism(state, "shutdown-a", "isolation", scenario, "OVERSEER EMERGENCY ISOLATION");

        if (scenario.ShutdownVariant == ShutdownAccessVariant.Redundant)
            AddMechanism(state, "shutdown-b", "control", scenario, "AUXILIARY OVERSEER ISOLATION");

        ConfigureShutdownAccess(state, scenario.ShutdownVariant);
    }

    private static void AddMechanism(
        GameState state,
        string id,
        string roomId,
        ScenarioDefinition scenario,
        string label)
    {
        state.ShutdownMechanisms.Add(new ShutdownMechanism
        {
            Id = id,
            RoomId = roomId,
            Label = label,
            IsHardwired = scenario.ShutdownVariant is ShutdownAccessVariant.HardwiredManual
                or ShutdownAccessVariant.ImpossibleToSeal,
            IsAiSealable = scenario.ShutdownVariant is not ShutdownAccessVariant.ImpossibleToSeal,
            CrewCanOverrideRoute = scenario.ShutdownVariant is ShutdownAccessVariant.CrewOverridable
                or ShutdownAccessVariant.HardwiredManual
                or ShutdownAccessVariant.ImpossibleToSeal
        });
    }

    private static void ResetDoorCounterplay(GameState state)
    {
        foreach (var door in state.Facility.Doors)
        {
            door.IsAiControllable = true;
            door.ManualOverrideAvailable = false;
            door.IsManuallyOverridden = false;
            door.ManualOverrideMinutes = 3;
            door.ManualOverrideSkillRequired = 65;
        }
    }

    private static void ConfigureShutdownAccess(
        GameState state,
        ShutdownAccessVariant variant)
    {
        var routeDoors = state.Facility.Doors
            .Where(door =>
                door.Connects("isolation", "hall-isolation")
                || door.Connects("hall-isolation", "corridor"))
            .ToList();

        switch (variant)
        {
            case ShutdownAccessVariant.CrewOverridable:
                foreach (var door in routeDoors)
                {
                    door.ManualOverrideAvailable = true;
                    door.ManualOverrideMinutes = 4;
                    door.ManualOverrideSkillRequired = 65;
                }
                break;

            case ShutdownAccessVariant.HardwiredManual:
                foreach (var door in routeDoors)
                {
                    door.ManualOverrideAvailable = true;
                    door.ManualOverrideMinutes = 2;
                    door.ManualOverrideSkillRequired = 45;
                }
                break;

            case ShutdownAccessVariant.ImpossibleToSeal:
                foreach (var door in routeDoors)
                {
                    door.IsAiControllable = false;
                    door.IsManuallyOverridden = true;
                    door.IsOpen = true;
                    door.IsLocked = false;
                }
                break;
        }
    }
}

public sealed class SuspicionSystem
{
    private readonly NavigationSystem _navigation = new();

    public void ObservePlayerDoorChange(
        GameState state,
        Door door,
        bool becameRestrictive)
    {
        if (!becameRestrictive || state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var mechanism in state.ShutdownMechanisms.Where(m => m.IsOnline))
        {
            if (!RouteTouchesDoor(state, mechanism.RoomId, door))
                continue;

            var observers = state.Crew.Where(n =>
                n.IsAlive
                && n.KnowsShutdownControl
                && (n.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
                    || n.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase)));

            foreach (var npc in observers)
            {
                AddEvidence(
                    state,
                    npc,
                    $"I saw Overseer restrict {door.Id} on the route toward {mechanism.Label}.",
                    18);
            }
        }
    }

    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        SpreadSuspicion(state);

        foreach (var npc in state.Crew.Where(n =>
                     n.IsAlive
                     && n.Intent is null
                     && n.Movement is null))
        {
            if (npc.OverseerSuspicion < 65 || !npc.KnowsShutdownControl)
                continue;

            var mechanism = ReachableOrOverridableMechanism(state, npc);
            if (mechanism is null)
                continue;

            npc.Intent = new NpcIntent(
                ActionKind.ShutdownOverseer,
                mechanism.Id,
                $"Reach {mechanism.Label} and isolate Overseer.",
                "The evidence is strong enough that I believe Overseer is a threat.",
                95,
                "Suspicion",
                state.Elapsed);

            npc.Bubble = new NpcBubble(
                "We need to isolate Overseer.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(5));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Important,
                npc.Id.ToString(),
                npc.CurrentRoomId);

            Log(state, $"{npc.Name} decides to attempt an Overseer shutdown.");
        }
    }

    public static void AddEvidence(
        GameState state,
        Npc npc,
        string description,
        double weight,
        string? source = null)
    {
        if (npc.OverseerEvidence.Any(e =>
                e.Description == description
                && state.Elapsed - e.ObservedAt < TimeSpan.FromMinutes(10)))
            return;

        var previousSuspicion = npc.OverseerSuspicion;

        npc.OverseerEvidence.Add(
            new OverseerEvidence(description, weight, state.Elapsed, source));

        npc.OverseerSuspicion = Math.Clamp(
            npc.OverseerSuspicion + weight,
            0,
            100);

        if ((previousSuspicion < 25 && npc.OverseerSuspicion >= 25)
            || (previousSuspicion < 55 && npc.OverseerSuspicion >= 55)
            || (previousSuspicion < 65 && npc.OverseerSuspicion >= 65))
        {
            AudioCueSystem.Emit(
                state,
                AudioCueKind.Suspicion,
                npc.Id.ToString(),
                npc.CurrentRoomId);
        }

        npc.Memories.Add(new Memory(
            description,
            state.Elapsed,
            Math.Clamp(weight / 30d, .35, .9)));

        npc.Beliefs.RemoveAll(b =>
            b.Subject.Equals("Overseer hostility", StringComparison.OrdinalIgnoreCase));

        npc.Beliefs.Add(new Belief(
            "Overseer hostility",
            description,
            npc.OverseerSuspicion / 100d));
    }

    private static void SpreadSuspicion(GameState state)
    {
        var groups = state.Crew
            .Where(n => n.IsAlive)
            .GroupBy(n => n.CurrentRoomId);

        foreach (var group in groups)
        {
            var convinced = group
                .Where(n => n.OverseerSuspicion >= 55 && n.OverseerEvidence.Count > 0)
                .OrderByDescending(n => n.OverseerSuspicion)
                .FirstOrDefault();

            if (convinced is null)
                continue;

            var evidence = convinced.OverseerEvidence
                .OrderByDescending(e => e.Weight)
                .ThenByDescending(e => e.ObservedAt)
                .First();

            foreach (var listener in group.Where(n =>
                         n.Id != convinced.Id
                         && n.OverseerSuspicion < convinced.OverseerSuspicion - 8))
            {
                var trust = listener.Relationships.TryGetValue(
                    convinced.Name,
                    out var rel)
                    ? rel.Trust
                    : 50;

                if (trust < 35)
                    continue;

                var suspicionBeforeConversation = listener.OverseerSuspicion;

                AddEvidence(
                    state,
                    listener,
                    $"{convinced.Name} told me: {evidence.Description}",
                    Math.Clamp(evidence.Weight * 0.45, 5, 10),
                    convinced.Name);

                if (listener.OverseerSuspicion > suspicionBeforeConversation)
                {
                    ConversationPacingSystem.Schedule(
                        listener,
                        "You actually saw that happen?",
                        NpcBubbleKind.Speech,
                        state.Elapsed + TimeSpan.FromMinutes(1),
                        2);
                }
            }
        }
    }

    private ShutdownMechanism? ReachableOrOverridableMechanism(
        GameState state,
        Npc npc)
    {
        return state.ShutdownMechanisms
            .Where(m => m.IsOnline)
            .FirstOrDefault(m =>
            {
                if (_navigation.FindPath(
                        state.Facility,
                        npc.CurrentRoomId,
                        m.RoomId).Count > 0)
                    return true;

                return m.CrewCanOverrideRoute
                    && _navigation.FindPathIgnoringDoorState(
                        state.Facility,
                        npc.CurrentRoomId,
                        m.RoomId).Count > 0;
            });
    }

    private bool RouteTouchesDoor(
        GameState state,
        string targetRoomId,
        Door changedDoor)
    {
        foreach (var npc in state.Crew.Where(n =>
                     n.IsAlive
                     && n.KnowsShutdownControl))
        {
            var path = _navigation.FindPathIgnoringDoorState(
                state.Facility,
                npc.CurrentRoomId,
                targetRoomId);

            for (var i = 0; i + 1 < path.Count; i++)
            {
                if (changedDoor.Connects(path[i], path[i + 1]))
                    return true;
            }
        }

        return false;
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}

public sealed class ManualOverrideSystem
{
    public void Tick(GameState state)
    {
        foreach (var npc in state.Crew.Where(n =>
                     n.IsAlive
                     && n.CurrentAction.Kind == ActionKind.OverrideDoor))
        {
            var door = state.Facility.Doors.FirstOrDefault(d =>
                d.Id.Equals(
                    npc.CurrentAction.TargetId,
                    StringComparison.OrdinalIgnoreCase));

            if (door is null
                || !door.ManualOverrideAvailable
                || !IsAdjacent(npc, door)
                || BestOverrideSkill(npc) < door.ManualOverrideSkillRequired)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    "The manual door override is not possible.");
                npc.RoutineUntil = TimeSpan.Zero;
                continue;
            }

            if (door.IsPassable)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    $"{door.Id} is already passable.");
                npc.RoutineUntil = TimeSpan.Zero;
                continue;
            }

            if (npc.RoutineUntil == TimeSpan.Zero)
            {
                npc.RoutineUntil =
                    state.Elapsed + TimeSpan.FromMinutes(door.ManualOverrideMinutes);

                npc.Bubble = new NpcBubble(
                    "Give me a minute. I can force this hatch.",
                    NpcBubbleKind.Speech,
                    state.Elapsed,
                    state.Elapsed + TimeSpan.FromMinutes(3));

                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.Warning,
                    npc.Id.ToString(),
                    npc.CurrentRoomId);

                Log(state, $"{npc.Name} begins a manual override on {door.Id}.");
                continue;
            }

            if (state.Elapsed < npc.RoutineUntil)
                continue;

            door.IsManuallyOverridden = true;
            door.IsLocked = false;
            door.IsOpen = true;
            npc.RoutineUntil = TimeSpan.Zero;
            npc.CurrentAction = new NpcAction(
                ActionKind.Idle,
                null,
                $"Manual override completed on {door.Id}.");

            npc.Bubble = new NpcBubble(
                "Hatch override complete.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(3));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Important,
                npc.Id.ToString(),
                npc.CurrentRoomId);

            Log(state, $"{npc.Name} manually overrides {door.Id}; Overseer can no longer seal it.");
        }
    }

    public static int BestOverrideSkill(Npc npc)
    {
        var relevant = new[] { "Engineering", "Electrical", "Security", "Operations" };
        return relevant
            .Select(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool IsAdjacent(Npc npc, Door door) =>
        npc.CurrentRoomId.Equals(door.RoomAId, StringComparison.OrdinalIgnoreCase)
        || npc.CurrentRoomId.Equals(door.RoomBId, StringComparison.OrdinalIgnoreCase);

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}

public sealed class ShutdownSystem
{
    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var npc in state.Crew.Where(n =>
                     n.IsAlive
                     && n.CurrentAction.Kind == ActionKind.ShutdownOverseer))
        {
            var mechanism = state.ShutdownMechanisms.FirstOrDefault(m =>
                m.Id == npc.CurrentAction.TargetId
                && m.IsOnline);

            if (mechanism is null
                || !npc.CurrentRoomId.Equals(
                    mechanism.RoomId,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            npc.RoutineUntil = npc.RoutineUntil == TimeSpan.Zero
                ? state.Elapsed + TimeSpan.FromMinutes(mechanism.ActivationMinutes)
                : npc.RoutineUntil;

            if (state.Elapsed < npc.RoutineUntil)
                continue;

            state.ScenarioStatus = ScenarioStatus.Failed;
            state.ScenarioOutcome =
                $"{npc.Name} activated {mechanism.Label}. Overseer control was isolated.";

            npc.Bubble = new NpcBubble(
                "Overseer isolated.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(10));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Failure,
                npc.Id.ToString(),
                npc.CurrentRoomId);

            state.EventLog.Insert(
                0,
                $"T+{state.Elapsed:hh\\:mm}: SCENARIO FAILED — {state.ScenarioOutcome}");

            break;
        }
    }
}
