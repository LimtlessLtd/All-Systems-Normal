using Overseer.Domain;

namespace Overseer.Simulation;

public enum ConversationTopic
{
    SmallTalk,
    OverseerDoubt,
    Gossip,
    News,
    Wellbeing
}

/// <summary>
/// One spoken exchange: what the initiator says, what the listener answers,
/// and what the listener takes away from it.
/// </summary>
public sealed record ConversationExchange(
    ConversationTopic Topic,
    string Opening,
    string Reply,
    string? LogLine);

/// <summary>
/// Gives conversations content. The initiator talks about what is actually on
/// their mind — doubts about Overseer, a colleague they feel strongly about,
/// something that happened, or how they are coping — and the listener's
/// answer depends on their own view. Gossip nudges the listener's opinion of
/// the person discussed, weighted by how far they trust the speaker; nothing
/// here edits beliefs about Overseer, which stay evidence-driven.
/// </summary>
public static class ConversationTopicSystem
{
    private const double GossipStep = 1.2;
    private const int MaxRumourHopCount = 3;
    private static readonly TimeSpan NewsWindow = TimeSpan.FromHours(3);
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromHours(6);

    public static ConversationExchange Converse(
        GameState state,
        Npc speaker,
        Npc listener,
        double roll)
    {
        ArgumentNullException.ThrowIfNull(state);

        var candidates = new List<(ConversationTopic Topic, double Weight)>
        {
            (ConversationTopic.SmallTalk, 0.35)
        };

        if (speaker.OverseerSuspicion >= 30)
            candidates.Add((ConversationTopic.OverseerDoubt, speaker.OverseerSuspicion / 100d * 1.5));

        var gossipTarget = StrongestFeeling(state, speaker, listener);
        if (gossipTarget is { } feeling)
            candidates.Add((ConversationTopic.Gossip, Math.Min(1.2, feeling.Heat / 30d)));

        var news = RecentNews(state, speaker, listener);
        if (news is not null)
            candidates.Add((ConversationTopic.News, 0.9));

        if (speaker.Stress >= 50 || speaker.Fatigue >= 65 || speaker.Hunger >= 60)
            candidates.Add((ConversationTopic.Wellbeing, 0.6));

        var pick = Math.Clamp(roll, 0, 0.9999) * candidates.Sum(candidate => candidate.Weight);
        var topic = candidates[^1].Topic;
        foreach (var candidate in candidates)
        {
            if (pick < candidate.Weight)
            {
                topic = candidate.Topic;
                break;
            }

            pick -= candidate.Weight;
        }

        return topic switch
        {
            ConversationTopic.OverseerDoubt => OverseerDoubt(state, speaker, listener, roll),
            ConversationTopic.Gossip when gossipTarget is { } target => Gossip(state, speaker, listener, target, roll),
            ConversationTopic.News when news is not null => News(state, speaker, listener, news, roll),
            ConversationTopic.Wellbeing => Wellbeing(speaker, listener),
            _ => SmallTalk(roll)
        };
    }

    /// <summary>
    /// What an argument is about, so it is not just noise: disagreement over
    /// Overseer, a personal grievance, or plain friction.
    /// </summary>
    public static (string Opening, string Reply, string? Topic) Argument(
        Npc first,
        Npc second,
        Relationship firstToSecond,
        double roll)
    {
        if (Math.Abs(first.OverseerSuspicion - second.OverseerSuspicion) >= 25)
        {
            return first.OverseerSuspicion > second.OverseerSuspicion
                ? ("You're really defending that machine?", "And you're being paranoid.", "Overseer")
                : ("You're being paranoid about Overseer.", "And you'd trust it with your life?", "Overseer");
        }

        if (firstToSecond.Resentment >= 50)
        {
            return roll < 0.5
                ? ("You've been working against me.", "I don't know what you're talking about.", "trust")
                : ($"I'm tired of covering for you, {FirstName(second.Name)}.", "Nobody asked you to.", "workload");
        }

        return roll < 0.5
            ? ("That's not what happened.", "Don't put this on me.", null)
            : ("I'm done listening to this.", "Back off.", null);
    }

