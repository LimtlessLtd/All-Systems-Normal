using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic authority over prisoner containment: whether a held prisoner
/// finds and takes a physical opportunity to breach containment, and whether a
/// crew member restraining an escapee succeeds, fails, or the struggle turns
/// lethal. Cognition may choose to attempt <see cref="ActionKind.RecapturePrisoner"/>
/// or leave a hatch unsecured; it never decides the outcome.
/// </summary>
public sealed class PrisonerContainmentSystem
{
    public const string ContainmentRoomId = "containment";

    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        TickRecaptureAttempts(state);
        TickEscapeAttempts(state);
    }

    public static bool IsCoLocated(Npc first, Npc second) =>
        first.CurrentRoomId.Equals(second.CurrentRoomId, StringComparison.OrdinalIgnoreCase);

    private static void TickRecaptureAttempts(GameState state)
    {
        foreach (var guard in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.RecapturePrisoner))
        {
            var prisoner = FindEscapedPrisoner(state, guard.CurrentAction.TargetId);
            if (prisoner is null || !IsCoLocated(guard, prisoner))
            {
                End(guard, "The escaped prisoner is no longer within reach.");
                continue;
            }

            if (!BeginOrComplete(state, guard, 2, $"restraining {prisoner.Name}"))
            {
                continue;
            }

            ResolveRecapture(state, guard, prisoner);
        }
    }

    private static void TickEscapeAttempts(GameState state)
    {
        var minute = (int)state.Elapsed.TotalMinutes;

        foreach (var prisoner in state.Crew.Where(npc =>
                     npc.IsPrisoner
                     && npc.IsAlive
                     && npc.IsPresent
                     && !npc.HasEscapedContainment
                     && npc.CurrentRoomId.Equals(ContainmentRoomId, StringComparison.OrdinalIgnoreCase)))
        {
            var breachDoor = BreachableDoor(state);
            if (breachDoor is null)
            {
                continue;
            }

            var tierPressure = prisoner.PrisonerDangerLevel switch
            {
                PrisonerDangerLevel.Extreme => 50,
                PrisonerDangerLevel.High => 32,
                PrisonerDangerLevel.Moderate => 16,
                _ => 6
            };

            var pressure = tierPressure
                + prisoner.PrisonerViolenceBias * 1.4
                + Math.Max(0, prisoner.Stress - 50) * 0.4;

            if (pressure < 18)
            {
                continue;
            }

            var chance = Math.Clamp((pressure - 14) / 400.0, 0.002, 0.05);
            var roll = StableRoll(prisoner.Name, ContainmentRoomId, minute, "escape");

            if (roll >= chance * 1000)
            {
                continue;
            }

            var destinationRoomId = breachDoor.RoomAId.Equals(
                ContainmentRoomId,
                StringComparison.OrdinalIgnoreCase)
                ? breachDoor.RoomBId
                : breachDoor.RoomAId;

            prisoner.CurrentRoomId = destinationRoomId;
            prisoner.HasEscapedContainment = true;
            prisoner.Movement = null;
            prisoner.Intent = null;
            prisoner.RoutineUntil = TimeSpan.Zero;
            prisoner.Bubble = new NpcBubble(
                "Out. I'm not going back in there.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(4));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Critical,
                prisoner.Id.ToString(),
                destinationRoomId);

            Log(
                state,
                $"CONTAINMENT BREACH: {prisoner.Name} slips past an unsecured hatch and is now at large.");
        }
    }

    private static void ResolveRecapture(GameState state, Npc guard, Npc prisoner)
    {
        var guardScore = CrewCounterplaySystem.BestForceSkill(guard);
        var prisonerResistance = prisoner.PrisonerDangerLevel switch
        {
            PrisonerDangerLevel.Extreme => 92,
            PrisonerDangerLevel.High => 70,
            PrisonerDangerLevel.Moderate => 46,
            _ => 26
        }
            + prisoner.PrisonerViolenceBias
            + (prisoner.Skills.TryGetValue("Athletics", out var athletics) ? athletics * 0.3 : 0);

        var minute = (int)state.Elapsed.TotalMinutes;
        var successChance = Math.Clamp(0.5 + (guardScore - prisonerResistance) / 150.0, 0.05, 0.95);
        var roll = StableRoll(guard.Name, prisoner.Name, minute, "recapture");

        if (roll < successChance * 1000)
        {
            prisoner.HasEscapedContainment = false;
            prisoner.CurrentRoomId = ContainmentRoomId;
            prisoner.Movement = null;
            prisoner.Intent = null;
            prisoner.Stress = Math.Clamp(prisoner.Stress + 12, 0, 100);

            AudioCueSystem.Emit(state, AudioCueKind.Important, guard.Id.ToString(), guard.CurrentRoomId);
            Log(state, $"{guard.Name} recaptures {prisoner.Name} and returns them to Containment.");
            End(guard, $"{prisoner.Name} is restrained and returned to Containment.");
            return;
        }

        var tierDamage = prisoner.PrisonerDangerLevel switch
        {
            PrisonerDangerLevel.Extreme => 26,
            PrisonerDangerLevel.High => 16,
            PrisonerDangerLevel.Moderate => 8,
            _ => 3
        };

        var guardDamage = 6 + tierDamage
            + (prisoner.PrisonerViolenceBias * 0.6)
            + (StableRoll(prisoner.Name, guard.Name, minute, "guard-hurt") % 9);
        var prisonerDamage = 5 + (guardScore * 0.12)
            + (StableRoll(guard.Name, prisoner.Name, minute, "prisoner-hurt") % 7);

        guard.Health = Math.Clamp(guard.Health - guardDamage, 0, 100);
        prisoner.Health = Math.Clamp(prisoner.Health - prisonerDamage, 0, 100);
        guard.Fear = Math.Clamp(guard.Fear + 20, 0, 100);
        guard.Stress = Math.Clamp(guard.Stress + 18, 0, 100);
        prisoner.Stress = Math.Clamp(prisoner.Stress + 10, 0, 100);

        if (guard.Health <= 0)
        {
            guard.CauseOfDeath = $"Killed by {prisoner.Name} while attempting recapture.";
            AudioCueSystem.Emit(state, AudioCueKind.Critical, guard.Id.ToString(), guard.CurrentRoomId);
            Log(state, $"CRITICAL: {guard.Name} is killed by {prisoner.Name} during a recapture attempt.");
            End(guard, "Deceased.");
            return;
        }

        if (prisoner.Health <= 0)
        {
            prisoner.CauseOfDeath = $"Killed by {guard.Name} during a recapture struggle.";
            prisoner.CurrentAction = new NpcAction(ActionKind.Idle, null, "Deceased.");
            AudioCueSystem.Emit(state, AudioCueKind.Critical, prisoner.Id.ToString(), prisoner.CurrentRoomId);
            Log(state, $"CRITICAL: {prisoner.Name} is killed by {guard.Name} during a recapture struggle.");
            End(guard, $"{prisoner.Name} dies resisting recapture.");
            return;
        }

        Log(
            state,
            $"{prisoner.Name} breaks free of {guard.Name}'s recapture attempt and remains at large.");
        End(guard, $"{prisoner.Name} breaks free and remains at large.");
    }

    private static Npc? FindEscapedPrisoner(GameState state, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : state.Crew.FirstOrDefault(npc =>
                npc.IsPrisoner
                && npc.IsAlive
                && npc.IsPresent
                && npc.HasEscapedContainment
                && npc.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static Door? BreachableDoor(GameState state) =>
        state.Facility.Doors
            .Where(door =>
                (door.RoomAId.Equals(ContainmentRoomId, StringComparison.OrdinalIgnoreCase)
                    || door.RoomBId.Equals(ContainmentRoomId, StringComparison.OrdinalIgnoreCase))
                && door.IsPassable)
            .OrderBy(door => door.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool BeginOrComplete(
        GameState state,
        Npc npc,
        int minutes,
        string activity)
    {
        if (npc.RoutineUntil == TimeSpan.Zero)
        {
            npc.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(minutes);
            npc.Bubble = new NpcBubble(
                $"I'm {activity}.",
                NpcBubbleKind.Alert,
                state.Elapsed,
                state.Elapsed + TimeSpan.FromMinutes(Math.Max(2, minutes)));
            Log(state, $"{npc.Name} begins {activity}.");
            return false;
        }

        return state.Elapsed >= npc.RoutineUntil;
    }

    private static void End(Npc npc, string reason)
    {
        npc.RoutineUntil = TimeSpan.Zero;
        npc.Intent = null;
        npc.CurrentAction = new NpcAction(ActionKind.Idle, null, reason);
    }

    private static int StableRoll(string first, string second, int minute, string salt)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in $"{first}|{second}|{minute}|{salt}")
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (int)(hash % 1000);
        }
    }

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
