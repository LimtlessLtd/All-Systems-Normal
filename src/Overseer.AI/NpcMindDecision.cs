namespace Overseer.AI;

public sealed class NpcMindDecision
{
    public required string Action { get; init; }
    public string? TargetId { get; init; }
    public required string Goal { get; init; }
    public required string Reason { get; init; }
    public int Urgency { get; init; }
}
