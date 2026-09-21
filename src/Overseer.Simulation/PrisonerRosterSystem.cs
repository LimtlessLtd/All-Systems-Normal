using Overseer.Domain;

namespace Overseer.Simulation;

public static class PrisonerRosterSystem
{
    public static IReadOnlyList<Npc> Compose(
        IReadOnlyList<Npc> crew,
        ScenarioDefinition scenario)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(scenario);

        if (scenario.Prisoners is not { Count: > 0 })
            return crew;

        var result = crew.ToList();
        foreach (var definition in scenario.Prisoners)
        {
            if (result.Any(npc => npc.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(CreatePrisoner(scenario.Id, definition));
        }

        return result;
    }

    private static Npc CreatePrisoner(string scenarioId, PrisonerDefinition definition)
    {
        var profile = definition.DangerLevel switch
        {
            PrisonerDangerLevel.Low => new Personality(55, 42, 48, 48),
            PrisonerDangerLevel.Moderate => new Personality(42, 58, 44, 58),
            PrisonerDangerLevel.High => new Personality(31, 76, 38, 70),
            PrisonerDangerLevel.Extreme => new Personality(20, 91, 30, 82),
            _ => new Personality(45, 55, 45, 55)
        };

        var npc = new Npc
        {
            Id = DeterministicGuid($"{scenarioId}|{definition.Name}"),
            Name = definition.Name,
            Role = CrewRole.Prisoner,
            CurrentRoomId = definition.StartRoomId,
            Personality = profile,
            IsPrisoner = true,
            PrisonerDangerLevel = definition.DangerLevel,
            PrisonerViolenceBias = definition.ViolenceBias,
            GenerationSource = "Scenario prisoner manifest",
            Stress = definition.DangerLevel >= PrisonerDangerLevel.High ? 48 : 28
        };

        npc.Skills["Athletics"] = definition.DangerLevel switch
        {
            PrisonerDangerLevel.Extreme => 88,
            PrisonerDangerLevel.High => 76,
            PrisonerDangerLevel.Moderate => 58,
            _ => 42
        };

        FoodPreferenceRules.EnsureDefaults(npc);
        return npc;
    }

    private static Guid DeterministicGuid(string value)
    {
        Span<byte> bytes = stackalloc byte[16];
        unchecked
        {
            uint state = 2166136261;
            foreach (var ch in value)
            {
                state ^= ch;
                state *= 16777619;
            }

            for (var index = 0; index < bytes.Length; index++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                bytes[index] = (byte)(state & 0xff);
            }
        }

        return new Guid(bytes);
    }
}
