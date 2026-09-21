using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Model-free fresh-roster generation for the static browser runtime.
/// Fresh stations target twelve people by default. The same seed always
/// produces the same roster, including IDs, names, personality, skills and
/// mechanical traits.
/// </summary>
public static class SeededCrewRosterGenerator
{
    private static readonly CrewRole[] Roles = Enum.GetValues<CrewRole>()
        .Where(role => role != CrewRole.Prisoner)
        .ToArray();

    public static IReadOnlyList<Npc> Generate(int seed, int count = 12)
    {
        if (count <= 0)
            return [];

        return Enumerable.Range(0, count)
            .Select(index => CreateCrew(seed, Roles[index % Roles.Length], index))
            .ToList();
    }

    private static Npc CreateCrew(int seed, CrewRole role, int roleIndex)
    {
        var profile = ProfileFor(role);
        var npc = new Npc
        {
            Id = DeterministicGuid(seed, roleIndex),
            Name = profile.Names[Pick(seed, roleIndex * 17 + 1, profile.Names.Count)],
            Role = role,
            CurrentRoomId = profile.StartRoom,
            Personality = new Personality(
                Vary(profile.Personality.Empathy, seed, roleIndex * 17 + 2),
                Vary(profile.Personality.Temper, seed, roleIndex * 17 + 3),
                Vary(profile.Personality.Sociability, seed, roleIndex * 17 + 4),
                Vary(profile.Personality.Courage, seed, roleIndex * 17 + 5)),
            GenerationSource = "Seeded browser roster"
        };

        for (var skillIndex = 0; skillIndex < profile.Skills.Count; skillIndex++)
        {
            var skill = profile.Skills[skillIndex];
            npc.Skills[skill.Name] = Math.Clamp(
                skill.Value + SignedOffset(seed, roleIndex * 31 + skillIndex + 50, 6),
                0,
                100);
        }

        var traitCount = 1 + Pick(seed, roleIndex * 17 + 9, 3);
        var traitStart = Pick(seed, roleIndex * 17 + 10, profile.Traits.Count);

        for (var i = 0; i < traitCount; i++)
        {
            npc.Traits.Add(profile.Traits[(traitStart + i) % profile.Traits.Count]);
        }

        return npc;
    }

