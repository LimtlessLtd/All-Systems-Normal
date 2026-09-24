using Overseer.Domain;

namespace Overseer.Simulation;

public static class CrewRosterScalingSystem
{
    public const int DefaultTargetSize = 12;

    public static IReadOnlyList<Npc> EnsureTargetSize(
        IReadOnlyList<Npc> supplied,
        string seedMaterial,
        int targetSize = DefaultTargetSize)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        var result = supplied.ToList();
        if (result.Count >= targetSize)
            return result;

        var names = result.Select(npc => npc.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = result.Select(npc => npc.Id).ToHashSet();
        var seed = StableSeed(seedMaterial);

        for (var pass = 0; result.Count < targetSize && pass < 16; pass++)
        {
            foreach (var candidate in SeededCrewRosterGenerator.Generate(seed + pass, targetSize * 2))
            {
                if (result.Count >= targetSize)
                    break;
                if (!names.Add(candidate.Name) || !ids.Add(candidate.Id))
                    continue;

                candidate.GenerationSource = "Seeded roster supplement";
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns exactly <paramref name="size"/> crew: supplements a short
    /// generated roster like <see cref="EnsureTargetSize"/>, and keeps only the
    /// first <paramref name="size"/> members of a longer one so a sampled
    /// <see cref="RosterComposition"/> is honoured.
    /// </summary>
    public static IReadOnlyList<Npc> FitToSize(
        IReadOnlyList<Npc> supplied,
        string seedMaterial,
        int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        return EnsureTargetSize(supplied, seedMaterial, size).Take(size).ToList();
    }

    public static int StableSeed(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }
}
