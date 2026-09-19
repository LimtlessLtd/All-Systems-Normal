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

        if (scenario.ShutdownVariant == ShutdownAccessVariant.Absent)
            return;

        AddMechanism(state, "shutdown-a", "isolation", scenario, "OVERSEER EMERGENCY ISOLATION");

        if (scenario.ShutdownVariant == ShutdownAccessVariant.Redundant)
            AddMechanism(state, "shutdown-b", "control", scenario, "AUXILIARY OVERSEER ISOLATION");
    }

    private static void AddMechanism(GameState state, string id, string roomId, ScenarioDefinition scenario, string label)
    {
        state.ShutdownMechanisms.Add(new ShutdownMechanism
        {
            Id = id,
            RoomId = roomId,
            Label = label,
            IsHardwired = scenario.ShutdownVariant is ShutdownAccessVariant.HardwiredManual or ShutdownAccessVariant.ImpossibleToSeal,
            IsAiSealable = scenario.ShutdownVariant is not ShutdownAccessVariant.ImpossibleToSeal,
            CrewCanOverrideRoute = scenario.ShutdownVariant is ShutdownAccessVariant.CrewOverridable or ShutdownAccessVariant.HardwiredManual or ShutdownAccessVariant.ImpossibleToSeal
        });
    }
}

public sealed class SuspicionSystem
{
    private readonly NavigationSystem _navigation = new();

    public void ObservePlayerDoorChange(GameState state, Door door, bool becameRestrictive)
    {
        if (!becameRestrictive || state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var mechanism in state.ShutdownMechanisms.Where(m => m.IsOnline))
        {
            if (!RouteTouchesDoor(state, mechanism.RoomId, door))
                continue;

            foreach (var npc in state.Crew.Where(n => n.IsAlive && n.KnowsShutdownControl))
            {
                AddEvidence(state, npc,
                    $"Overseer restricted access toward {mechanism.Label}.",
                    18);
            }
        }
    }

    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        SpreadSuspicion(state);

        foreach (var npc in state.Crew.Where(n => n.IsAlive && n.Intent is null && n.Movement is null))
        {
            if (npc.OverseerSuspicion < 65 || !npc.KnowsShutdownControl)
                continue;

            var mechanism = ReachableMechanism(state, npc);
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
                "We need to isolate Overseer.", NpcBubbleKind.Alert,
                state.Elapsed, state.Elapsed + TimeSpan.FromMinutes(5));
            Log(state, $"{npc.Name} decides to attempt an Overseer shutdown.");
        }
    }

    public static void AddEvidence(GameState state, Npc npc, string description, double weight, string? source = null)
    {
        if (npc.OverseerEvidence.Any(e => e.Description == description && state.Elapsed - e.ObservedAt < TimeSpan.FromMinutes(10)))
            return;

        npc.OverseerEvidence.Add(new OverseerEvidence(description, weight, state.Elapsed, source));
        npc.OverseerSuspicion = Math.Clamp(npc.OverseerSuspicion + weight, 0, 100);
        npc.Memories.Add(new Memory(description, state.Elapsed, Math.Clamp(weight / 30d, .35, .9)));
        npc.Beliefs.RemoveAll(b => b.Subject.Equals("Overseer hostility", StringComparison.OrdinalIgnoreCase));
        npc.Beliefs.Add(new Belief("Overseer hostility", description, npc.OverseerSuspicion / 100d));
    }

    private static void SpreadSuspicion(GameState state)
    {
        var groups = state.Crew.Where(n => n.IsAlive).GroupBy(n => n.CurrentRoomId);
        foreach (var group in groups)
        {
            var convinced = group.Where(n => n.OverseerSuspicion >= 55).OrderByDescending(n => n.OverseerSuspicion).FirstOrDefault();
            if (convinced is null) continue;

            foreach (var listener in group.Where(n => n.Id != convinced.Id && n.OverseerSuspicion < convinced.OverseerSuspicion - 8))
            {
                var trust = listener.Relationships.TryGetValue(convinced.Name, out var rel) ? rel.Trust : 50;
                if (trust < 35) continue;
                AddEvidence(state, listener, $"{convinced.Name} warned me about suspicious Overseer behaviour.", 8, convinced.Name);
                listener.Bubble = new NpcBubble("You think Overseer did that?", NpcBubbleKind.Speech, state.Elapsed, state.Elapsed + TimeSpan.FromMinutes(3));
            }
        }
    }

    private ShutdownMechanism? ReachableMechanism(GameState state, Npc npc) =>
        state.ShutdownMechanisms.Where(m => m.IsOnline)
            .FirstOrDefault(m => _navigation.FindPath(state.Facility, npc.CurrentRoomId, m.RoomId).Count > 0);

    private bool RouteTouchesDoor(GameState state, string targetRoomId, Door changedDoor)
    {
        foreach (var npc in state.Crew.Where(n => n.IsAlive && n.KnowsShutdownControl))
        {
            var path = _navigation.FindPathIgnoringDoorState(state.Facility, npc.CurrentRoomId, targetRoomId);
            for (var i = 0; i + 1 < path.Count; i++)
                if (changedDoor.Connects(path[i], path[i + 1])) return true;
        }
        return false;
    }

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
        if (state.ScenarioStatus != ScenarioStatus.Running) return;

        foreach (var npc in state.Crew.Where(n => n.IsAlive && n.CurrentAction.Kind == ActionKind.ShutdownOverseer))
        {
            var mechanism = state.ShutdownMechanisms.FirstOrDefault(m => m.Id == npc.CurrentAction.TargetId && m.IsOnline);
            if (mechanism is null || !npc.CurrentRoomId.Equals(mechanism.RoomId, StringComparison.OrdinalIgnoreCase)) continue;

            npc.RoutineUntil = npc.RoutineUntil == TimeSpan.Zero ? state.Elapsed + TimeSpan.FromMinutes(mechanism.ActivationMinutes) : npc.RoutineUntil;
            if (state.Elapsed < npc.RoutineUntil) continue;

            state.ScenarioStatus = ScenarioStatus.Failed;
            state.ScenarioOutcome = $"{npc.Name} activated {mechanism.Label}. Overseer control was isolated.";
            npc.Bubble = new NpcBubble("Overseer isolated.", NpcBubbleKind.Alert, state.Elapsed, state.Elapsed + TimeSpan.FromMinutes(10));
            state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: SCENARIO FAILED — {state.ScenarioOutcome}");
            break;
        }
    }
}
