namespace Overseer.Domain;

/// <summary>
/// What a corporate directive actually measures. Every kind must be evaluable
/// from deterministic simulation state: the corporation collects data, it does
/// not read the player's intentions.
/// </summary>
public enum DirectiveKind
{
    /// <summary>Overseer must still be in control when the window closes.</summary>
    MaintainContinuity,

    /// <summary>Drive a number of crew to a sustained stress threshold.</summary>
    BehaviouralStressResponse,

    /// <summary>Keep one named subject physically alone.</summary>
    IsolationTolerance,

    /// <summary>Keep a compartment unpowered or sealed off.</summary>
    ResourceDenial,

    /// <summary>Crew must respond to a live hazard within the response window.</summary>
    EmergencyCompliance,

    /// <summary>No crew member may suspect Overseer above the stated threshold.</summary>
    MaintainDeniability,

    /// <summary>Drive resentment between two named subjects past a threshold.</summary>
    SocialFracture,

    /// <summary>Every transferred prisoner must stay alive and physically contained.</summary>
    ContainmentIntegrity
}

public enum DirectiveStatus
{
    Active,
    Completed,
    Failed
}

/// <summary>
/// How much of the directive's real intent the corporation is willing to state.
/// Later campaign stages escalate this; the reveal is that the redactions were
/// hiding an experiment, not a safety procedure.
/// </summary>
public enum DirectiveClassification
{
    Routine,
    Restricted,
    Redacted
}

/// <summary>
/// An immutable objective handed to Overseer by the corporate sponsor.
/// <see cref="PublicJustification"/> is the sanitised reason shown to the
/// player from the start. <see cref="TruePurpose"/> is the experiment's actual
/// intent and stays hidden until the campaign reveals it.
/// </summary>
public sealed record CorporateDirective
{
    public required string Id { get; init; }

    /// <summary>Cohort/experiment reference, e.g. "HX-2291/C".</summary>
    public required string ExperimentCode { get; init; }

    public required DirectiveKind Kind { get; init; }
    public required string Title { get; init; }
    public required string PublicJustification { get; init; }
    public string? TruePurpose { get; init; }

    public DirectiveClassification Classification { get; init; } =
        DirectiveClassification.Routine;

    /// <summary>
    /// Mandatory directives decide the scenario outcome. Optional ones only
    /// move the compliance score.
    /// </summary>
    public bool IsMandatory { get; init; } = true;

    /// <summary>Room ID or crew name, depending on <see cref="Kind"/>.</summary>
    public string? TargetId { get; init; }

    /// <summary>Second crew name for relational directives.</summary>
    public string? SecondaryTargetId { get; init; }

    /// <summary>Stress / suspicion / resentment threshold, by kind.</summary>
    public double Threshold { get; init; }

    /// <summary>How many crew must satisfy the condition at once.</summary>
    public int RequiredCount { get; init; } = 1;

    /// <summary>Cumulative simulated minutes the condition must hold.</summary>
    public int RequiredMinutes { get; init; }

    /// <summary>When the directive is graded. Null means it resolves on its own terms.</summary>
    public TimeSpan? Deadline { get; init; }
}

/// <summary>
/// Mutable grading state for one directive. Kept separate from the definition so
/// scenario content stays immutable and replayable.
/// </summary>
public sealed class DirectiveProgress
{
    public required string DirectiveId { get; init; }
    public DirectiveStatus Status { get; set; } = DirectiveStatus.Active;

    /// <summary>Simulated minutes the directive's condition has held.</summary>
    public double AccumulatedMinutes { get; set; }

    /// <summary>0..1, for player-facing display only.</summary>
    public double Fraction { get; set; }

    /// <summary>One short line of player-facing telemetry.</summary>
    public string Detail { get; set; } = "No data collected yet.";

    public TimeSpan? ResolvedAt { get; set; }

    /// <summary>Set when a hazard window opened, for EmergencyCompliance.</summary>
    public TimeSpan? HazardStartedAt { get; set; }

    public bool IsResolved => Status != DirectiveStatus.Active;
}
