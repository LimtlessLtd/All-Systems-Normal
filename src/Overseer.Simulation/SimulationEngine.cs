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

            npc.Hunger = Clamp(
                npc.Hunger
                + ((npc.CurrentAction.Kind == ActionKind.Eat ? -1.9 : 0.11) * minutes));

            npc.Fatigue = npc.CurrentAction.Kind is ActionKind.Rest or ActionKind.Sleep
                ? Clamp(npc.Fatigue - (0.9 * minutes))
                : Clamp(npc.Fatigue + (0.065 * minutes));

            npc.HygieneNeed = Clamp(
                npc.HygieneNeed
                + ((npc.CurrentAction.Kind == ActionKind.Shower ? -2.2
                    : npc.CurrentAction.Kind == ActionKind.Groom ? -1.0
                    : 0.055) * minutes));

            npc.RecreationNeed = Clamp(
                npc.RecreationNeed
                + ((npc.CurrentAction.Kind == ActionKind.Recreate ? -1.45 : 0.045) * minutes));

            npc.SocialNeed = Clamp(
                npc.SocialNeed
                + ((npc.CurrentAction.Kind is ActionKind.Talk
                    or ActionKind.Socialize
                    or ActionKind.Intimacy
                        ? -1.15
                        : 0.04) * minutes));

            npc.IntimacyNeed = Clamp(
                npc.IntimacyNeed
                + ((npc.CurrentAction.Kind == ActionKind.Intimacy ? -1.6 : 0.025) * minutes));

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
                + Math.Max(0, npc.Fear - 55)
                + (Math.Max(0, npc.HygieneNeed - 70) * 0.35)
                + (Math.Max(0, npc.RecreationNeed - 75) * 0.25)
                + (Math.Max(0, npc.SocialNeed - 75) * 0.3);

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
