using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic station hazard physics. This system creates and advances the
/// world problem; cognition chooses what it wants to do about it through the
/// generic crew affordances exposed elsewhere.
/// </summary>
public sealed class StationHazardSystem
{
    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (delta <= TimeSpan.Zero) return;

        TryIgniteEquipment(state);
        AdvanceFires(state, delta);
        PropagateSmoke(state, delta);
        ApplySmokeExposure(state, delta);
        WakeRemoteFireResponders(state);
    }

    private static void TryIgniteEquipment(GameState state)
    {
        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);
        if (minute <= 0 || minute % 12 != 0)
            return;

        foreach (var device in state.Devices.Values
                     .Where(device => device.Condition <= device.DegradedAt)
                     .OrderBy(device => device.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!state.Facility.Rooms.TryGetValue(device.RoomId, out var room)
                || room.Type == RoomType.Corridor
                || room.FireIntensity > 0
                || room.OxygenPercent <= 1)
                continue;

            var conditionRisk = Math.Clamp((device.DegradedAt - device.Condition) / 140d, 0, .22);
            var roomRisk = room.Type is RoomType.Reactor or RoomType.Generator
                ? .035
                : room.Type == RoomType.Kitchen ? .02 : .008;
            var chance = Math.Clamp(.008 + conditionRisk + roomRisk, .008, .28);

            if (StableRoll(state.UpkeepSeed, minute, room.Id, device.Id, "ignite") >= chance)
                continue;

            var origin = FireFrontRules.MachineOrigin(room, device);
            FireFrontRules.Ignite(
                room,
                origin.X,
                origin.Y,
                Math.Clamp(16 + ((device.DegradedAt - device.Condition) * .35), 14, 38));
            room.SmokePercent = Math.Max(room.SmokePercent, 4);
            Log(state, $"FIRE: {device.Label} ignites in {room.Name}.");
            AudioCueSystem.Emit(state, AudioCueKind.Critical, roomId: room.Id);

            foreach (var npc in state.Crew.Where(n =>
                         n.IsAlive && n.IsPresent
                         && n.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
            {
                npc.NeedsMindReconsideration = true;
                StatLogSystem.Set(state, npc, CrewStat.Fear, Math.Clamp(npc.Fear + 20, 0, 100), $"fire broke out in {room.Name}");
                StatLogSystem.Set(state, npc, CrewStat.Stress, Math.Clamp(npc.Stress + 12, 0, 100), $"fire broke out in {room.Name}");
            }
        }
    }

    private static void AdvanceFires(GameState state, TimeSpan delta)
    {
        var minutes = delta.TotalMinutes;
        foreach (var room in state.Facility.Rooms.Values)
        {
            FireFrontRules.ClearIfOut(room);
        }

        var burning = state.Facility.Rooms.Values
            .Where(room => room.FireIntensity > 0)
            .OrderBy(room => room.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var room in burning)
        {
            // Combustion cannot persist without oxidizer. Previously a 60%
            // fire at 0% O2 took almost 100 simulated minutes to decay while
            // still making heat/smoke, damaging the hull and remaining eligible
            // to spread. Extinguish before applying any combustion consequences.
            if (room.OxygenPercent <= 1)
            {
                room.FireIntensity = 0;
                Log(state, $"Fire in {room.Name} goes out from oxygen starvation.");
                continue;
            }

            var intensity = room.FireIntensity;
            var oxygenAvailability = Math.Clamp(room.OxygenPercent / 18d, 0, 1);
            room.OxygenPercent = Math.Clamp(
                room.OxygenPercent - (intensity * .00055 * oxygenAvailability * minutes),
                0,
                23);
            room.CarbonDioxidePercent = Math.Clamp(
                room.CarbonDioxidePercent + (intensity * .00038 * oxygenAvailability * minutes),
                0,
                20);
            room.TemperatureC = Math.Clamp(
                room.TemperatureC + (intensity * .0045 * oxygenAvailability * minutes),
                -50,
                180);
            room.SmokePercent = Math.Clamp(
                room.SmokePercent + (intensity * .05 * oxygenAvailability * minutes),
                0,
                100);

            // Healthy oxygen lets an unattended fire escalate. Below that,
            // starvation progressively accelerates decay instead of applying
            // one flat low-O2 rate all the way down to vacuum.
            var growth = room.OxygenPercent >= 18
                ? .32 * minutes
                : -(0.62 + ((18 - room.OxygenPercent) / 17d * 1.38)) * minutes;
            if (!room.IsPowered) growth -= .06 * minutes;
            room.FireIntensity = Math.Clamp(room.FireIntensity + growth, 0, 100);

            // Sustained fire attacks the compartment itself, not just occupants.
            // Once the pressure hull fails, EnvironmentSystem sees a real vacuum
            // source and existing decompression propagation owns the consequence.
            if (!room.HasHullBreach && room.FireIntensity > 35)
            {
                room.HullIntegrityPercent = Math.Max(
                    0,
                    room.HullIntegrityPercent
                    - ((room.FireIntensity - 35) * .015 * minutes));

                if (room.HullIntegrityPercent <= 0)
                {
                    room.HasHullBreach = true;
                    room.VentilationEnabled = false;
                    Log(state, $"STRUCTURAL FAILURE: uncontrolled fire breaches the hull in {room.Name}.");
                    AudioCueSystem.Emit(state, AudioCueKind.Critical, roomId: room.Id);
                }
            }

            foreach (var npc in state.Crew.Where(n =>
                         n.IsAlive && n.IsPresent
                         && n.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
            {
                StatLogSystem.Set(state, npc, CrewStat.Fear, Math.Clamp(npc.Fear + (.32 * minutes), 0, 100), $"fire in {room.Name}");
                StatLogSystem.Set(state, npc, CrewStat.Stress, Math.Clamp(npc.Stress + (.28 * minutes), 0, 100), $"fire in {room.Name}");

                // Owner idea #76: flames burn only the people the front has
                // reached; heat, smoke and fear still fill the compartment.
                var fireDamageRate = Math.Max(0, room.FireIntensity - 28) * .012;
                if (fireDamageRate > 0
                    && FireFrontRules.IsInsideFront(room, npc.PositionX, npc.PositionY))
                    StatLogSystem.Set(state, npc, CrewStat.Health, Math.Max(0, npc.Health - (fireDamageRate * minutes)), "burns");
            }

            if (room.FireIntensity < .5)
            {
                room.FireIntensity = 0;
                Log(state, $"Fire in {room.Name} goes out.");
                continue;
            }

            TrySpread(state, room, (int)Math.Floor(state.Elapsed.TotalMinutes));
        }
    }

    private static void PropagateSmoke(GameState state, TimeSpan delta)
    {
        var minutes = delta.TotalMinutes;
        var smokeAtStart = state.Facility.Rooms.Values.ToDictionary(
            room => room.Id,
            room => room.SmokePercent,
            StringComparer.OrdinalIgnoreCase);
        var change = state.Facility.Rooms.Keys.ToDictionary(
            id => id,
            _ => 0d,
            StringComparer.OrdinalIgnoreCase);

        // Smoke follows the same physical open-compartment graph as atmosphere.
        // Closed/sealed hatches therefore become a meaningful containment tool.
        foreach (var door in state.Facility.Doors.Where(door =>
                     door.IsOpen || door.IsManuallyOverridden))
        {
            var a = smokeAtStart[door.RoomAId];
            var b = smokeAtStart[door.RoomBId];
            var difference = a - b;
            if (Math.Abs(difference) < .01)
                continue;

            var transfer = difference * Math.Min(.18, .045 * minutes);
            change[door.RoomAId] -= transfer;
            change[door.RoomBId] += transfer;
        }

        foreach (var room in state.Facility.Rooms.Values)
        {
            var clearing =
                state.LifeSupport.IsOnline
                && room.IsPowered
                && room.VentilationEnabled
                    ? .45 * minutes
                    : 0;

            room.SmokePercent = Math.Clamp(
                smokeAtStart[room.Id] + change[room.Id] - clearing,
                0,
                100);
        }
    }

    private static void ApplySmokeExposure(GameState state, TimeSpan delta)
    {
        var minutes = delta.TotalMinutes;

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            if (!state.Facility.Rooms.TryGetValue(npc.CurrentRoomId, out var room)
                || room.SmokePercent < 25)
            {
                continue;
            }

            StatLogSystem.Set(
                state,
                npc,
                CrewStat.Fear,
                Math.Clamp(
                    npc.Fear + (Math.Max(0, room.SmokePercent - 25) * .012 * minutes),
                    0,
                    100),
                "smoke");
            StatLogSystem.Set(
                state,
                npc,
                CrewStat.Stress,
                Math.Clamp(
                    npc.Stress + (Math.Max(0, room.SmokePercent - 25) * .01 * minutes),
                    0,
                    100),
                "smoke");

            // Thick smoke becomes rapidly unsurvivable even after flames have
            // been contained or in a neighbouring compartment.
            var smokeDamageRate =
                Math.Max(0, room.SmokePercent - 45) * .018
                + Math.Max(0, room.SmokePercent - 80) * .035;
            if (smokeDamageRate > 0)
            {
                StatLogSystem.Set(
                    state,
                    npc,
                    CrewStat.Health,
                    Math.Max(
                        0,
                        npc.Health - (smokeDamageRate * minutes)),
                    "smoke inhalation");
                npc.NeedsMindReconsideration = true;
            }
        }
    }

    private static void TrySpread(GameState state, Room source, int minute)
    {
        if (minute <= 0 || minute % 5 != 0 || source.FireIntensity < 28)
            return;

        foreach (var door in state.Facility.Doors.Where(d =>
                     d.IsPassable
                     && (d.RoomAId.Equals(source.Id, StringComparison.OrdinalIgnoreCase)
                         || d.RoomBId.Equals(source.Id, StringComparison.OrdinalIgnoreCase))))
        {
            var otherId = door.RoomAId.Equals(source.Id, StringComparison.OrdinalIgnoreCase)
                ? door.RoomBId : door.RoomAId;
            if (!state.Facility.Rooms.TryGetValue(otherId, out var other)
                || other.FireIntensity > 0)
                continue;

            var chance = Math.Clamp(source.FireIntensity / 900d, .02, .11);
            var flashover = source.FireIntensity >= 75;
            if (!flashover
                && StableRoll(state.UpkeepSeed, minute, source.Id, other.Id, "spread") >= chance)
                continue;

            var origin = FireFrontRules.PortalOrigin(source, other);
            FireFrontRules.Ignite(
                other,
                origin.X,
                origin.Y,
                Math.Clamp(source.FireIntensity * .32, 10, 28));
            other.SmokePercent = Math.Max(other.SmokePercent, 3);
            Log(state, $"FIRE SPREAD: flames reach {other.Name} from {source.Name}.");
            AudioCueSystem.Emit(state, AudioCueKind.Critical, roomId: other.Id);
        }
    }

    public static bool TryExecuteCrewAction(
        GameState state,
        Npc npc,
        ActionKind action,
        Room room,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(room);

        if (!npc.IsAlive || !npc.IsPresent
            || !npc.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
        {
            message = "Crew member is not physically present in the target compartment.";
            return false;
        }

        switch (action)
        {
            case ActionKind.FightFire:
            {
                if (room.FireIntensity <= 0)
                {
                    message = $"{room.Name} has no active fire.";
                    return false;
                }

                if (!FireFrontRules.CanReachFront(room, npc.PositionX, npc.PositionY))
                {
                    message = $"{npc.Name} is too far from the flames in {room.Name} to reach them.";
                    return false;
                }

                var skill = Math.Max(
                    npc.Skills.GetValueOrDefault("Engineering"),
                    npc.Skills.GetValueOrDefault("Security"));
                skill = CrewConditionRules.EffectiveSkill(npc, skill);
                var reduction = 6 + (skill * .08);
                room.FireIntensity = Math.Max(0, room.FireIntensity - reduction);
                room.SmokePercent = Math.Max(0, room.SmokePercent - 3);
                StatLogSystem.Set(state, npc, CrewStat.Stress, Math.Clamp(npc.Stress + 3, 0, 100), $"fighting the fire in {room.Name}");
                message = room.FireIntensity <= 0
                    ? $"{npc.Name} extinguishes the fire in {room.Name}."
                    : $"{npc.Name} knocks the fire in {room.Name} down to {room.FireIntensity:0}% intensity.";
                Log(state, message);
                return true;
            }

            case ActionKind.PatchHull:
            {
                if (!HullRepairRules.CanAttempt(npc, room))
                {
                    message = !room.HasHullBreach
                        ? $"{room.Name} no longer has a hull breach to patch."
                        : room.FireIntensity > 0
                            ? $"{room.Name} is still burning; the hull cannot be patched yet."
                            : $"{npc.Name} is not fit or skilled enough for emergency hull repair.";
                    return false;
                }

                room.HasHullBreach = false;
                room.HullIntegrityPercent = Math.Max(
                    room.HullIntegrityPercent,
                    HullRepairRules.RestoredHullIntegrityPercent);
                room.VentilationEnabled = true;
                message = $"{npc.Name} seals the hull breach in {room.Name}; the air loop can repressurise the compartment.";
                Log(state, message);
                AudioCueSystem.Emit(state, AudioCueKind.Important, roomId: room.Id);
                return true;
            }

            case ActionKind.SealHazardRoom:
            {
                var closable = state.Facility.Doors
                    .Where(door => CrewDoorInteractionSystem.CanClose(npc, door))
                    .ToList();
                if (closable.Count == 0)
                {
                    message = $"{npc.Name} cannot reach an operable open hatch from {room.Name}.";
                    return false;
                }

                foreach (var door in closable)
                {
                    door.IsOpen = false;
                    door.CrewAutoCloseAt = null;
                    door.LastCrewOperatorId = npc.Id;
                }

                message = $"{npc.Name} seals {closable.Count} hatch(es) around {room.Name}.";
                Log(state, message);
                return true;
            }

            case ActionKind.VentHazardRoom:
            {
                if (room.FireIntensity <= 0 && room.SmokePercent < 8)
                {
                    message = $"{room.Name} has no fire or smoke worth venting.";
                    return false;
                }
                if (room.PressureKpa < 72)
                {
                    message = $"{room.Name} is already too depressurised to vent safely.";
                    return false;
                }

                room.SmokePercent = Math.Max(0, room.SmokePercent - 38);
                room.FireIntensity = Math.Max(0, room.FireIntensity - 14);
                room.PressureKpa = Math.Max(55, room.PressureKpa - 14);
                room.OxygenPercent = Math.Max(12, room.OxygenPercent - 1.8);
                message = $"{npc.Name} vents atmosphere from {room.Name}; smoke and fire fall, but pressure drops.";
                Log(state, message);
                RecordVentWitnesses(state, npc, room);
                return true;
            }

            default:
                message = "That is not a deterministic hazard response affordance.";
                return false;
        }
    }

    // Owner idea #16: venting with other people still inside is a decision
    // others can later hold against (or credit to) the actor. Only the people
    // in the compartment perceive it.
    private static void RecordVentWitnesses(GameState state, Npc actor, Room room)
    {
        var others = state.Crew
            .Where(other => other.IsAlive
                && other.IsPresent
                && other.Id != actor.Id
                && other.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (others.Count == 0)
            return;

        foreach (var witness in others)
        {
            if (!PerceptionSystem.CanMakeOut(state, witness, actor))
                continue;

            var alsoInside = others
                .Where(other => other.Id != witness.Id)
                .Select(other => other.Name)
                .ToArray();
            var whoWasInside = alsoInside.Length == 0
                ? "I was"
                : $"I and {string.Join(" and ", alsoInside)} were";
            witness.Memories.Add(new Memory(
                $"Witnessed {actor.Name} vent {room.Name} while {whoWasInside} still inside.",
                state.Elapsed,
                0.7,
                MoralActorName: actor.Name));
        }
    }

    /// <summary>
    /// Shared by <c>BrowserMindSystem</c> and <c>RuleBasedAiDecisionService</c>
    /// (P1 ladder convergence): whether an ordinary crew member facing an
    /// active fire in their own current room should stay and fight it rather
    /// than flee, gated on fire intensity still being survivable and the
    /// person having either the practical skill or the courage for it.
    /// </summary>
    /// <summary>
    /// Emergency hull repair is a cognition-visible capability, not an automatic
    /// rescue script. The worker must be healthy enough for the exposure, have
    /// real repair skill, and wait until the fire that caused the breach is out.
    /// </summary>
    public static class HullRepairRules
    {
        public const int MinimumRepairSkill = 55;
        public const double MinimumHealth = 55;
        public const double RestoredHullIntegrityPercent = 35;
        public static readonly TimeSpan PatchDuration = TimeSpan.FromMinutes(4);

        public static bool CanAttempt(Npc npc, Room room)
        {
            ArgumentNullException.ThrowIfNull(npc);
            ArgumentNullException.ThrowIfNull(room);

            return npc.IsAlive
                && npc.IsPresent
                && npc.Health >= MinimumHealth
                && CrewCounterplaySystem.BestRepairSkill(npc) >= MinimumRepairSkill
                && room.HasHullBreach
                && room.FireIntensity <= 0;
        }

        /// <summary>
        /// Choosing PatchHull includes donning the station's standard emergency
        /// pressure suit and tether before entering the target compartment.
        /// This deterministic execution gear protects only while that patch
        /// intent/task is live; it does not choose the NPC's motive.
        /// </summary>
        public static bool HasEmergencyPressureProtection(Npc npc, string roomId)
        {
            ArgumentNullException.ThrowIfNull(npc);
            ArgumentException.ThrowIfNullOrWhiteSpace(roomId);

            return (npc.Intent is { Action: ActionKind.PatchHull, TargetId: { } intentTarget }
                        && intentTarget.Equals(roomId, StringComparison.OrdinalIgnoreCase))
                || (npc.ActiveTask is
                    {
                        Status: CrewTaskStatus.InProgress,
                        Action: ActionKind.PatchHull,
                        TargetId: { } taskTarget
                    }
                    && taskTarget.Equals(roomId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// One grounded breach a deterministic fallback mind could choose to patch.
    /// C# exposes the opportunity and avoids duplicate responders; the mind still
    /// chooses the PatchHull intention.
    /// </summary>
    public static Room? FindRepairableBreachForResponder(
        GameState state,
        Npc npc,
        NavigationSystem navigation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(navigation);

        if (!npc.IsAlive || !npc.IsPresent || npc.IsContainmentBreachInProgress)
            return null;

        return state.Facility.Rooms.Values
            .Where(room => HullRepairRules.CanAttempt(npc, room))
            .Where(room => !state.Crew.Any(other =>
                other.Id != npc.Id
                && other.IsAlive
                && other.IsPresent
                && ((other.Intent is
                        {
                            Action: ActionKind.PatchHull,
                            TargetId: { } intentTarget
                        }
                        && intentTarget.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
                    || (other.ActiveTask is
                        {
                            Status: CrewTaskStatus.InProgress,
                            Action: ActionKind.PatchHull,
                            TargetId: { } taskTarget
                        }
                        && taskTarget.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))))
            .Select(room => new
            {
                Room = room,
                IsCurrent = room.Id.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase),
                Path = room.Id.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                    ? new[] { npc.CurrentRoomId }
                    : navigation.FindPathForCrew(state, npc, npc.CurrentRoomId, room.Id).ToArray()
            })
            .Where(candidate => candidate.IsCurrent || candidate.Path.Length >= 2)
            .OrderByDescending(candidate => candidate.IsCurrent)
            .ThenBy(candidate => candidate.Path.Length)
            .ThenBy(candidate => candidate.Room.Id, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Room)
            .FirstOrDefault();
    }

    public static bool ShouldFightFire(Npc npc, Room room)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(room);

        var courage = Math.Clamp(
            npc.Personality.Courage + CrewTraitMath.Modifier(npc, TraitEffectKind.Courage),
            0,
            100);
        var practical = Math.Max(
            npc.Skills.GetValueOrDefault("Engineering"),
            npc.Skills.GetValueOrDefault("Security"));
        return room.FireIntensity > 0
            && room.FireIntensity <= 58
            && room.SmokePercent <= 70
            && room.OxygenPercent >= 14
            && room.PressureKpa >= 65
            && npc.Health >= 35
            && npc.Stress < 88
            && npc.Fatigue < 88
            && (practical >= 45 || courage >= 72);
    }

    /// <summary>
    /// Chooses one remote fire this fallback mind can physically reach and
    /// safely attempt to suppress. Ollama cognition already receives
    /// station-wide fire/smoke readings; this gives deterministic fallback
    /// minds the same grounded opportunity without scripting an all-hands
    /// response.
    ///
    /// At most one responder is nominated per fire while another crew member
    /// already holds a FightFire intent or active FightFire task for it —
    /// unless this crew member heard a recent Overseer FIRE ALARM for that
    /// compartment and finds Overseer credible enough to act on it (owner idea
    /// #89), in which case they will join one responder already there.
    /// </summary>
    public static Room? FindRemoteFireForResponder(
        GameState state,
        Npc npc,
        NavigationSystem navigation,
        IReadOnlySet<string>? excludedRoomIds = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(navigation);

        if (!npc.IsAlive || !npc.IsPresent)
        {
            return null;
        }

        return state.Facility.Rooms.Values
            .Where(room =>
                !room.Id.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase)
                && (excludedRoomIds is null || !excludedRoomIds.Contains(room.Id))
                && ShouldFightFire(npc, room)
                && state.Crew.Count(other =>
                    other.Id != npc.Id
                    && other.IsAlive
                    && other.IsPresent
                    && ((other.Intent is
                            {
                                Action: ActionKind.FightFire,
                                TargetId: { } intentTarget
                            }
                            && intentTarget.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
                        || (other.ActiveTask is
                            {
                                Status: CrewTaskStatus.InProgress,
                                Action: ActionKind.FightFire,
                                TargetId: { } taskTarget
                            }
                            && taskTarget.Equals(room.Id, StringComparison.OrdinalIgnoreCase))))
                    < (HeardCredibleFireAlarm(state, npc, room) ? 2 : 1))
            .Select(room => new
            {
                Room = room,
                Path = navigation.FindPathForCrew(
                    state,
                    npc,
                    npc.CurrentRoomId,
                    room.Id)
            })
            .Where(candidate => candidate.Path.Count >= 2)
            .OrderByDescending(candidate => candidate.Room.FireIntensity)
            .ThenBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.Room.Id, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Room)
            .FirstOrDefault();
    }

    /// <summary>How long an Overseer FIRE ALARM stays fresh enough for a fallback mind to act on.</summary>
    public static readonly TimeSpan FireAlarmResponseWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether this crew member received an Overseer FIRE ALARM naming
    /// <paramref name="room"/> recently and trusts Overseer enough to act on
    /// it. This is the deterministic fallback minds' stand-in for weighing
    /// the alarm; Ollama cognition sees the same broadcast in its prompt and
    /// judges it for itself.
    /// </summary>
    public static bool HeardCredibleFireAlarm(GameState state, Npc npc, Room room)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(room);

        return OverseerCommsRules.Persuasiveness(npc.OverseerCredibility, npc.OverseerSuspicion) >= 0.5
            && npc.ReceivedMessages.Any(message =>
                message.Claim == OverseerClaimKind.FireAlarm
                && room.Id.Equals(message.SubjectRoomId, StringComparison.OrdinalIgnoreCase)
                && state.Elapsed - message.SentAt <= FireAlarmResponseWindow);
    }

    /// <summary>
    /// A remote fire is a station event worth reconsidering, but not permission
    /// for C# to choose anybody's goal. Wake at most one available, capable
    /// responder per fire so the active mind gets a prompt now instead of
    /// waiting for the ordinary 4-6 minute cognition rotation. A remote station
    /// fire is allowed to wake someone doing mundane committed work; cognition
    /// still chooses the response, and CrewTaskSystem validates whether that
    /// chosen response is a genuine life-threat interruption.
    /// </summary>
    private static void WakeRemoteFireResponders(GameState state)
    {
        var navigation = new NavigationSystem();
        var reservedFireRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var npc in state.Crew
                     .Where(candidate =>
                         candidate.IsAlive
                         && candidate.IsPresent
                         && !candidate.IsContainmentBreachInProgress
                         && candidate.Intent?.Action != ActionKind.FightFire
                         && !CrewEnvironmentSafety.IsDangerous(
                             state.Facility.Rooms[candidate.CurrentRoomId]))
                     // Do not filter on the current intent's urgency here.
                     // Urgency is a mind hint, not proof that cognition should
                     // never reconsider. In particular, critical Eat/Sleep
                     // intents are urgency 90+, while both fallback minds
                     // deliberately rank a viable remote fire above those
                     // needs. Nomination only asks the mind to reconsider; it
                     // still decides the response.
                     // Preserve useful work when an equally viable idle responder exists.
                     // This is only responder nomination for a station event; cognition still
                     // decides whether the nominated person actually wants to fight the fire.
                     .OrderBy(candidate => CrewTaskSystem.IsWorking(candidate) ? 1 : 0)
                     .ThenByDescending(candidate =>
                         Math.Max(
                             candidate.Skills.GetValueOrDefault("Engineering"),
                             candidate.Skills.GetValueOrDefault("Security")))
                     .ThenByDescending(candidate => candidate.Personality.Courage)
                     .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
        {
            var fire = FindRemoteFireForResponder(
                state,
                npc,
                navigation,
                reservedFireRooms);

            if (fire is null)
                continue;

            npc.NeedsMindReconsideration = true;
            reservedFireRooms.Add(fire.Id);
        }
    }

    private static double StableRoll(int seed, int minute, string a, string b, string salt)
    {
        unchecked
        {
            uint hash = (uint)seed ^ 2166136261u ^ (uint)minute;
            foreach (var ch in $"{a}|{b}|{salt}")
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return (hash % 10000) / 10000d;
        }
    }

    private static void Log(GameState state, string message) =>
        state.EventLog.Insert(0, $"T+{state.Elapsed:hh\\:mm}: {message}");
}
