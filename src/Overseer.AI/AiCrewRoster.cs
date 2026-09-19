namespace Overseer.AI;

public sealed class AiCrewRoster
{
    public required List<AiCrewMember> Crew { get; init; }
}

public sealed class AiCrewMember
{
    public required string Name { get; init; }
    public required string Role { get; init; }
    public int Empathy { get; init; }
    public int Temper { get; init; }
    public int Sociability { get; init; }
    public int Courage { get; init; }
    public required List<AiCrewSkill> Skills { get; init; }
    public required List<AiCrewTrait> Traits { get; init; }
}

public sealed class AiCrewSkill
{
    public required string Name { get; init; }
    public int Value { get; init; }
}

public sealed class AiCrewTrait
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required List<AiCrewTraitEffect> Effects { get; init; }
}

public sealed class AiCrewTraitEffect
{
    public required string Kind { get; init; }
    public int Modifier { get; init; }
}
