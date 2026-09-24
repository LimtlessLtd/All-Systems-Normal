using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #74, slice 1: stepping into a dead colleague's post. Whether
/// anyone wants to is the mind's choice (<see cref="ActionKind.AssumeRole"/>);
/// this only says whether the post is really open to them. The post must be
/// empty (no living holder aboard), the person must personally have found the
/// body of someone who held it, and they need at least a working grasp of the
/// post's core skill. Who holds which post is treated as the public crew
/// manifest, the same way the PEOPLE HERE line already shows roles; deaths
/// are not, so a post only looks open to someone who has seen the body.
/// </summary>
public static class RoleSuccessionRules
{
    /// <summary>
    /// Skill floor for covering a post. Deliberately well below the hiring
    /// floors in crew generation (72-80): a less-qualified volunteer stepping
    /// up under pressure is the behaviour the idea asks for.
    /// </summary>
    public const int MinimumSkill = 40;

    /// <summary>The skills that count towards covering a post; the best one is used.</summary>
    public static IReadOnlyList<string> SkillsFor(CrewRole role) => role switch
    {
        CrewRole.Commander => ["Leadership"],
        CrewRole.Engineer => ["Engineering"],
        CrewRole.Security => ["Security"],
        CrewRole.Doctor => ["Medicine", "First Aid"],
        CrewRole.Technician => ["Electrical"],
        CrewRole.Scientist => ["Research"],
        _ => []
    };

    public static int SkillFor(Npc npc, CrewRole role) =>
        SkillsFor(role)
            .Select(skill => npc.Skills.TryGetValue(skill, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>Nobody alive and aboard holds the post.</summary>
    public static bool IsVacant(GameState state, CrewRole role) =>
        role != CrewRole.Prisoner
        && !state.Crew.Any(holder =>
            holder.IsAlive
            && holder.IsPresent
            && !holder.IsPrisoner
            && holder.Role == role);

    /// <summary>A holder of the post whose body this person has personally found.</summary>
    public static Npc? KnownFallenHolder(GameState state, Npc observer, CrewRole role) =>
        state.Crew
            .Where(holder =>
                !holder.IsAlive
                && holder.Role == role
                && observer.DiscoveredBodies.Contains(holder.Id))
            .OrderBy(holder => holder.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    /// <summary>
    /// A post name as cognition writes it ("Doctor", "doctor"). Numbers and
    /// the Prisoner "role" are not posts anyone can take.
    /// </summary>
    public static bool TryParseRole(string? target, out CrewRole role)
    {
        role = default;
        var text = target?.Trim();
        return !string.IsNullOrEmpty(text)
            && char.IsLetter(text[0])
            && Enum.TryParse(text, ignoreCase: true, out role)
            && Enum.IsDefined(role)
            && role != CrewRole.Prisoner;
    }

    /// <summary>
    /// Whether this person may take the post now. <paramref name="reason"/> is
    /// a first-person explanation when they may not, suitable as a failed-attempt memory.
    /// </summary>
    public static bool CanAssume(GameState state, Npc npc, CrewRole role, out string reason)
    {
        if (!npc.IsAlive || !npc.IsPresent || npc.IsPrisoner || role == CrewRole.Prisoner)
        {
            reason = "I am in no position to take over anyone's post.";
            return false;
        }

        if (npc.Role == role)
        {
            reason = $"I already hold the {role} post.";
            return false;
        }

        var fallen = KnownFallenHolder(state, npc, role);
        if (fallen is null)
        {
            reason = $"Nothing I have seen tells me the {role} post is empty.";
            return false;
        }

        var holder = state.Crew.FirstOrDefault(other =>
            other.IsAlive
            && other.IsPresent
            && !other.IsPrisoner
            && other.Role == role);
        if (holder is not null)
        {
            reason = $"{holder.Name} already holds the {role} post since {fallen.Name} died.";
            return false;
        }

        if (SkillFor(npc, role) < MinimumSkill)
        {
            reason = $"I don't have the training to take over as {role}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Posts this person could step into right now, with whose death opened each.</summary>
    public static IReadOnlyList<(CrewRole Role, Npc FallenHolder)> OpenPostsFor(GameState state, Npc npc) =>
        Enum.GetValues<CrewRole>()
            .Where(role => CanAssume(state, npc, role, out _))
            .Select(role => (role, KnownFallenHolder(state, npc, role)!))
            .ToList();

    /// <summary>
    /// The fallback minds' "model citizen" baseline (ARCHITECTURE.md →
    /// Emergent-agency direction): cover an empty post you are qualified for,
    /// but not by abandoning your own. Someone else still holding your current
    /// post means you can be spared. The LLM may weigh it differently.
    /// </summary>
    public static CrewRole? FallbackPostToTake(GameState state, Npc npc)
    {
        if (npc.IsPrisoner)
            return null;

        var canBeSpared = state.Crew.Any(other =>
            other.Id != npc.Id
            && other.IsAlive
            && other.IsPresent
            && !other.IsPrisoner
            && other.Role == npc.Role);

        return canBeSpared
            ? OpenPostsFor(state, npc).Select(post => (CrewRole?)post.Role).FirstOrDefault()
            : null;
    }
}
