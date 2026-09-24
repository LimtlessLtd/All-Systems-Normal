using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class SimulationEngine
{
    public const double AwakeHungerPerMinute = 0.11;
    public const double SleepingHungerPerMinute = 0.04;
    public const double AwakeBladderPerMinute = 0.085;
    public const double SleepingBladderPerMinute = 0.035;

    private static void ConsumeMeal(GameState state, Npc npc, double amount, bool awayFromGalley)
    {
        if (npc.CarriedMealPortion > 0 || awayFromGalley)
        {
            npc.CarriedMealPortion = Math.Max(0, npc.CarriedMealPortion - amount);
            return;
        }

        state.Stores.Meals = Math.Max(0, state.Stores.Meals - amount);
    }

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

            // Sampled before any of this tick's consequences so hunger and
            // bladder use the same "was physically asleep" answer.
            var asleep = IsPhysicallyAsleep(state, npc);
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
            // Owner idea #90: a meal carried from the galley is eaten first,
            // and away from the galley it is the only food there is.
            var eating = npc.CurrentAction.Kind == ActionKind.Eat;
            var awayFromGalley = DiningSeatRules.IsAwayDiningRoom(room);
            var eatingPrepared = eating
                && (npc.CarriedMealPortion > 0 || (!awayFromGalley && state.Stores.HasMeal));
            var rawCrop = eatingPrepared || !eating || awayFromGalley
                ? null
                : ChooseRawCrop(state.Stores, npc);
            var eatingRaw = rawCrop is not null;

            if (eatingPrepared)
            {
                ConsumeMeal(state, npc, 0.07 * minutes, awayFromGalley);

                // Owner idea #9 (private coping behaviours under stress): eating a
                // prepared meal while not actually hungry is cognition choosing to
                // comfort-eat rather than responding to a real need. Give it a real
                // physical consequence — extra food burned for real stress relief —
                // instead of a no-op beyond driving Hunger further below zero.
                if (npc.Hunger < StationProvisionRules.HungryAt
                    && npc.Stress >= StationProvisionRules.ComfortEatingStressThreshold)
                {
                    ConsumeMeal(state, npc, StationProvisionRules.ComfortEatingExtraMealsPerMinute * minutes, awayFromGalley);
                    StatLogSystem.Set(
                        state,
                        npc,
                        CrewStat.Stress,
                        Clamp(
                            npc.Stress
                            - (StationProvisionRules.ComfortEatingStressReliefPerMinute * minutes)),
                        "comfort eating");
                }
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

                StatLogSystem.Set(
                    state,
                    npc,
                    CrewStat.Stress,
                    Clamp(
                        npc.Stress
                        + ((StationProvisionRules.RawFoodStressPerMinute + preferenceStress) * minutes)),
                    $"eating raw {crop.ToString().ToLowerInvariant()}");
            }

            // Owner idea #90: a seat at the table makes a meal a small comfort;
            // eating on your feet because every chair is taken is a small
            // irritation. Walking to a free chair costs nothing either way.
            if (eatingPrepared || eatingRaw)
            {
                if (DiningSeatRules.SeatedAt(state, npc) is not null)
                {
                    StatLogSystem.Set(
                        state,
                        npc,
                        CrewStat.Stress,
                        Clamp(npc.Stress - (DiningSeatRules.SeatedStressReliefPerMinute * minutes)),
                        "eating seated");
                }
                else if (DiningSeatRules.IsEatingStanding(state, npc))
                {
                    StatLogSystem.Set(
                        state,
                        npc,
                        CrewStat.Stress,
                        Clamp(npc.Stress + (DiningSeatRules.StandingStressPerMinute * minutes)),
                        "no free seat to eat at");
                }
            }

            // A person physically asleep in a bed burns far less than one on
            // shift (and the bladder fills more slowly), so a full night's
            // sleep no longer guarantees waking up starving or bursting, which
            // used to pull every sleeper out of bed mid-night.
            var hungerDelta = eatingPrepared
                ? -1.9
                : rawCrop is { } activeCrop
                    ? -CropRules.RawHungerReliefPerMinute(activeCrop)
                    : asleep
                        ? SleepingHungerPerMinute
                        : AwakeHungerPerMinute;
            StatLogSystem.Set(
                state,
                npc,
                CrewStat.Hunger,
                Clamp(npc.Hunger + (hungerDelta * minutes)),
                eatingPrepared
                    ? "eating a prepared meal"
                    : rawCrop is { } eatenCrop
                        ? $"eating raw {eatenCrop.ToString().ToLowerInvariant()}"
                        : asleep
                            ? "metabolism while asleep"
                            : "metabolism");

            // The meal they brought is finished; there is nothing else to eat
            // here, so they stop. What next is the mind's choice.
            if (eating && awayFromGalley && npc.CarriedMealPortion <= 0)
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    "Finished the meal I brought.");
            }

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
            var restFixture = FindPhysicalRestFixture(state, npc);
            var sleeping = restFixture is not null;
            var scheduledSleep = CrewDutySchedule.IsSleepWindow(npc, state.Elapsed);

            if (restFixture is { Type: FixtureType.Bed })
            {
                PersonalSpaceSystem.RecordUse(
                    npc,
                    npc.CurrentRoomId,
                    restFixture,
                    minutes);
            }

            // Owner idea #65: a noisy room (running or worn machinery here or
            // through an open hatch) makes the same time in bed less restful.
            var restRecovery = sleeping
                ? StationNoiseSystem.RestRecoveryFactor(StationNoiseSystem.RoomLevel(state, npc.CurrentRoomId))
                : 1;

            if (scheduledSleep && !sleeping)
                npc.SleepDebtMinutes = Math.Clamp(npc.SleepDebtMinutes + minutes, 0, 16 * 60);
            else if (sleeping)
                npc.SleepDebtMinutes = Math.Clamp(npc.SleepDebtMinutes - (2.2 * restRecovery * minutes), 0, 16 * 60);

            var fatigueRate = sleeping
                ? -0.9 * restRecovery
                : 0.065
                    + (scheduledSleep ? 0.055 : 0)
                    + (Math.Min(360, npc.SleepDebtMinutes) / 12000d);
            StatLogSystem.Set(
                state,
                npc,
                CrewStat.Fatigue,
                Clamp(npc.Fatigue + (fatigueRate * minutes)),
                restFixture is null
                    ? scheduledSleep ? "awake in sleep window" : "awake"
                    : restFixture.Type == FixtureType.Sofa
                        ? "resting on a sofa"
                        : restRecovery < 1 ? "sleeping in a noisy room" : "sleeping in bed");
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

            // Owner idea #20: the washroom toilet is a real capacity-1
            // physical resource. Merely carrying a UseToilet action does not
            // provide relief before arrival, and if two crew overlap the same
            // interaction point only one deterministic occupant can use it.
            var usingToilet = IsUsingExclusiveFixture(
                state,
                npc,
                FixtureType.Toilet,
                ActionKind.UseToilet);
            npc.BladderNeed = Clamp(
                npc.BladderNeed
                + ((usingToilet ? -3.0
                    : asleep ? SleepingBladderPerMinute
                    : AwakeBladderPerMinute) * minutes));

            // Owner idea #92: each recreation activity is worth what its
            // fixture offers; a dead TV or console offers nothing.
            var recreationRelief = RecreationActivityRules.ReliefPerMinute(state, npc);
            npc.RecreationNeed = Clamp(
                npc.RecreationNeed
                + ((recreationRelief > 0 ? -recreationRelief : 0.045) * minutes));

            npc.SocialNeed = Clamp(
                npc.SocialNeed
                + ((npc.CurrentAction.Kind is ActionKind.Talk
                    or ActionKind.Socialize
                    or ActionKind.Intimacy
                        ? -1.15
                        : RecreationActivityRules.IsWatchingWithOthers(state, npc)
                            ? -RecreationActivityRules.SharedViewingSocialReliefPerMinute
                            : 0.04) * minutes));

            if (RecreationActivityRules.Current(npc) is { StressReliefPerMinute: > 0 } calming
                && recreationRelief > 0)
            {
                StatLogSystem.Set(
                    state,
                    npc,
                    CrewStat.Stress,
                    Clamp(npc.Stress - (calming.StressReliefPerMinute * minutes)),
                    calming.Doing);
            }

            npc.IntimacyNeed = Clamp(
                npc.IntimacyNeed
                + ((npc.CurrentAction.Kind == ActionKind.Intimacy ? -1.6 : 0.045) * minutes));

            StatLogSystem.Set(
                state,
                npc,
                CrewStat.Fear,
                Clamp(
                    npc.Fear
                    - (0.08 * minutes)
                    - (courageModifier * 0.012 * minutes)),
                "calming down");

            var environmentalStress = 0d;
            var environmentalCauses = new List<(string Cause, double Rate)>();

            if (!room.IsPowered)
            {
                environmentalStress += 0.55;
                environmentalCauses.Add(("room without power", 0.55));
                StatLogSystem.Set(state, npc, CrewStat.Fear, Clamp(npc.Fear + (0.18 * minutes)), "room without power");
            }

            if (!room.LightsOn)
            {
                environmentalStress += 0.22;
                environmentalCauses.Add(("lights off", 0.22));
            }

            if (room.OxygenPercent < 19.5)
            {
                environmentalStress += 1.4;
                environmentalCauses.Add(("low oxygen", 1.4));
                StatLogSystem.Set(state, npc, CrewStat.Fear, Clamp(npc.Fear + (0.8 * minutes)), "low oxygen");
            }

            if (room.OxygenPercent < 17
                && !StationHazardSystem.HullRepairRules.HasEmergencyPressureProtection(npc, room.Id))
            {
                StatLogSystem.Set(
                    state,
                    npc,
                    CrewStat.Health,
                    Clamp(
                        npc.Health
                        - ((17 - room.OxygenPercent) * 0.12 * minutes)),
                    "oxygen deprivation");
            }

            if (room.CarbonDioxidePercent > 1)
            {
                var co2Stress = Math.Min(
                    2.2,
                    (room.CarbonDioxidePercent - 1) * 0.8);
                environmentalStress += co2Stress;
                environmentalCauses.Add(("high CO₂", co2Stress));
            }

            if (room.CarbonDioxidePercent > 3)
            {
                StatLogSystem.Set(
                    state,
                    npc,
                    CrewStat.Health,
                    Clamp(
                        npc.Health
                        - ((room.CarbonDioxidePercent - 3) * 0.08 * minutes)),
                    "CO₂ poisoning");
            }

            if (room.PressureKpa < 70)
            {
                environmentalStress += 2.4;
                environmentalCauses.Add(("low pressure", 2.4));
                StatLogSystem.Set(state, npc, CrewStat.Fear, Clamp(npc.Fear + (1.5 * minutes)), "low pressure");
                if (!StationHazardSystem.HullRepairRules.HasEmergencyPressureProtection(npc, room.Id))
                {
                    StatLogSystem.Set(
                        state,
                        npc,
                        CrewStat.Health,
                        Clamp(
                            npc.Health
                            - ((70 - room.PressureKpa) * 0.08 * minutes)),
                        "low pressure");
                }
            }

            if (room.TemperatureC is < 16 or > 28)
            {
                environmentalStress += 0.65;
                environmentalCauses.Add((room.TemperatureC < 16 ? "cold room" : "hot room", 0.65));
            }

            if (room.TemperatureC < 5)
            {
                StatLogSystem.Set(
                    state,
                    npc,
                    CrewStat.Health,
                    Clamp(
                        npc.Health
                        - ((5 - room.TemperatureC) * 0.025 * minutes)),
                    "extreme cold");
            }
            else if (room.TemperatureC > 38)
            {
                StatLogSystem.Set(
                    state,
                    npc,
                    CrewStat.Health,
                    Clamp(
                        npc.Health
                        - ((room.TemperatureC - 38) * 0.035 * minutes)),
                    "extreme heat");
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

            var stressBefore = npc.Stress;
            npc.Stress = Clamp(
                npc.Stress
                + (pressure * 0.003 * minutes)
                + (environmentalStress * stressMultiplier * minutes)
                - (0.04 * minutes));

            var stressParts = new List<(string Cause, double Delta)>
            {
                ("unmet needs", pressure * 0.003 * minutes),
                ("natural recovery", -0.04 * minutes)
            };
            stressParts.AddRange(environmentalCauses.Select(part =>
                (part.Cause, part.Rate * stressMultiplier * minutes)));
            StatLogSystem.RecordParts(state, npc, CrewStat.Stress, npc.Stress - stressBefore, stressParts);

            if (npc.Hunger > 95)
            {
                StatLogSystem.Set(state, npc, CrewStat.Health, Clamp(npc.Health - (0.15 * minutes)), "starvation");
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

    private static bool IsUsingExclusiveFixture(
        GameState state,
        Npc npc,
        FixtureType fixtureType,
        ActionKind action)
    {
        if (!npc.IsPresent
            || npc.CurrentAction.Kind != action
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            return false;
        }

        var fixture = room.Fixtures.FirstOrDefault(candidate =>
            candidate.Type == fixtureType);
        if (fixture is null
            || !LocalMovementSystem.IsAtInteractionPoint(room, npc, fixture))
        {
            return false;
        }

        var occupant = state.Crew
            .Where(other =>
                other.IsAlive
                && other.IsPresent
                && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                && other.CurrentAction.Kind == action
                && LocalMovementSystem.IsAtInteractionPoint(room, other, fixture))
            .OrderBy(other => other.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(other => other.Id)
            .FirstOrDefault();

        return occupant?.Id == npc.Id;
    }

    /// <summary>
    /// Actually asleep: carrying a Sleep action while physically at a bed.
    /// A remote Sleep flag, or resting on a sofa, does not count.
    /// </summary>
    public static bool IsPhysicallyAsleep(GameState state, Npc npc) =>
        npc.CurrentAction.Kind == ActionKind.Sleep
        && FindPhysicalRestFixture(state, npc) is not null;

    private static RoomFixture? FindPhysicalRestFixture(GameState state, Npc npc)
    {
        if (npc.CurrentAction.Kind is not (ActionKind.Rest or ActionKind.Sleep)
            || !state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room))
        {
            return null;
        }

        IEnumerable<RoomFixture> fixtures;
        if (npc.CurrentAction.Kind == ActionKind.Sleep)
        {
            var assigned = BedUseRules.AssignedBed(state, npc);
            fixtures = assigned is null ? [] : [assigned];
        }
        else
        {
            fixtures = room.Fixtures.Where(fixture =>
                fixture.Type is FixtureType.Bed
                    or FixtureType.MedicalBed
                    or FixtureType.Sofa);
        }

        foreach (var fixture in fixtures)
        {
            // Local movement stops at the fixture's collision-safe interaction
            // point, while authored/tests may place an actor directly on the bed
            // footprint (lying down). Both are genuine physical attendance; a
            // remote Sleep flag alone is never restorative.
            if (LocalMovementSystem.IsAtInteractionPoint(room, npc, fixture))
                return fixture;

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

            if (Math.Sqrt((dx * dx) + (dy * dy)) <= 0.10)
                return fixture;
        }

        return null;
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
