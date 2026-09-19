using Overseer.Domain;

namespace Overseer.AI;

/// <summary>
/// Resilient offline fallback only. Normal server sessions use Ollama to
/// generate the roster. The static Pages build has its own explicit demo crew.
/// </summary>
public sealed class RuleBasedCrewGenerator : IAiCrewGenerator
{
    public Task<IReadOnlyList<Npc>> GenerateAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Npc> crew =
        [
            Create("Alex Mercer", CrewRole.Commander, "control",
                new Personality(70, 38, 63, 72),
                ("Leadership", 88), ("Operations", 82),
                Trait("Decisive", "Acts quickly once a course seems necessary.",
                    (TraitEffectKind.Courage, 8), (TraitEffectKind.StressResistance, 5))),
            Create("Priya Shah", CrewRole.Engineer, "engineering",
                new Personality(65, 42, 55, 69),
                ("Engineering", 94), ("Reactor", 82),
                Trait("Methodical", "Works technical problems step by step.",
                    (TraitEffectKind.Repair, 10), (TraitEffectKind.Technical, 8))),
            Create("Jonah Price", CrewRole.Security, "corridor",
                new Personality(48, 67, 46, 84),
                ("Security", 91), ("Athletics", 86),
                Trait("Stubborn", "Keeps pushing against physical obstacles.",
                    (TraitEffectKind.Force, 12), (TraitEffectKind.Courage, 5))),
            Create("Maya Torres", CrewRole.Doctor, "medical",
                new Personality(90, 25, 77, 62),
                ("Medicine", 96), ("Psychology", 83),
                Trait("Protective", "Other people's safety weighs heavily on her decisions.",
                    (TraitEffectKind.Empathy, 13), (TraitEffectKind.SuspicionSensitivity, 4))),
            Create("Owen Brooks", CrewRole.Technician, "generator",
                new Personality(57, 53, 60, 71),
                ("Electrical", 91), ("Engineering", 75),
                Trait("Resourceful", "Finds practical ways to get broken hardware working.",
                    (TraitEffectKind.Repair, 12), (TraitEffectKind.Technical, 8))),
            Create("Leila Morgan", CrewRole.Scientist, "reactor",
                new Personality(73, 36, 58, 57),
                ("Research", 95), ("Reactor", 79),
                Trait("Sceptical", "Notices inconsistencies and questions convenient explanations.",
                    (TraitEffectKind.SuspicionSensitivity, 11), (TraitEffectKind.Technical, 4)))
        ];

        return Task.FromResult(crew);
    }

    private static Npc Create(
        string name,
        CrewRole role,
        string room,
        Personality personality,
        (string Name, int Value) firstSkill,
        (string Name, int Value) secondSkill,
        params CrewTrait[] traits)
    {
        var npc = new Npc
        {
            Name = name,
            Role = role,
            CurrentRoomId = room,
            Personality = personality,
            GenerationSource = "Fallback generator"
        };

        npc.Skills[firstSkill.Name] = firstSkill.Value;
        npc.Skills[secondSkill.Name] = secondSkill.Value;
        npc.Traits.AddRange(traits);
        return npc;
    }

    private static CrewTrait Trait(
        string name,
        string description,
        params (TraitEffectKind Kind, int Modifier)[] effects) =>
        new(
            name,
            description,
            effects.Select(effect =>
                new CrewTraitEffect(effect.Kind, effect.Modifier)).ToList());
}