    private static ConversationExchange OverseerDoubt(
        GameState state,
        Npc speaker,
        Npc listener,
        double roll)
    {
        var opening = (int)(roll * 3) switch
        {
            0 => "Doesn't the station feel off to you lately?",
            1 => "I don't trust what Overseer is doing.",
            _ => "Have you noticed the systems acting strangely?"
        };

        var reply = listener.OverseerSuspicion >= 35
            ? "I've noticed it too."
            : listener.OverseerCredibility >= 60 && listener.OverseerSuspicion < 20
                ? "Overseer's kept us alive so far."
                : "Maybe. Keep me posted.";

        Remember(state, listener, $"{speaker.Name} told me they don't trust Overseer.", 0.35);

        return new ConversationExchange(
            ConversationTopic.OverseerDoubt,
            opening,
            reply,
            $"{speaker.Name} shares doubts about Overseer with {listener.Name}.");
    }

    private static ConversationExchange Gossip(
        GameState state,
        Npc speaker,
        Npc listener,
        (Relationship Relationship, double Heat, bool Negative) target,
        double roll)
    {
        var subject = target.Relationship.PersonName;
        var subjectFirstName = FirstName(subject);
        var trustFactor = listener.Relationships.TryGetValue(speaker.Name, out var listenerToSpeaker)
            ? listenerToSpeaker.Trust / 100d
            : 0.5;
        listener.Relationships.TryGetValue(subject, out var listenerToSubject);

        if (target.Negative)
        {
            var opening = roll < 0.5
                ? $"{subjectFirstName} has been impossible lately."
                : $"I can't work with {subjectFirstName} much longer.";

            // A friend of the person being run down pushes back instead.
            if (listenerToSubject is { Affinity: >= 65 })
            {
                if (listenerToSpeaker is not null)
                    listenerToSpeaker.Resentment = Clamp(listenerToSpeaker.Resentment + 1.0);

                Remember(state, listener, $"{speaker.Name} ran {subject} down to me.", 0.4);
                return new ConversationExchange(
                    ConversationTopic.Gossip,
                    opening,
                    $"That's not fair to {subjectFirstName}.",
                    $"{listener.Name} defends {subject} against {speaker.Name}.");
            }

            if (listenerToSubject is not null)
            {
                listenerToSubject.Resentment = Clamp(listenerToSubject.Resentment + (GossipStep * trustFactor));
                listenerToSubject.Trust = Clamp(listenerToSubject.Trust - (0.8 * trustFactor));
            }

            Remember(state, listener, $"{speaker.Name} complained to me about {subject}.", 0.4);
            return new ConversationExchange(
                ConversationTopic.Gossip,
                opening,
                listenerToSubject is { Resentment: >= 30 } ? "Tell me about it." : "Maybe you two should talk.",
                $"{speaker.Name} complains to {listener.Name} about {subject}.");
        }

        if (listenerToSubject is not null && listenerToSubject.Resentment < 30)
        {
            listenerToSubject.Affinity = Clamp(listenerToSubject.Affinity + (GossipStep * trustFactor));
            listenerToSubject.Trust = Clamp(listenerToSubject.Trust + (0.6 * trustFactor));
        }

        Remember(state, listener, $"{speaker.Name} spoke well of {subject}.", 0.3);
        return new ConversationExchange(
            ConversationTopic.Gossip,
            roll < 0.5
                ? $"{subjectFirstName} really pulled their weight today."
                : $"I'm glad {subjectFirstName} is on this crew.",
            listenerToSubject is { Resentment: >= 30 } ? "We'll see." : "Yeah, they're solid.",
            $"{speaker.Name} speaks well of {subject} to {listener.Name}.");
    }

    private static ConversationExchange News(
        GameState state,
        Npc speaker,
        Npc listener,
        Memory news,
        double roll)
    {
        var hopCount = Math.Min(news.RumourHopCount + 1, MaxRumourHopCount);
        var coreDescription = news.RumourCoreDescription ?? news.Description;

        Remember(
            state,
            listener,
            RetoldMemoryText(speaker.Name, coreDescription, hopCount),
            Math.Clamp(news.Importance * 0.7, 0.3, 0.7),
            hopCount,
            coreDescription);

        return new ConversationExchange(
            ConversationTopic.News,
            roll < 0.5 ? "You need to hear about this." : "Something happened earlier.",
            "Go on.",
            RetoldLogLine(speaker.Name, listener.Name, coreDescription, hopCount));
    }

