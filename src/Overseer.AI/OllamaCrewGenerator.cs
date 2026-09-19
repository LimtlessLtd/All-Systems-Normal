using System.Text.Json;
using Microsoft.Extensions.AI;
using Overseer.Domain;

namespace Overseer.AI;

public sealed class OllamaCrewGenerator(
    IChatClient chatClient,
    RuleBasedCrewGenerator fallback) : IAiCrewGenerator
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly RuleBasedCrewGenerator _fallback = fallback;

    private static readonly CrewRole[] RequiredRoles =
        Enum.GetValues<CrewRole>();

    private static readonly HashSet<string> AllowedSkills =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Leadership",
            "Operations",
            "Engineering",
            "Reactor",
            "Security",
            "First Aid",
            "Medicine",
            "Psychology",
            "Electrical",
            "Research",
            "Athletics"
        };

    public async Task<IReadOnlyList<Npc>> GenerateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _chatClient.GetResponseAsync<AiCrewRoster>(
                BuildPrompt(),
                options: new ChatOptions
                {
                    Temperature = 1.0f,
                    MaxOutputTokens = 1800
                },
                useJsonSchemaResponseFormat: true,
                cancellationToken: cancellationToken);

            AiCrewRoster? roster = null;

            if (!response.TryGetResult(out roster) || roster is null)
            {
                roster = TryParse(response.Text);
            }

            if (roster is null)
            {
                throw new InvalidOperationException(
                    "The model did not return a valid crew roster.");
            }

            return Validate(roster);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await _fallback.GenerateAsync(cancellationToken);
        }
    }

    private static string BuildPrompt() =>
        """
        Generate a fresh six-person crew for a fictional space-station simulation.

        The people themselves should be original and varied. Generate:
        - exactly six unique realistic full names
        - exactly one person in each role: Commander, Engineer, Security, Doctor, Technician, Scientist
        - personality values Empathy, Temper, Sociability and Courage from 0 to 100
        - 2 to 4 practical skills per person, using ONLY these skill names:
          Leadership, Operations, Engineering, Reactor, Security, First Aid,
          Medicine, Psychology, Electrical, Research, Athletics
        - exactly 1, 2 or 3 MAIN personality traits per person

        The traits are important. Invent concise human-sounding trait names and
        one-sentence descriptions. Every trait must have 1 to 3 mechanical effects
        using ONLY these effect kinds:
          Empathy, Temper, Sociability, Courage, Force, Technical, Repair,
          StressResistance, SuspicionSensitivity

        Each effect Modifier must be an integer from -15 to +15.
        Effects should make intuitive sense for the trait. A trait may have both
        an advantage and a drawback. Avoid bland duplicates between people.

        Examples of the design language only (do NOT copy these exact characters):
        - "Built Like a Bulkhead" might improve Force but reduce Sociability.
        - "Hypervigilant" might improve SuspicionSensitivity but reduce StressResistance.
        - "Patient Tinkerer" might improve Technical/Repair but reduce Courage.
        - "Protective" might improve Empathy and Courage.

        Skills, numeric personality and traits should broadly agree with one another
        without making everyone an ideal stereotype. Include imperfections, awkward
        combinations and believable weaknesses. The station roles constrain their job,
        not their entire personality.

        Return only the structured roster requested by the schema.
        """;

    private static AiCrewRoster? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AiCrewRoster>(
                text,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<Npc> Validate(AiCrewRoster roster)
    {
        if (roster.Crew is null || roster.Crew.Count != RequiredRoles.Length)
        {
            throw new InvalidOperationException(
                "AI roster must contain exactly six crew.");
        }

        var byRole = new Dictionary<CrewRole, AiCrewMember>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var generated in roster.Crew)
        {
            if (!Enum.TryParse<CrewRole>(
                    generated.Role,
                    true,
                    out var role)
                || byRole.ContainsKey(role))
            {
                throw new InvalidOperationException(
                    "AI roster must contain exactly one of each required role.");
            }

            var name = Clean(generated.Name, 48);

            if (name.Length < 3 || !names.Add(name))
            {
                throw new InvalidOperationException(
                    "AI roster names must be unique and usable.");
            }

            byRole[role] = generated;
        }

        if (RequiredRoles.Any(role => !byRole.ContainsKey(role)))
        {
            throw new InvalidOperationException(
                "AI roster omitted a required role.");
        }

        return RequiredRoles
            .Select(role => BuildNpc(byRole[role], role))
            .ToList();
    }

    private static Npc BuildNpc(AiCrewMember generated, CrewRole role)
    {
        var traits = ValidateTraits(generated.Traits);

        if (traits.Count is < 1 or > 3)
        {
            throw new InvalidOperationException(
                "Every generated crew member needs one to three valid traits.");
        }

        var npc = new Npc
        {
            Name = Clean(generated.Name, 48),
            Role = role,
            CurrentRoomId = StartingRoom(role),
            Personality = new Personality(
                Math.Clamp(generated.Empathy, 0, 100),
                Math.Clamp(generated.Temper, 0, 100),
                Math.Clamp(generated.Sociability, 0, 100),
                Math.Clamp(generated.Courage, 0, 100)),
            GenerationSource = "AI generated"
        };

        foreach (var skill in generated.Skills
                     .Where(skill =>
                         !string.IsNullOrWhiteSpace(skill.Name)
                         && AllowedSkills.Contains(skill.Name))
                     .GroupBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.OrderByDescending(skill => skill.Value).First())
                     .Take(4))
        {
            npc.Skills[CanonicalSkill(skill.Name)] =
                Math.Clamp(skill.Value, 0, 100);
        }

        EnsureRoleCapability(npc);
        npc.Traits.AddRange(traits);
        return npc;
    }

    private static List<CrewTrait> ValidateTraits(
        IReadOnlyList<AiCrewTrait>? generatedTraits)
    {
        if (generatedTraits is null || generatedTraits.Count is < 1 or > 3)
        {
            return [];
        }

        var traits = new List<CrewTrait>();

        foreach (var generated in generatedTraits.Take(3))
        {
            var name = Clean(generated.Name, 42);
            var description = Clean(generated.Description, 180);

            if (name.Length < 2 || description.Length < 5)
            {
                continue;
            }

            var effects = generated.Effects
                .Where(effect =>
                    Enum.TryParse<TraitEffectKind>(
                        effect.Kind,
                        true,
                        out _))
                .Take(3)
                .Select(effect =>
                {
                    Enum.TryParse<TraitEffectKind>(
                        effect.Kind,
                        true,
                        out var kind);

                    return new CrewTraitEffect(
                        kind,
                        Math.Clamp(effect.Modifier, -15, 15));
                })
                .Where(effect => effect.Modifier != 0)
                .ToList();

            if (effects.Count == 0)
            {
                continue;
            }

            traits.Add(new CrewTrait(name, description, effects));
        }

        return traits;
    }

    private static void EnsureRoleCapability(Npc npc)
    {
        var (skill, minimum) = npc.Role switch
        {
            CrewRole.Commander => ("Leadership", 72),
            CrewRole.Engineer => ("Engineering", 78),
            CrewRole.Security => ("Security", 78),
            CrewRole.Doctor => ("Medicine", 80),
            CrewRole.Technician => ("Electrical", 76),
            CrewRole.Scientist => ("Research", 78),
            _ => ("Operations", 65)
        };

        if (!npc.Skills.TryGetValue(skill, out var value)
            || value < minimum)
        {
            npc.Skills[skill] = minimum;
        }
    }

    private static string StartingRoom(CrewRole role) => role switch
    {
        CrewRole.Commander => "control",
        CrewRole.Engineer => "engineering",
        CrewRole.Security => "corridor",
        CrewRole.Doctor => "medical",
        CrewRole.Technician => "generator",
        CrewRole.Scientist => "reactor",
        _ => "quarters"
    };

    private static string CanonicalSkill(string value) =>
        AllowedSkills.First(skill =>
            skill.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string? value, int maxLength)
    {
        var clean = (value ?? string.Empty).Trim();
        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength].Trim();
    }
}
