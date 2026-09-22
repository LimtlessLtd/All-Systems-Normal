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

            // Eating only helps if food physically exists. Prepared meals are
            // efficient; raw hydroponic crops are an emergency fallback with
            // substantially weaker hunger relief and a morale/stress cost.
            var eatingPrepared = npc.CurrentAction.Kind == ActionKind.Eat && state.Stores.HasMeal;
            var rawCrop = eatingPrepared || npc.CurrentAction.Kind != ActionKind.Eat
                ? null
                : ChooseRawCrop(state.Stores, npc);
            var eatingRaw = rawCrop is not null;

            if (eatingPrepared)
            {
                state.Stores.Meals = Math.Max(0, state.Stores.Meals - (0.07 * minutes));
            }
            else if (rawCrop is { } crop)
            {
                state.Stores.RawCrops[crop] = Math.Max(
                    0,
                    state.Stores.RawCrops[crop] - (StationProvisionRules.RawCropUnitsPerMinute * minutes));

                var rawTarget = $"raw:{crop}";
                if (!string.Equals(npc.CurrentAction.TargetId, rawTarget, StringComparison.Ordinal))
                {
                    npc.CurrentAction = new NpcAction(
                        ActionKind.Eat,
                        rawTarget,
                        $"Eating raw {crop.ToString().ToLowerInvariant()} because no prepared meal is available.");
                    state.EventLog.Insert(
                        0,
                        $"T+{state.Elapsed:hh\\:mm}: {npc.Name} resorts to eating raw {crop.ToString().ToLowerInvariant()}.");
                }

                var preference = FoodPreferenceRules.PreferenceScore(npc, crop);
                var preferenceStress = preference switch
                {
                    <= -2 => 0.22,
                    >= 2 => -0.04,
                    _ => 0.08
                };

                if (preference <= -2)
                {
                    npc.DislikedFoodExposureMinutes += minutes;
                }

                npc.Stress = Clamp(
                    npc.Stress
                    + ((StationProvisionRules.RawFoodStressPerMinute + preferenceStress) * minutes));
            }

            var hungerDelta = eatingPrepared
                ? -1.9
                : rawCrop is { } activeCrop
                    ? -CropRules.RawHungerReliefPerMinute(activeCrop)
                    : 0.11;
            npc.Hunger = Clamp(npc.Hunger + (hungerDelta * minutes));

            // Crossing into a serious physiological need requests fresh
            // cognition; it does not choose the response. Browser/LLM minds
            // still decide what the person wants to do, while C# continues to
            // own the need, navigation and consequences.
            if (npc.Hunger >= 72
                && npc.Intent?.Action != ActionKind.Eat
                && npc.CurrentAction.Kind != ActionKind.Eat)
            {
                npc.NeedsMindReconsideration = true;
            }

            // Merely having "Sleep" in CurrentAction is not restorative.
            // The person must physically reach a bed/rest fixture first so sleep
            // remains visible station behaviour rather than a remote state flag.
            var sleeping = IsPhysicallyResting(state, npc);
            var scheduledSleep = CrewDutySchedule.IsSleepWindow(npc, state.Elapsed);

            if (scheduledSleep && !sleeping)
                npc.SleepDebtMinutes = Math.Clamp(npc.SleepDebtMinutes + minutes, 0, 16 * 60);
            else if (sleeping)
                npc.SleepDebtMinutes = Math.Clamp(npc.SleepDebtMinutes - (2.2 * minutes), 0, 16 * 60);

            var fatigueRate = sleeping
                ? -0.9
                : 0.065
                    + (scheduledSleep ? 0.055 : 0)
                    + (Math.Min(360, npc.SleepDebtMinutes) / 12000d);
            npc.Fatigue = Clamp(npc.Fatigue + (fatigueRate * minutes));
            if (npc.Fatigue >= 86
                && npc.Intent?.Action is not (ActionKind.Rest or ActionKind.Sleep)
                && npc.CurrentAction.Kind is not (ActionKind.Rest or ActionKind.Sleep))
            {
                npc.NeedsMindReconsideration = true;
            }

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
                + (Math.Max(0, npc.SleepDebtMinutes - 60) / 8)
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

    private static bool IsPhysicallyResting(GameState state, Npc npc)
    {
        if (npc.CurrentAction.Kind is not (ActionKind.Rest or ActionKind.Sleep)
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            return false;
        }

        var fixtures = room.Fixtures.Where(fixture =>
            npc.CurrentAction.Kind == ActionKind.Sleep
                ? fixture.Type is FixtureType.Bed or FixtureType.MedicalBed
                : fixture.Type is FixtureType.Bed
                    or FixtureType.MedicalBed
                    or FixtureType.Sofa);

        foreach (var fixture in fixtures)
        {
            // Local movement deliberately stops at a collision-safe interaction
            // point beside furniture, not at the fixture centre. Measure physical
            // distance to the bed/sofa footprint so "at the bed" and movement's
            // safe stand-off point use the same geometry.
            var nearestX = Math.Clamp(
                npc.PositionX,
                fixture.X - (fixture.Width / 2),
                fixture.X + (fixture.Width / 2));
            var nearestY = Math.Clamp(
                npc.PositionY,
                fixture.Y - (fixture.Height / 2),
                fixture.Y + (fixture.Height / 2));
            var dx = (npc.PositionX - nearestX) / 100d * room.MapWidth;
            var dy = (npc.PositionY - nearestY) / 100d * room.MapHeight;

            if (Math.Sqrt((dx * dx) + (dy * dy)) <= 1.35)
                return true;
        }

        return false;
    }

    private static CropKind? ChooseRawCrop(StationStores stores, Npc npc) =>
        stores.RawCrops
            .Where(pair => pair.Value > 0 && CropRules.IsEdibleRaw(pair.Key))
            .OrderByDescending(pair => FoodPreferenceRules.PreferenceScore(npc, pair.Key))
            .ThenByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Select(pair => (CropKind?)pair.Key)
            .FirstOrDefault();

    private static string DetermineCauseOfDeath(Npc npc, Room room)
    {
        if (room.PressureKpa < 55)
            return "Died from decompression.";

        if (room.OxygenPercent < 17)
            return "Died from oxygen deprivation.";

        if (room.SmokePercent >= 55)
            return "Died from smoke inhalation.";

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
