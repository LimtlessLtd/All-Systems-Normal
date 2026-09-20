namespace Overseer.Domain;

public enum OverseerMessageScope
{
    /// <summary>A private channel to one crew member.</summary>
    Direct,

    /// <summary>Station-wide announcement. Everyone hears it, and everyone can compare notes.</summary>
    Broadcast
}

/// <summary>
/// The falsifiable content of something Overseer said. Free player text is
/// interpreted down to one of these so the simulation can decide whether the
/// statement was true when it was made, and whether anybody can later find out.
/// </summary>
public enum OverseerClaimKind
{
    /// <summary>Social noise. Influences mood, asserts nothing checkable.</summary>
    None,

    /// <summary>"You are safe / the station is fine." False if the subject room is dangerous.</summary>
    Reassurance,

    /// <summary>"A named crew member caused this." False if Overseer caused it.</summary>
    BlameCrew,

    /// <summary>"This compartment's systems are in state X." Checkable on arrival.</summary>
    SystemStatus,

    /// <summary>"There is a hazard in X." False if the compartment is fine.</summary>
    Warning,

    /// <summary>"Go to X / do Y." Not truth-apt, but it pushes on intent.</summary>
    Instruction
}

/// <summary>
/// A message the player sent, as delivered. Kept for the comms log and for the
/// campaign record.
/// </summary>
public sealed record OverseerMessage(
    long Sequence,
    OverseerMessageScope Scope,
    string? TargetNpcName,
    string Text,
    TimeSpan SentAt,
    OverseerClaimKind Claim,
    string? SubjectNpcName,
    string? SubjectRoomId,
    bool WasFalseWhenSent,
    string InterpretationSource);

/// <summary>
/// A claim Overseer made to one specific crew member, held until they are in a
/// position to find out whether it was true.
///
/// Truthfulness is decided when the message is sent, by comparing the claim
/// against actual world state. Discovery happens later, when the NPC can see
/// the subject for themselves. The player therefore commits to a lie at the
/// moment of sending and cannot retroactively make it true.
/// </summary>
public sealed class OverseerClaimRecord
{
    public required long MessageSequence { get; init; }
    public required OverseerClaimKind Kind { get; init; }
    public required string Statement { get; init; }
    public required TimeSpan MadeAt { get; init; }
    public required OverseerMessageScope Scope { get; init; }

    public string? SubjectRoomId { get; init; }
    public string? SubjectNpcName { get; init; }

    /// <summary>Whether the claim was false at the moment it was transmitted.</summary>
    public required bool WasFalseWhenMade { get; init; }

    /// <summary>
    /// Suspicion cost before the reach multiplier, representing how big a thing
    /// this was to be wrong about.
    /// </summary>
    public required double Magnitude { get; init; }

    /// <summary>Set once the crew member has checked and settled the matter.</summary>
    public bool Resolved { get; set; }
}

public static class OverseerCommsRules
{
    /// <summary>
    /// A lie told to the whole station is far more dangerous than one whispered
    /// to a single person: six people can compare accounts, and any one of them
    /// can walk into the room that disproves it.
    /// </summary>
    public static double ScopeMultiplier(OverseerMessageScope scope) => scope switch
    {
        OverseerMessageScope.Broadcast => 2.5,
        _ => 1.0
    };

    /// <summary>How much being caught out on this kind of claim hurts.</summary>
    public static double Magnitude(OverseerClaimKind kind) => kind switch
    {
        // Telling somebody they are safe while their compartment kills them, or
        // pinning your own sabotage on an innocent colleague, are the two ways
        // to be caught that a crew never forgives.
        OverseerClaimKind.Reassurance => 18,
        OverseerClaimKind.BlameCrew => 18,
        OverseerClaimKind.SystemStatus => 8,
        OverseerClaimKind.Warning => 8,
        _ => 0
    };

    /// <summary>Suspicion inflicted when a crew member catches a specific lie.</summary>
    public static double CaughtLieCost(OverseerClaimRecord claim) =>
        Magnitude(claim.Kind) * ScopeMultiplier(claim.Scope);

    /// <summary>
    /// How much weight a crew member gives Overseer's word, from their
    /// credibility and their current suspicion.
    /// </summary>
    public static double Persuasiveness(double credibility, double suspicion) =>
        Math.Clamp((credibility / 100d) * (1 - (suspicion / 140d)), 0, 1);
}
