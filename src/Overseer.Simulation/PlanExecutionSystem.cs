using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic foundation for owner idea #5 (crew-generated multi-step
/// plans): promotes the next queued <see cref="NpcPlanStep"/> into an NPC's
/// <see cref="Npc.Intent"/> once the current intent completes, so a plan is
/// pursued one already-validated step at a time rather than all at once.
/// Runs before cognition each tick (see <c>StationSession.AdvanceCoreAsync</c>)
/// so a continuing plan already has a live <see cref="Npc.Intent"/> by the
/// time cognition decides whether this NPC needs a fresh goal — matching the
/// existing "do not erase a goal already being pursued" rule ordinary
/// intents get. <see cref="IntentExecutionSystem"/> validates the promoted
/// step exactly like any freshly decided intent and clears
/// <see cref="Npc.Plan"/> if that step fails, expires or is pre-empted,
/// rather than continuing a plan whose premise may no longer hold.
/// Nothing produces an <see cref="NpcPlan"/> yet — this system is inert
/// until a future cognition slice starts proposing one.
/// </summary>
public sealed class PlanExecutionSystem
{
    public void Tick(GameState state)
    {
        foreach (var npc in state.Crew.Where(npc =>
            npc.IsAlive && npc.Intent is null && npc.Plan is { Steps.Count: > 0 }))
        {
            var plan = npc.Plan!;
            var step = plan.Steps[0];

            npc.Intent = new NpcIntent(
                step.Action,
                step.TargetId,
                step.Goal,
                plan.Reason,
                plan.Urgency,
                plan.Source,
                state.Elapsed,
                step.SubjectId);
            npc.Plan = plan.WithoutFirstStep();
        }
    }
}
