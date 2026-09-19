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

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive))
        {
            var room = state.Facility.Rooms[npc.CurrentRoomId];

            npc.Hunger = Clamp(npc.Hunger + (0.18 * minutes));

            npc.Fatigue = npc.CurrentAction.Kind == ActionKind.Rest
                ? Clamp(npc.Fatigue - (1.4 * minutes))
                : Clamp(npc.Fatigue + (0.25 * minutes));

            npc.Fear = Clamp(npc.Fear - (0.08 * minutes));

            var environmentalStress = 0d;

            if (!room.IsPowered)
            {
                environmentalStress += 0.55;
                npc.Fear = Clamp(npc.Fear + (0.18 * minutes));
            }

            if (!room.LightsOn)
            {
                environmentalStress += 0.22;
            }

            if (room.OxygenPercent < 19.5)
            {
                environmentalStress += 1.4;
                npc.Fear = Clamp(npc.Fear + (0.8 * minutes));
            }

            if (room.TemperatureC is < 16 or > 28)
            {
                environmentalStress += 0.65;
            }

            var pressure =
                Math.Max(0, npc.Hunger - 65)
                + Math.Max(0, npc.Fatigue - 65)
                + Math.Max(0, npc.Fear - 55);

            npc.Stress = Clamp(
                npc.Stress
                + (pressure * 0.003 * minutes)
                + (environmentalStress * minutes)
                - (0.04 * minutes));

            if (npc.Hunger > 95)
            {
                npc.Health = Clamp(npc.Health - (0.15 * minutes));
            }
        }
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
