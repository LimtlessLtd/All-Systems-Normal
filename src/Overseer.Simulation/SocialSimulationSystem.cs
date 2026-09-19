using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class SocialSimulationSystem
{
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);

        if (minute <= 0 || minute % 5 != 0)
        {
            return;
        }

        var living = state.Crew
            .Where(npc => npc.IsAlive && npc.Movement is null)
            .ToList();

        foreach (var roomGroup in living.GroupBy(npc => npc.CurrentRoomId))
        {
            var occupants = roomGroup.OrderBy(npc => npc.Name).ToList();

            for (var i = 0; i < occupants.Count; i++)
            {
                for (var j = i + 1; j < occupants.Count; j++)
                {
                    ResolvePair(state, occupants[i], occupants[j], minute);
                }
            }
        }
    }

    private static void ResolvePair(GameState state, Npc first, Npc second, int minute)
    {
        var firstToSecond = first.Relationships[second.Name];
        var secondToFirst = second.Relationships[first.Name];

        if (TryIntimacy(
                state,
                first,
                second,
                firstToSecond,
                secondToFirst))
        {
            return;
        }

        if (TryViolence(state, first, second, firstToSecond, minute, salt: 1)
            || TryViolence(state, second, first, secondToFirst, minute, salt: 2))
        {
            return;
        }

        var combinedStress = (first.Stress + second.Stress) / 2;
        var combinedResentment = (firstToSecond.Resentment + secondToFirst.Resentment) / 2;
        var friction = combinedStress + combinedResentment
            + ((first.Personality.Temper + second.Personality.Temper) / 5);

        var roll = StableRoll(minute, first.Name, second.Name, 11);

        if (friction >= 85 && roll < 0.62)
        {
            Argue(state, first, second, firstToSecond, secondToFirst, minute);
            return;
        }

        var wantsCompany =
            first.SocialNeed >= 34
            || second.SocialNeed >= 34
            || first.CurrentAction.Kind is ActionKind.Talk or ActionKind.Socialize
            || second.CurrentAction.Kind is ActionKind.Talk or ActionKind.Socialize
            || state.Facility.Rooms[first.CurrentRoomId].Type == RoomType.Recreation;

        var socialChance = Math.Clamp(
            0.16
            + ((first.Personality.Sociability + second.Personality.Sociability) / 500),
            0.16,
            0.48);

        if (wantsCompany && roll < socialChance)
        {
            Socialize(state, first, second, firstToSecond, secondToFirst, minute);
        }
    }

    private static bool TryIntimacy(
        GameState state,
        Npc first,
        Npc second,
        Relationship firstToSecond,
        Relationship secondToFirst)
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

        SetBubble(
            first,
            "Want some privacy?",
            NpcBubbleKind.Speech,
            state.Elapsed,
            4);
        SetBubble(
            second,
            "Yeah. Come on.",
            NpcBubbleKind.Speech,
            state.Elapsed,
            4);

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
            + ((first.Personality.Sociability + second.Personality.Sociability) / 200);

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

        first.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(8);
        second.RoutineUntil = state.Elapsed + TimeSpan.FromMinutes(8);

        var lineIndex = (int)(StableRoll(minute, first.Name, second.Name, 31) * 5);
        var firstLines = new[]
        {
            "How are you holding up?",
            "Long shift.",
            "Anything strange today?",
            "You doing okay?",
            "Got a minute?"
        };
        var secondLines = new[]
        {
            "I'm alright. You?",
            "Tell me about it.",
            "Nothing I can't handle.",
            "Yeah. Just tired.",
            "Sure. What's up?"
        };

        SetBubble(
            first,
            firstLines[Math.Clamp(lineIndex, 0, firstLines.Length - 1)],
            NpcBubbleKind.Speech,
            state.Elapsed,
            4);
        SetBubble(
            second,
            secondLines[Math.Clamp(lineIndex, 0, secondLines.Length - 1)],
            NpcBubbleKind.Speech,
            state.Elapsed,
            4);

        if ((firstToSecond.Conversations + secondToFirst.Conversations) % 8 == 0)
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
        first.Stress = Clamp(first.Stress + 2.5);
        second.Stress = Clamp(second.Stress + 2.5);
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

        var firstLine = StableRoll(minute, first.Name, second.Name, 55) < 0.5
            ? "That's not what happened."
            : "I'm done listening to this.";
        var secondLine = StableRoll(minute, second.Name, first.Name, 56) < 0.5
            ? "Don't put this on me."
            : "Back off.";

        SetBubble(first, firstLine, NpcBubbleKind.Speech, state.Elapsed, 4);
        SetBubble(second, secondLine, NpcBubbleKind.Speech, state.Elapsed, 4);

        first.Memories.Add(new Memory(
            $"Argument with {second.Name}.",
            state.Elapsed,
            0.55));
        second.Memories.Add(new Memory(
            $"Argument with {first.Name}.",
            state.Elapsed,
            0.55));

        Log(state, $"{first.Name} and {second.Name} get into an argument.");
    }

    private static bool TryViolence(
        GameState state,
        Npc aggressor,
        Npc target,
        Relationship relationship,
        int minute,
        int salt)
    {
        if (!aggressor.IsAlive
            || !target.IsAlive
            || relationship.Resentment < 82
            || aggressor.Stress < 78
            || aggressor.Personality.Temper < 60)
        {
            return false;
        }

        var pressure =
            (relationship.Resentment - 82) * 0.012
            + (aggressor.Stress - 78) * 0.009
            + (aggressor.Personality.Temper - 60) * 0.006;

        var chance = Math.Clamp(pressure, 0.02, 0.42);

        if (StableRoll(minute, aggressor.Name, target.Name, 100 + salt) >= chance)
        {
            return false;
        }

        var damage = 18
            + (aggressor.Personality.Temper * 0.18)
            + (relationship.Resentment * 0.11);

        target.Health = Clamp(target.Health - damage);
        target.Fear = Clamp(target.Fear + 30);
        target.Stress = Clamp(target.Stress + 25);
        aggressor.Stress = Clamp(aggressor.Stress + 8);
        relationship.Resentment = Clamp(relationship.Resentment + 4);

        aggressor.CurrentAction = new NpcAction(
            ActionKind.Attack,
            target.Name,
            $"Attacking {target.Name} after escalating conflict.");

        SetBubble(
            aggressor,
            "Stay out of my way!",
            NpcBubbleKind.Alert,
            state.Elapsed,
            3);
        SetBubble(
            target,
            "Help!",
            NpcBubbleKind.Alert,
            state.Elapsed,
            4);

        foreach (var witness in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.Id != aggressor.Id
                     && npc.Id != target.Id
                     && npc.CurrentRoomId == aggressor.CurrentRoomId))
        {
            witness.Fear = Clamp(witness.Fear + 22);
            witness.Stress = Clamp(witness.Stress + 18);
            witness.Memories.Add(new Memory(
                $"Witnessed {aggressor.Name} attack {target.Name}.",
                state.Elapsed,
                0.92));

            SetBubble(
                witness,
                "What the hell?!",
                NpcBubbleKind.Alert,
                state.Elapsed,
                3);

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

        return true;
    }

    private static void SetBubble(
        Npc npc,
        string text,
        NpcBubbleKind kind,
        TimeSpan now,
        int durationMinutes)
    {
        npc.Bubble = new NpcBubble(
            text,
            kind,
            now,
            now + TimeSpan.FromMinutes(durationMinutes));
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

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
