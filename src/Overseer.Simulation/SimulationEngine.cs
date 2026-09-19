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
            var stressResistance = Math.Clamp(
                CrewTraitMath.Modifier(npc, TraitEffectKind.StressResistance),
                -25,
                25);
            var courageModifier = Math.Clamp(
                CrewTraitMath.Modifier(npc, TraitEffectKind.Courage),
                -25,
                25);

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

            npc.BladderNeed = Clamp(
                npc.BladderNeed
                + ((npc.CurrentAction.Kind == ActionKind.UseToilet ? -3.0 : 0.085) * minutes));

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
                + ((npc.CurrentAction.Kind == ActionKind.Intimacy ? -1.6 : 0.045) * minutes));

            npc.Fear = Clamp(
                npc.Fear
                - (0.08 * minutes)
                - (courageModifier * 0.012 * minutes));

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

            if (room.OxygenPercent < 17)
            {
                npc.Health = Clamp(
                    npc.Health
                    - ((17 - room.OxygenPercent) * 0.12 * minutes));
            }

            if (room.CarbonDioxidePercent > 1)
            {
                environmentalStress += Math.Min(
                    2.2,
                    (room.CarbonDioxidePercent - 1) * 0.8);
            }

            if (room.CarbonDioxidePercent > 3)
            {
                npc.Health = Clamp(
                    npc.Health
                    - ((room.CarbonDioxidePercent - 3) * 0.08 * minutes));
            }

            if (room.PressureKpa < 70)
            {
                environmentalStress += 2.4;
                npc.Fear = Clamp(npc.Fear + (1.5 * minutes));
                npc.Health = Clamp(
                    npc.Health
                    - ((70 - room.PressureKpa) * 0.08 * minutes));
            }

            if (room.TemperatureC is < 16 or > 28)
            {
                environmentalStress += 0.65;
            }

            if (room.TemperatureC < 5)
            {
                npc.Health = Clamp(
                    npc.Health
                    - ((5 - room.TemperatureC) * 0.025 * minutes));
            }
            else if (room.TemperatureC > 38)
            {
                npc.Health = Clamp(
                    npc.Health
                    - ((room.TemperatureC - 38) * 0.035 * minutes));
            }

            var pressure =
                Math.Max(0, npc.Hunger - 65)
                + Math.Max(0, npc.Fatigue - 65)
                + Math.Max(0, npc.Fear - 55)
                + (Math.Max(0, npc.HygieneNeed - 70) * 0.35)
                + (Math.Max(0, npc.RecreationNeed - 75) * 0.25)
                + (Math.Max(0, npc.SocialNeed - 75) * 0.3);

            var stressMultiplier = Math.Clamp(
                1 - (stressResistance / 100d),
                0.65,
                1.35);

            npc.Stress = Clamp(
                npc.Stress
                + (pressure * 0.003 * minutes)
                + (environmentalStress * stressMultiplier * minutes)
                - (0.04 * minutes));

            if (npc.Hunger > 95)
            {
                npc.Health = Clamp(npc.Health - (0.15 * minutes));
            }

            if (npc.Health <= 0 && npc.CauseOfDeath is null)
            {
                npc.Health = 0;
                npc.CauseOfDeath = DetermineCauseOfDeath(npc, room);
                npc.Intent = null;
                npc.Movement = null;
                npc.RoutineUntil = TimeSpan.Zero;
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    "Deceased.");

                AudioCueSystem.Emit(
                    state,
                    AudioCueKind.Critical,
                    npc.Id.ToString(),
                    npc.CurrentRoomId);

                state.EventLog.Insert(
                    0,
                    $"T+{state.Elapsed:hh\\:mm}: CRITICAL: {npc.Name} has died — {npc.CauseOfDeath}");
            }
        }
    }

    private static string DetermineCauseOfDeath(Npc npc, Room room)
    {
        if (room.PressureKpa < 55)
            return "Died from decompression.";

        if (room.OxygenPercent < 17)
            return "Died from oxygen deprivation.";

        if (room.CarbonDioxidePercent > 3)
            return "Died from carbon dioxide exposure.";

        if (room.TemperatureC < 5)
            return "Died from extreme cold.";

        if (room.TemperatureC > 38)
            return "Died from extreme heat.";

        if (npc.Hunger > 95)
            return "Died from starvation.";

        return "Died from critical physiological stress.";
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