    private static CrewProfile ProfileFor(CrewRole role) => role switch
    {
        CrewRole.Commander => new(
            ["Avery Knox", "Elena Marlow", "Malik Soren", "Ruth Calder", "Tomas Vey"],
            "control",
            new Personality(68, 39, 66, 76),
            [("Leadership", 88), ("Operations", 81), ("First Aid", 55)],
            [
                Trait("Command Presence", "Projects confidence when other people hesitate.",
                    (TraitEffectKind.Courage, 8), (TraitEffectKind.Sociability, 4)),
                Trait("Measured", "Usually slows down before committing the crew to a risky course.",
                    (TraitEffectKind.StressResistance, 8), (TraitEffectKind.Temper, -4)),
                Trait("Protective Authority", "Takes responsibility for danger personally.",
                    (TraitEffectKind.Empathy, 6), (TraitEffectKind.Courage, 5)),
                Trait("Control Focused", "Dislikes unexplained deviations from procedure.",
                    (TraitEffectKind.SuspicionSensitivity, 7), (TraitEffectKind.Sociability, -3))
            ]),

        CrewRole.Engineer => new(
            ["Inez Park", "Priya Desai", "Rowan Beck", "Keira Holt", "Samir Vale"],
            "engineering",
            new Personality(58, 44, 48, 70),
            [("Engineering", 93), ("Reactor", 80), ("Electrical", 73)],
            [
                Trait("Patient Tinkerer", "Stays with difficult machinery longer than most.",
                    (TraitEffectKind.Repair, 11), (TraitEffectKind.Technical, 7)),
                Trait("Systems Intuition", "Spots technical failure patterns quickly.",
                    (TraitEffectKind.Technical, 11), (TraitEffectKind.Repair, 7)),
                Trait("Risk Calculator", "Prefers understood hazards to improvised heroics.",
                    (TraitEffectKind.StressResistance, 7), (TraitEffectKind.Courage, -4)),
                Trait("Machine Loyalist", "Trusts instrumentation before reassurance.",
                    (TraitEffectKind.SuspicionSensitivity, 7), (TraitEffectKind.Empathy, -2))
            ]),

        CrewRole.Security => new(
            ["Cal Rowan", "Jonah Price", "Talia Cross", "Marcus Dane", "Niko Grant"],
            "corridor",
            new Personality(45, 64, 48, 84),
            [("Security", 92), ("Athletics", 87), ("First Aid", 52)],
            [
                Trait("Heavy Handed", "Trusts physical solutions when pressure rises.",
                    (TraitEffectKind.Force, 13), (TraitEffectKind.Temper, 5)),
                Trait("Watchful", "Notices patterns that suggest somebody is hiding something.",
                    (TraitEffectKind.SuspicionSensitivity, 10), (TraitEffectKind.StressResistance, -3)),
                Trait("Steady Nerves", "Keeps functioning when a compartment becomes dangerous.",
                    (TraitEffectKind.Courage, 9), (TraitEffectKind.StressResistance, 7)),
                Trait("Blunt", "Says what seems wrong even when it creates friction.",
                    (TraitEffectKind.Sociability, -5), (TraitEffectKind.Courage, 5))
            ]),

        CrewRole.Doctor => new(
            ["Mara Bell", "Nadia Okafor", "Lian Foster", "Sofia Quill", "Arun Mehta"],
            "medical",
            new Personality(88, 25, 72, 61),
            [("Medicine", 95), ("Psychology", 83), ("First Aid", 88)],
            [
                Trait("Protective", "Other people's danger is hard to ignore.",
                    (TraitEffectKind.Empathy, 12), (TraitEffectKind.Courage, 4)),
                Trait("Bedside Reader", "Reads emotional strain before people state it directly.",
                    (TraitEffectKind.Empathy, 8), (TraitEffectKind.SuspicionSensitivity, 5)),
                Trait("Conflict Averse", "Dislikes direct confrontation even when concerned.",
                    (TraitEffectKind.Courage, -5), (TraitEffectKind.Temper, -4)),
                Trait("Clinical Focus", "Can compartmentalise distress while treating an emergency.",
                    (TraitEffectKind.StressResistance, 9), (TraitEffectKind.Sociability, -2))
            ]),

        CrewRole.Technician => new(
            ["Theo Grant", "Owen Brooks", "Felix Ward", "Mina Cho", "Jules Mercer"],
            "generator",
            new Personality(56, 51, 62, 70),
            [("Electrical", 91), ("Engineering", 76), ("Operations", 66)],
            [
                Trait("Improviser", "Finds unconventional fixes quickly.",
                    (TraitEffectKind.Repair, 12), (TraitEffectKind.Technical, 6)),
                Trait("Nosy", "Notices when system behaviour does not add up.",
                    (TraitEffectKind.SuspicionSensitivity, 8)),
                Trait("Restless", "Does poorly under prolonged confinement.",
                    (TraitEffectKind.StressResistance, -5), (TraitEffectKind.Sociability, 3)),
                Trait("Practical", "Prefers a working patch to a perfect theory.",
                    (TraitEffectKind.Repair, 8), (TraitEffectKind.Courage, 3))
            ]),

        CrewRole.Scientist => new(
            ["Sana Velez", "Emma Voss", "Leila Morgan", "Hana Rios", "Victor Ames"],
            "reactor",
            new Personality(70, 36, 55, 57),
            [("Research", 94), ("Reactor", 74), ("Operations", 62)],
            [
                Trait("Sceptical", "Questions convenient explanations.",
                    (TraitEffectKind.SuspicionSensitivity, 10), (TraitEffectKind.Technical, 3)),
                Trait("Analytical", "Responds to anomalies by looking for a causal explanation.",
                    (TraitEffectKind.Technical, 7), (TraitEffectKind.StressResistance, 4)),
                Trait("Curiosity Driven", "Will keep investigating an unresolved inconsistency.",
                    (TraitEffectKind.SuspicionSensitivity, 7), (TraitEffectKind.Courage, 3)),
                Trait("Detached", "Can prioritise evidence over social reassurance.",
                    (TraitEffectKind.Empathy, -4), (TraitEffectKind.StressResistance, 6))
            ]),

        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    private static CrewTrait Trait(
        string name,
        string description,
        params (TraitEffectKind Kind, int Modifier)[] effects) =>
        new(
            name,
            description,
            effects.Select(effect =>
                new CrewTraitEffect(effect.Kind, effect.Modifier)).ToList());

    private static double Vary(double value, int seed, int salt) =>
        Math.Clamp(value + SignedOffset(seed, salt, 8), 0, 100);

    private static int SignedOffset(int seed, int salt, int magnitude) =>
        Pick(seed, salt, (magnitude * 2) + 1) - magnitude;

    private static int Pick(int seed, int salt, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return (int)((uint)Mix(seed, salt) % (uint)count);
    }

    private static int Mix(int seed, int salt)
    {
        unchecked
        {
            var value = (uint)seed + (0x9E3779B9u * (uint)(salt + 1));
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            value *= 0xC2B2AE35u;
            value ^= value >> 16;
            return (int)value;
        }
    }

    private static Guid DeterministicGuid(int seed, int roleIndex)
    {
        Span<byte> bytes = stackalloc byte[16];
        var state = (uint)Mix(seed, roleIndex + 500);

        for (var i = 0; i < bytes.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[i] = (byte)(state & 0xFF);
        }

        return new Guid(bytes);
    }

    private sealed record CrewProfile(
        IReadOnlyList<string> Names,
        string StartRoom,
        Personality Personality,
        IReadOnlyList<(string Name, int Value)> Skills,
        IReadOnlyList<CrewTrait> Traits);
}