    /// <summary>
    /// Owner idea #4: each retelling deterministically degrades certainty
    /// and eventually specificity, instead of copying the previous holder's
    /// memory verbatim. A one-hop retelling (the common case: hearing
    /// something from whoever actually witnessed it) keeps the exact
    /// original wording — only a rumour that has already passed through
    /// someone else's retelling degrades further, first into hedged
    /// language and then, past <see cref="MaxRumourHopCount"/>, into a
    /// fixed template that carries no real content at all. This can only
    /// ever be reached by an event important enough to keep clearing the
    /// existing <c>Importance >= 0.5</c> newsworthiness bar after each
    /// hop's own importance decay.
    /// </summary>
    private static string RetoldMemoryText(string speakerName, string description, int hopCount) => hopCount switch
    {
        <= 1 => $"{speakerName} told me: {description}",
        2 => $"{speakerName} thinks: {description}",
        _ => $"{speakerName} mentioned hearing some rumour about it, but couldn't say exactly what."
    };

    private static string RetoldLogLine(string speakerName, string listenerName, string description, int hopCount) => hopCount switch
    {
        <= 1 => $"{speakerName} tells {listenerName}: {description}",
        2 => $"{speakerName} tells {listenerName} what they think happened: {description}",
        _ => $"{speakerName} tells {listenerName} some half-remembered rumour."
    };

    private static ConversationExchange Wellbeing(Npc speaker, Npc listener)
    {
        var opening = speaker.Stress >= 50 ? "I'm wound pretty tight right now."
            : speaker.Fatigue >= 65 ? "I'm running on fumes."
            : "I could really use a proper meal.";

        if (listener.Personality.Empathy >= 60)
        {
            speaker.Stress = Clamp(speaker.Stress - 3);
            return new ConversationExchange(
                ConversationTopic.Wellbeing,
                opening,
                "Take a breather. I've got things here.",
                null);
        }

        return new ConversationExchange(
            ConversationTopic.Wellbeing,
            opening,
            "We're all stretched thin.",
            null);
    }

    private static ConversationExchange SmallTalk(double roll)
    {
        var openings = new[]
        {
            "How are you holding up?",
            "Long shift.",
            "Anything strange today?",
            "You doing okay?",
            "Got a minute?"
        };
        var replies = new[]
        {
            "I'm alright. You?",
            "Tell me about it.",
            "Nothing I can't handle.",
            "Yeah. Just tired.",
            "Sure. What's up?"
        };

        var index = Math.Clamp((int)(roll * openings.Length), 0, openings.Length - 1);
        return new ConversationExchange(ConversationTopic.SmallTalk, openings[index], replies[index], null);
    }

    private static (Relationship Relationship, double Heat, bool Negative)? StrongestFeeling(
        GameState state,
        Npc speaker,
        Npc listener)
    {
        (Relationship Relationship, double Heat, bool Negative)? strongest = null;

        foreach (var relationship in speaker.Relationships.Values.OrderBy(r => r.PersonName, StringComparer.Ordinal))
        {
            if (relationship.PersonName.Equals(listener.Name, StringComparison.OrdinalIgnoreCase)
                || !state.Crew.Any(member =>
                    member.IsAlive
                    && member.IsPresent
                    && member.Name.Equals(relationship.PersonName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var negativeHeat = relationship.Resentment - 25;
            var positiveHeat = relationship.Affinity - 65;
            var heat = Math.Max(negativeHeat, positiveHeat);

            if (heat > 0 && (strongest is null || heat > strongest.Value.Heat))
                strongest = (relationship, heat, negativeHeat >= positiveHeat);
        }

        return strongest;
    }

    private static Memory? RecentNews(GameState state, Npc speaker, Npc listener) =>
        speaker.Memories
            .Where(memory =>
                memory.Importance >= 0.5
                && state.Elapsed - memory.OccurredAt <= NewsWindow
                && !memory.Description.StartsWith("I decided to:", StringComparison.Ordinal)
                // Nobody needs to be told news about themselves.
                && !memory.Description.Contains(listener.Name, StringComparison.OrdinalIgnoreCase)
                && !listener.Memories.Any(heard => heard.Description.EndsWith(memory.Description, StringComparison.Ordinal)))
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.OccurredAt)
            .FirstOrDefault();

    private static void Remember(
        GameState state,
        Npc npc,
        string description,
        double importance,
        int rumourHopCount = 0,
        string? rumourCoreDescription = null)
    {
        // Hearing the same thing again adds nothing new.
        if (npc.Memories.Any(memory =>
                memory.Description.Equals(description, StringComparison.Ordinal)
                && state.Elapsed - memory.OccurredAt < RepeatWindow))
            return;

        npc.Memories.Add(new Memory(description, state.Elapsed, importance, rumourHopCount, rumourCoreDescription));
    }

    private static string FirstName(string name)
    {
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space] : name;
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
