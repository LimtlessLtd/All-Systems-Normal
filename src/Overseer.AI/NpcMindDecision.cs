namespace Overseer.AI;

public sealed class NpcMindDecision
{
    public required string Action { get; init; }
    public string? TargetId { get; init; }
    public required string Goal { get; init; }
    public required string Reason { get; init; }
    public int Urgency { get; init; }

    /// <summary>
    /// Owner idea #85: an optional short line in the character's own voice
    /// for their speech/thought bubble. Presentation only — it never changes
    /// what the action is or what happens.
    /// </summary>
    public string? Say { get; init; }
}
