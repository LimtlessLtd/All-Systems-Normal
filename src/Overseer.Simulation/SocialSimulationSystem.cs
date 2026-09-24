using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class SocialSimulationSystem
{
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);

        if (minute <= 0)
        {
            return;
        }

        // Rolls include the station seed so two stations do not share one
        // social history.
        var rollClock = unchecked(minute + (state.UpkeepSeed * 7919));

        var living = state.Crew
            .Where(npc => npc.IsAlive && npc.Movement is null)
            .ToList();

        foreach (var roomGroup in living.GroupBy(npc => npc.CurrentRoomId))
        {
            // Pairing order rotates rather than always favouring whoever sorts
            // first by name.
            var occupants = roomGroup
                .OrderBy(npc => StableRoll(rollClock, npc.Name, "", 7))
                .ThenBy(npc => npc.Name)
                .ToList();
            var engaged = new HashSet<Guid>();

            for (var i = 0; i < occupants.Count; i++)
            {
                for (var j = i + 1; j < occupants.Count; j++)
                {
                    var first = occupants[i];
                    var second = occupants[j];

                    if (engaged.Contains(first.Id) || engaged.Contains(second.Id))
                    {
                        continue;
                    }

                    if (ResolvePair(state, first, second, rollClock))
                    {
                        engaged.Add(first.Id);
                        engaged.Add(second.Id);
                    }
                }
            }
        }
    }

    private static bool ResolvePair(GameState state, Npc first, Npc second, int minute)
    {
        var firstToSecond = first.Relationships[second.Name];
        var secondToFirst = second.Relationships[first.Name];

        // Violence is urgent and is not held back by conversational pacing.
        if (TryViolence(state, first, second, firstToSecond, minute, salt: 1)
            || TryViolence(state, second, first, secondToFirst, minute, salt: 2))
        {
            return true;
        }

        // Someone asleep in bed is not available to chat; ambient pairing
        // used to pull sleepers up for a conversation every few minutes.
        if (SimulationEngine.IsPhysicallyAsleep(state, first)
            || SimulationEngine.IsPhysicallyAsleep(state, second))
        {
            return false;
        }

        if (state.Elapsed < first.NextConversationAt
            || state.Elapsed < second.NextConversationAt)
        {
            return false;
        }

        if (TryDirectedAffordance(
                state,
                first,
                second,
                firstToSecond,
                secondToFirst,
                minute)
            || TryDirectedAffordance(
                state,
                second,
                first,
                secondToFirst,
                firstToSecond,
                minute))
        {
            return true;
        }

        if (TryIntimacy(
                state,
                first,
                second,
                firstToSecond,
                secondToFirst,
                minute))
        {
            return true;
        }

        var combinedStress = (first.Stress + second.Stress) / 2;
        var combinedResentment = (firstToSecond.Resentment + secondToFirst.Resentment) / 2;
        var friction = combinedStress + combinedResentment
            + ((Effective(first, TraitEffectKind.Temper, first.Personality.Temper)
                + Effective(second, TraitEffectKind.Temper, second.Personality.Temper)) / 5);

        var roll = StableRoll(minute, first.Name, second.Name, 11);

        // This system now checks each simulated minute instead of every five,
        // so the per-turn chance is deliberately lower.
        if (friction >= 85 && roll < 0.18)
        {
            Argue(state, first, second, firstToSecond, secondToFirst, minute);
            return true;
        }

        var wantsCompany =
            first.SocialNeed >= 28
            || second.SocialNeed >= 28
            || first.CurrentAction.Kind is ActionKind.Talk
                or ActionKind.Socialize
                or ActionKind.CheckOnCrew
                or ActionKind.AssistCrew
                or ActionKind.CoordinateWork
                or ActionKind.ReassureCrew
                or ActionKind.ReportConcern
            || second.CurrentAction.Kind is ActionKind.Talk
                or ActionKind.Socialize
                or ActionKind.CheckOnCrew
                or ActionKind.AssistCrew
                or ActionKind.CoordinateWork
                or ActionKind.ReassureCrew
                or ActionKind.ReportConcern
            || state.Facility.Rooms[first.CurrentRoomId].Type is RoomType.Recreation or RoomType.Kitchen;

        var socialChance = Math.Clamp(
            0.04
            + ((Effective(first, TraitEffectKind.Sociability, first.Personality.Sociability)
                + Effective(second, TraitEffectKind.Sociability, second.Personality.Sociability)) / 1600),
            0.05,
            0.14);

        if (wantsCompany && roll < socialChance)
        {
            Socialize(state, first, second, firstToSecond, secondToFirst, minute);
            return true;
        }

        return false;
    }


    private static bool TryDirectedAffordance(
        GameState state,
        Npc actor,
        Npc target,
        Relationship actorToTarget,
        Relationship targetToActor,
        int minute)
    {
        if (!target.Name.Equals(
                actor.CurrentAction.TargetId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var kind = actor.CurrentAction.Kind;
        if (kind is not (ActionKind.CheckOnCrew
            or ActionKind.AssistCrew
            or ActionKind.CoordinateWork
            or ActionKind.ReassureCrew
            or ActionKind.MisleadCrew
            or ActionKind.ReportConcern
            or ActionKind.AskAboutLocation))
        {
            return false;
        }

        switch (kind)
        {
            case ActionKind.CheckOnCrew:
                StatLogSystem.Set(state, target, CrewStat.Stress, Clamp(target.Stress - 1.5), $"{actor.Name} checked on them");
                actorToTarget.Affinity = Clamp(actorToTarget.Affinity + 0.4);
                targetToActor.Trust = Clamp(targetToActor.Trust + 0.5);
                QueueSpeech(actor, "You doing okay?", state.Elapsed, 2);
                QueueSpeech(target, "I'm managing.", state.Elapsed + TimeSpan.FromMinutes(1), 2);
                break;

            case ActionKind.AssistCrew:
                actorToTarget.Trust = Clamp(actorToTarget.Trust + 0.6);
                targetToActor.Trust = Clamp(targetToActor.Trust + 0.9);
                StatLogSystem.Set(state, target, CrewStat.Stress, Clamp(target.Stress - 1), $"{actor.Name} helped out");
                QueueSpeech(actor, "Need another pair of hands?", state.Elapsed, 2);
                break;

            case ActionKind.CoordinateWork:
                actorToTarget.Trust = Clamp(actorToTarget.Trust + 0.5);
                targetToActor.Trust = Clamp(targetToActor.Trust + 0.5);
                QueueSpeech(actor, "Let's coordinate this properly.", state.Elapsed, 2);
                break;

            case ActionKind.ReassureCrew:
                StatLogSystem.Set(state, target, CrewStat.Fear, Clamp(target.Fear - 2.5), $"{actor.Name} reassured them");
                StatLogSystem.Set(state, target, CrewStat.Stress, Clamp(target.Stress - 2), $"{actor.Name} reassured them");
                targetToActor.Trust = Clamp(targetToActor.Trust + 0.7);
                QueueSpeech(actor, "We'll handle it. Stay with me.", state.Elapsed, 2);
                break;

            case ActionKind.ReportConcern:
                target.Memories.Add(new Memory(
                    $"{actor.Name} shared a concern: {actor.CurrentAction.Reason}",
                    state.Elapsed,
                    0.38));
                target.NeedsMindReconsideration = true;
                targetToActor.Trust = Clamp(targetToActor.Trust + 0.25);
                QueueSpeech(actor, "Something's bothering me. Hear me out.", state.Elapsed, 2);
                break;

            case ActionKind.MisleadCrew:
                // Deception is an attempt, not a magic belief edit. The target
                // remembers the interaction; later evidence/account comparison
                // determines what they actually believe.
                target.Memories.Add(new Memory(
                    $"{actor.Name} tried to steer me away from a subject: {actor.CurrentAction.Reason}",
                    state.Elapsed,
                    0.42));
                var noticed = StableRoll(
                    minute,
                    actor.Name,
                    target.Name,
                    319) < Math.Clamp(
                        (target.Personality.Empathy + targetToActor.Resentment) / 180d,
                        0.18,
                        0.78);
                if (noticed)
                {
                    targetToActor.Trust = Clamp(targetToActor.Trust - 2.2);
                    targetToActor.Resentment = Clamp(targetToActor.Resentment + 1.3);
                    QueueSpeech(target, "Why are you trying to steer me away from this?", state.Elapsed + TimeSpan.FromMinutes(1), 2);
                }
                else
                {
                    QueueSpeech(actor, "Honestly, it's probably nothing. Focus somewhere else.", state.Elapsed, 2);
                }
                break;

            case ActionKind.AskAboutLocation:
                // Asking is always worth a small amount of goodwill, whether
                // or not the answer turns out to be useful; the substance of
                // the answer (and any belief update) is resolved deterministically.
                targetToActor.Trust = Clamp(targetToActor.Trust + 0.2);
                MissingPersonSystem.ResolveAsk(state, actor, target, actor.CurrentAction.SubjectId);
                break;
        }

        actor.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(3);
        SetConversationCooldown(state, actor, target, minute, 321, 5, 9);
        Log(state, $"{actor.Name} acts on a {kind} intention involving {target.Name}.");
        return true;
    }

    private static bool TryIntimacy(
        GameState state,
        Npc first,
        Npc second,
        Relationship firstToSecond,
        Relationship secondToFirst,
        int minute)
    {
        if (state.Facility.Rooms[first.CurrentRoomId].Type != RoomType.CrewQuarters
            || state.Elapsed < first.RoutineUntil
            || state.Elapsed < second.RoutineUntil
            || first.IntimacyNeed < 65
            || second.IntimacyNeed < 65
            || firstToSecond.Trust < 60
            || secondToFirst.Trust < 60
            || firstToSecond.Affinity < 65
            || secondToFirst.Affinity < 65
            || firstToSecond.Attraction < 55
            || secondToFirst.Attraction < 55
            || firstToSecond.Resentment >= 25
            || secondToFirst.Resentment >= 25)
        {
            return false;
        }

        first.CurrentAction = new NpcAction(
            ActionKind.Intimacy,
            second.Name,
            $"Spending consensual private time with {second.Name}.");
        second.CurrentAction = new NpcAction(
            ActionKind.Intimacy,
            first.Name,
            $"Spending consensual private time with {first.Name}.");

        first.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(20);
        second.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(20);

        firstToSecond.Affinity = Clamp(firstToSecond.Affinity + 1.0);
        secondToFirst.Affinity = Clamp(secondToFirst.Affinity + 1.0);
        firstToSecond.Trust = Clamp(firstToSecond.Trust + 0.6);
        secondToFirst.Trust = Clamp(secondToFirst.Trust + 0.6);

        var replyDelay = ReplyDelayMinutes(minute, first.Name, second.Name, 201, 1, 2);
        QueueSpeech(first, "Want some privacy?", state.Elapsed, 2);
        QueueSpeech(second, "Yeah. Come on.", state.Elapsed + TimeSpan.FromMinutes(replyDelay), 2);
        SetConversationCooldown(state, first, second, minute, 202, 18, 24);

        Log(state, $"{first.Name} and {second.Name} spend some private time together.");
        return true;
    }

    private static void Socialize(
        GameState state,
        Npc first,
        Npc second,
        Relationship firstToSecond,
        Relationship secondToFirst,
        int minute)
    {
        var warmth = 0.6
            + ((Effective(first, TraitEffectKind.Sociability, first.Personality.Sociability)
                + Effective(second, TraitEffectKind.Sociability, second.Personality.Sociability)) / 200)
            + ((Effective(first, TraitEffectKind.Empathy, first.Personality.Empathy)
                + Effective(second, TraitEffectKind.Empathy, second.Personality.Empathy)) / 1000);

        firstToSecond.Affinity = Clamp(firstToSecond.Affinity + warmth);
        secondToFirst.Affinity = Clamp(secondToFirst.Affinity + warmth);
        firstToSecond.Trust = Clamp(firstToSecond.Trust + 0.45);
        secondToFirst.Trust = Clamp(secondToFirst.Trust + 0.45);
        firstToSecond.Resentment = Clamp(firstToSecond.Resentment - 0.35);
        secondToFirst.Resentment = Clamp(secondToFirst.Resentment - 0.35);

        if (firstToSecond.Affinity >= 62)
        {
            firstToSecond.Attraction = Clamp(firstToSecond.Attraction + 0.18);
        }

        if (secondToFirst.Affinity >= 62)
        {
            secondToFirst.Attraction = Clamp(secondToFirst.Attraction + 0.18);
        }

        firstToSecond.Conversations++;
        secondToFirst.Conversations++;

        first.CurrentAction = new NpcAction(
            ActionKind.Socialize,
            second.Name,
            $"Talking with {second.Name}.");
        second.CurrentAction = new NpcAction(
            ActionKind.Socialize,
            first.Name,
            $"Talking with {first.Name}.");

        first.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(6);
        second.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(6);

        // Whoever most wants to talk opens, about whatever is on their mind.
        var (speaker, listener) = first.SocialNeed >= second.SocialNeed
            ? (first, second)
            : (second, first);
        var exchange = ConversationTopicSystem.Converse(
            state,
            speaker,
            listener,
            StableRoll(minute, first.Name, second.Name, 31));

        var replyDelay = ReplyDelayMinutes(minute, first.Name, second.Name, 32, 1, 3);

        QueueSpeech(speaker, exchange.Opening, state.Elapsed, 2);
        QueueSpeech(
            listener,
            exchange.Reply,
            state.Elapsed + TimeSpan.FromMinutes(replyDelay),
            2);

        SetConversationCooldown(state, first, second, minute, 33, 8, 15);

        if (exchange.LogLine is not null)
        {
            Log(state, exchange.LogLine);
        }
        else if ((firstToSecond.Conversations + secondToFirst.Conversations) % 8 == 0)
        {
            Log(state, $"{first.Name} and {second.Name} spend time talking.");
        }
    }

    private static void Argue(
        GameState state,
        Npc first,
        Npc second,
        Relationship firstToSecond,
        Relationship secondToFirst,
        int minute)
    {
        var firstIncrease = 2.2 + (first.Personality.Temper / 40);
        var secondIncrease = 2.2 + (second.Personality.Temper / 40);

        firstToSecond.Resentment = Clamp(firstToSecond.Resentment + firstIncrease);
        secondToFirst.Resentment = Clamp(secondToFirst.Resentment + secondIncrease);
        firstToSecond.Trust = Clamp(firstToSecond.Trust - 1.4);
        secondToFirst.Trust = Clamp(secondToFirst.Trust - 1.4);
        firstToSecond.Attraction = Clamp(firstToSecond.Attraction - 0.8);
        secondToFirst.Attraction = Clamp(secondToFirst.Attraction - 0.8);
        StatLogSystem.Set(state, first, CrewStat.Stress, Clamp(first.Stress + 2.5), $"argument with {second.Name}");
        StatLogSystem.Set(state, second, CrewStat.Stress, Clamp(second.Stress + 2.5), $"argument with {first.Name}");
        firstToSecond.Arguments++;
        secondToFirst.Arguments++;

        first.CurrentAction = new NpcAction(
            ActionKind.Argue,
            second.Name,
            $"Arguing with {second.Name}.");
        second.CurrentAction = new NpcAction(
            ActionKind.Argue,
            first.Name,
            $"Arguing with {first.Name}.");

        first.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(7);
        second.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(7);

        var (firstLine, secondLine, topic) = ConversationTopicSystem.Argument(
            first,
            second,
            firstToSecond,
            StableRoll(minute, first.Name, second.Name, 55));
        var about = topic is null ? "" : $" about {topic}";

        var replyDelay = ReplyDelayMinutes(minute, first.Name, second.Name, 57, 1, 2);
        QueueSpeech(first, firstLine, state.Elapsed, 2);
        QueueSpeech(second, secondLine, state.Elapsed + TimeSpan.FromMinutes(replyDelay), 2);
        SetConversationCooldown(state, first, second, minute, 58, 9, 16);

        AudioCueSystem.Emit(
            state,
            AudioCueKind.Warning,
            first.Id.ToString(),
            first.CurrentRoomId);

        first.Memories.Add(new Memory(
            $"Argument with {second.Name}{about}.",
            state.Elapsed,
            0.55));
        second.Memories.Add(new Memory(
            $"Argument with {first.Name}{about}.",
            state.Elapsed,
            0.55));

        Log(state, $"{first.Name} and {second.Name} get into an argument{about}.");

        SideWithCliqueMate(state, first, second);
        SideWithCliqueMate(state, second, first);
    }

    public const double CliqueSidingResentment = 1.0;

    // Owner idea #13: disputes propagate within a clique, but only to friends
    // who actually witness them; someone in both parties' clique stays neutral.
    public static void SideWithCliqueMate(GameState state, Npc friend, Npc opponent)
    {
        foreach (var witness in state.Crew)
        {
            if (witness.Id == friend.Id
                || witness.Id == opponent.Id
                || !witness.IsAlive
                || !witness.IsPresent
                || witness.CurrentRoomId != friend.CurrentRoomId
                || !SocialClusterSystem.SharesClique(witness, friend)
                || SocialClusterSystem.SharesClique(witness, opponent)
                || !witness.Relationships.TryGetValue(opponent.Name, out var witnessToOpponent))
            {
                continue;
            }

            witnessToOpponent.Resentment = Clamp(witnessToOpponent.Resentment + CliqueSidingResentment);
            witness.Memories.Add(new Memory(
                $"Saw {opponent.Name} argue with my friend {friend.Name}.",
                state.Elapsed,
                0.35));
        }
    }

    private static bool TryViolence(
        GameState state,
        Npc aggressor,
        Npc target,
        Relationship relationship,
        int minute,
        int salt)
    {
        if (!aggressor.IsAlive || !target.IsAlive)
            return false;

        var temper = Effective(
            aggressor,
            TraitEffectKind.Temper,
            aggressor.Personality.Temper);
        var room = state.Facility.Rooms[aggressor.CurrentRoomId];
        var prisonerPressure = aggressor.IsPrisoner
            ? aggressor.PrisonerDangerLevel switch
            {
                PrisonerDangerLevel.Low => 0,
                PrisonerDangerLevel.Moderate => 8,
                PrisonerDangerLevel.High => 20,
                PrisonerDangerLevel.Extreme => 34,
                _ => 0
            } + aggressor.PrisonerViolenceBias
            : 0;

        // No single threshold causes a fight. A bad relationship, stress,
        // exhaustion, hunger, fear, an ongoing argument and prisoner danger can
        // combine into escalation pressure. The stable roll keeps outcomes
        // deterministic for the same world state/minute.
        var pressure =
            (relationship.Resentment * 1.05)
            + ((100 - relationship.Trust) * .30)
            + (temper * .62)
            + (Math.Max(0, aggressor.Stress - 42) * .72)
            + (Math.Max(0, aggressor.Fatigue - 58) * .34)
            + (Math.Max(0, aggressor.Hunger - 68) * .24)
            + (Math.Max(0, aggressor.Fear - 65) * .20)
            + (aggressor.CurrentAction.Kind == ActionKind.Argue ? 18 : 0)
            + (room.FireIntensity > 0 ? 8 : 0)
            + prisonerPressure;

        if (pressure < 172)
            return false;

        // Evaluated per co-located pair per minute, so even severe pressure
        // remains an occasional escalation rather than a guaranteed brawl.
        var chance = Math.Clamp((pressure - 160) / 1250d, 0.008, 0.11);

        if (StableRoll(minute, aggressor.Name, target.Name, 100 + salt) >= chance)
        {
            return false;
        }

        var damage = 18
            + (Effective(
                aggressor,
                TraitEffectKind.Temper,
                aggressor.Personality.Temper) * 0.18)
            + (relationship.Resentment * 0.11);

        StatLogSystem.Set(state, target, CrewStat.Health, Clamp(target.Health - damage), $"attacked by {aggressor.Name}");
        StatLogSystem.Set(state, target, CrewStat.Fear, Clamp(target.Fear + 30), $"attacked by {aggressor.Name}");
        StatLogSystem.Set(state, target, CrewStat.Stress, Clamp(target.Stress + 25), $"attacked by {aggressor.Name}");
        StatLogSystem.Set(state, aggressor, CrewStat.Stress, Clamp(aggressor.Stress + 8), $"attacking {target.Name}");
        relationship.Resentment = Clamp(relationship.Resentment + 4);
        SetConversationCooldown(state, aggressor, target, minute, 211 + salt, 4, 8);

        aggressor.CurrentAction = new NpcAction(
            ActionKind.Attack,
            target.Name,
            $"Attacking {target.Name} after escalating conflict.");

        SetImmediateBubble(state, aggressor, "Stay out of my way!", NpcBubbleKind.Alert, 3);
        SetImmediateBubble(state, target, "Help!", NpcBubbleKind.Alert, 4);
        AudioCueSystem.Emit(
            state,
            AudioCueKind.Hostile,
            aggressor.Id.ToString(),
            aggressor.CurrentRoomId);

        foreach (var witness in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.Id != aggressor.Id
                     && npc.Id != target.Id
                     && npc.CurrentRoomId == aggressor.CurrentRoomId))
        {
            StatLogSystem.Set(state, witness, CrewStat.Fear, Clamp(witness.Fear + 22), "witnessed an assault");
            StatLogSystem.Set(state, witness, CrewStat.Stress, Clamp(witness.Stress + 18), "witnessed an assault");
            witness.NeedsMindReconsideration = true;

            // In the dark, or too far away, a witness hears the struggle but
            // cannot say who did it.
            if (!PerceptionSystem.CanMakeOut(state, witness, aggressor))
            {
                witness.Memories.Add(new Memory(
                    $"Heard a violent struggle in {state.Facility.Rooms[aggressor.CurrentRoomId].Name} but couldn't see who.",
                    state.Elapsed,
                    0.8));
                SetImmediateBubble(state, witness, "Who's there?!", NpcBubbleKind.Alert, 3);
                continue;
            }

            witness.Memories.Add(new Memory(
                $"Witnessed {aggressor.Name} attack {target.Name}.",
                state.Elapsed,
                0.92,
                MoralActorName: aggressor.Name));

            SetImmediateBubble(state, witness, "What the hell?!", NpcBubbleKind.Alert, 3);

            if (witness.Relationships.TryGetValue(aggressor.Name, out var witnessRelationship))
            {
                witnessRelationship.Trust = Clamp(witnessRelationship.Trust - 18);
                witnessRelationship.Resentment = Clamp(witnessRelationship.Resentment + 22);
            }
        }

        if (target.Health <= 0)
        {
            target.Health = 0;
            target.CauseOfDeath = $"Killed by {aggressor.Name} during a violent confrontation.";
            target.CurrentAction = new NpcAction(
                ActionKind.Idle,
                null,
                "Deceased.");

            aggressor.Memories.Add(new Memory(
                $"Killed {target.Name} during a confrontation.",
                state.Elapsed,
                1.0));

            AudioCueSystem.Emit(
                state,
                AudioCueKind.Critical,
                target.Id.ToString(),
                target.CurrentRoomId);

            Log(state, $"CRITICAL: {target.Name} has been killed by {aggressor.Name}.");
        }
        else
        {
            target.CurrentAction = new NpcAction(
                ActionKind.RequestHelp,
                aggressor.Name,
                $"Injured by {aggressor.Name}; seeking help.");

            Log(state, $"CRITICAL: {aggressor.Name} attacks {target.Name}.");
        }

        SetConversationCooldown(state, aggressor, target, minute, 150 + salt, 10, 18);
        return true;
    }

    private static void QueueSpeech(
        Npc npc,
        string text,
        TimeSpan startsAt,
        int durationMinutes) =>
        ConversationPacingSystem.Schedule(
            npc,
            text,
            NpcBubbleKind.Speech,
            startsAt,
            durationMinutes);

    private static void SetImmediateBubble(
        GameState state,
        Npc npc,
        string text,
        NpcBubbleKind kind,
        int durationMinutes)
    {
        npc.Bubble = new NpcBubble(
            text,
            kind,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(durationMinutes));

        AudioCueSystem.Emit(
            state,
            kind == NpcBubbleKind.Alert
                ? AudioCueKind.Warning
                : AudioCueKind.Speech,
            npc.Id.ToString(),
            npc.CurrentRoomId);
    }

    private static void SetConversationCooldown(
        GameState state,
        Npc first,
        Npc second,
        int minute,
        int salt,
        int minMinutes,
        int maxMinutes)
    {
        var delay = ReplyDelayMinutes(
            minute,
            first.Name,
            second.Name,
            salt,
            minMinutes,
            maxMinutes);

        var next = state.Elapsed + TimeSpan.FromMinutes(delay);
        first.NextConversationAt = next;
        second.NextConversationAt = next;
    }

    private static int ReplyDelayMinutes(
        int minute,
        string first,
        string second,
        int salt,
        int minMinutes,
        int maxMinutes)
    {
        var range = Math.Max(0, maxMinutes - minMinutes);
        return minMinutes
            + (int)Math.Floor(StableRoll(minute, first, second, salt) * (range + 1));
    }

    private static double StableRoll(int minute, string first, string second, int salt)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in $"{minute}|{first}|{second}|{salt}")
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (hash % 10_000) / 10_000d;
        }
    }

    private static double Effective(
        Npc npc,
        TraitEffectKind kind,
        double baseValue) =>
        Math.Clamp(
            baseValue + CrewTraitMath.Modifier(npc, kind),
            0,
            100);

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
