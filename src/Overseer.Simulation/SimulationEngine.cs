using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class SimulationEngine
{
    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (delta <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "Simulation time must move forward.");
        }

        state.Elapsed += delta;
        var minutes = delta.TotalMinutes;

        foreach (var npc in state.Crew)
        {
            npc.Hunger = Clamp(npc.Hunger + (0.18 * minutes));

            npc.Fatigue = npc.CurrentAction.Kind == ActionKind.Rest
                ? Clamp(npc.Fatigue - (1.4 * minutes))
                : Clamp(npc.Fatigue + (0.25 * minutes));

            npc.Fear = Clamp(npc.Fear - (0.08 * minutes));

            var pressure =
                Math.Max(0, npc.Hunger - 70)
                + Math.Max(0, npc.Fatigue - 70)
                + Math.Max(0, npc.Fear - 60);

            npc.Stress = Clamp(
                npc.Stress
                + (pressure * 0.002 * minutes)
                - (0.05 * minutes));

            if (npc.Hunger > 95)
            {
                npc.Health = Clamp(npc.Health - (0.15 * minutes));
            }
        }
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
