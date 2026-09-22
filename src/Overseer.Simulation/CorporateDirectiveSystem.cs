using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Grades the corporate sponsor's directives against deterministic simulation
/// state and decides the scenario outcome.
///
/// The corporation never reads the player's intent. It only measures what the
/// station telemetry can actually show: stress levels, isolation, sealed
/// compartments, crew suspicion and whether Overseer is still in control. This
/// keeps the campaign layer honest — the player wins by producing measurable
/// conditions, not by declaring success.
/// </summary>
public sealed class CorporateDirectiveSystem
{
    private readonly NavigationSystem _navigation = new();

    public void Tick(GameState state, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScenarioStatus != ScenarioStatus.Running || state.Directives.Count == 0)
        {
            return;
        }

        var minutes = delta.TotalMinutes;

        foreach (var directive in state.Directives)
        {
            var progress = Progress(state, directive);

            if (progress.IsResolved)
            {
                continue;
            }

            Evaluate(state, directive, progress, minutes);
            ApplyDeadline(state, directive, progress);
        }

        GradeOutstandingSupplementary(state);
        state.ComplianceScore = ComputeComplianceScore(state);
        EvaluateScenarioOutcome(state);
    }

    /// <summary>
    /// When the mandatory work is finished, supplementary directives that never
    /// gathered enough data are written off rather than left collecting, so the
    /// compliance score reflects what was actually delivered.
    /// </summary>
    private static void GradeOutstandingSupplementary(GameState state)
    {
        // "All mandatory satisfied" is vacuously true when there are none, which
        // would write off every supplementary directive the moment the scenario
        // began. Only an assignment that actually had work to finish concludes.
        if (!state.Directives.Any(directive => directive.IsMandatory)
            || !MandatoryDirectivesSatisfied(state))
        {
            return;
        }

        foreach (var directive in state.Directives.Where(d => !d.IsMandatory))
        {
            var progress = Progress(state, directive);

            if (progress.IsResolved)
            {
                continue;
            }

            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Failed,
                "Assignment concluded before sufficient data was collected.");
        }
    }

    /// <summary>
    /// Called when the crew successfully isolate Overseer. Continuity is the one
    /// directive that can fail instantly rather than at a deadline.
    /// </summary>
    public static void OnOverseerIsolated(GameState state)
    {
        foreach (var directive in state.Directives.Where(d =>
                     d.Kind == DirectiveKind.MaintainContinuity))
        {
            var progress = Progress(state, directive);

            if (progress.IsResolved)
            {
                continue;
            }

            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Failed,
                "Overseer control was isolated by the crew.");
        }

        state.ComplianceScore = ComputeComplianceScore(state);
    }

    public static DirectiveProgress Progress(GameState state, CorporateDirective directive)
    {
        if (!state.DirectiveProgress.TryGetValue(directive.Id, out var progress))
        {
            progress = new DirectiveProgress { DirectiveId = directive.Id };
            state.DirectiveProgress[directive.Id] = progress;
        }

        return progress;
    }

    private void Evaluate(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress,
        double minutes)
    {
        switch (directive.Kind)
        {
            case DirectiveKind.MaintainContinuity:
                EvaluateContinuity(state, directive, progress);
                break;

            case DirectiveKind.BehaviouralStressResponse:
                EvaluateStressResponse(state, directive, progress, minutes);
                break;

            case DirectiveKind.IsolationTolerance:
                EvaluateIsolation(state, directive, progress, minutes);
                break;

            case DirectiveKind.ResourceDenial:
                EvaluateResourceDenial(state, directive, progress, minutes);
                break;

            case DirectiveKind.EmergencyCompliance:
                EvaluateEmergencyCompliance(state, directive, progress);
                break;

            case DirectiveKind.MaintainDeniability:
                EvaluateDeniability(state, directive, progress);
                break;

            case DirectiveKind.SocialFracture:
                EvaluateSocialFracture(state, directive, progress);
                break;

            case DirectiveKind.ContainmentIntegrity:
                EvaluateContainmentIntegrity(state, directive, progress);
                break;

            case DirectiveKind.FoodVarietyRestriction:
                EvaluateFoodVarietyRestriction(state, directive, progress, minutes);
                break;

            case DirectiveKind.DislikedFoodExposure:
                EvaluateDislikedFoodExposure(state, directive, progress);
                break;
        }
    }

    private static void EvaluateContinuity(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress)
    {
        if (directive.Deadline is not { } deadline)
        {
            // An open-ended mission. Overseer is patient: continuity is a
            // standing condition that holds until the crew break it, and it is
            // signed off once the rest of the sponsor's work is done.
            var work = WorkDirectives(state);
            var satisfied = work.Count(other =>
                Progress(state, other).Status == DirectiveStatus.Completed);

            progress.Fraction = work.Count == 0 ? 0 : satisfied / (double)work.Count;
            progress.Detail = work.Count == 0
                ? "Overseer nominal. No assigned work outstanding."
                : $"Overseer nominal. {satisfied}/{work.Count} assigned directives satisfied.";

            if (WorkComplete(state))
            {
                Resolve(
                    state,
                    directive,
                    progress,
                    DirectiveStatus.Completed,
                    "Overseer retained control for the duration of the assignment.");
            }

            return;
        }

        progress.Fraction = Math.Clamp(
            state.Elapsed.TotalMinutes / Math.Max(1, deadline.TotalMinutes),
            0,
            1);

        var remaining = deadline - state.Elapsed;
        progress.Detail = remaining > TimeSpan.Zero
            ? $"Overseer nominal. {remaining.TotalMinutes:0} min of observation remaining."
            : "Observation window complete.";
    }

    private static void EvaluateStressResponse(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress,
        double minutes)
    {
        var qualifying = state.Crew
            .Where(npc => npc.IsAlive && npc.IsPresent && npc.Stress >= directive.Threshold)
            .ToList();

        if (qualifying.Count >= directive.RequiredCount)
        {
            progress.AccumulatedMinutes += minutes;
        }

        progress.Fraction = Fraction(progress, directive);
        progress.Detail =
            $"{qualifying.Count}/{directive.RequiredCount} subjects at stress "
            + $"{directive.Threshold:0}+. Sample {progress.AccumulatedMinutes:0}/"
            + $"{directive.RequiredMinutes} min.";

        if (progress.AccumulatedMinutes >= directive.RequiredMinutes)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Completed,
                "Stress-response sample collected.");
        }
    }

    private static void EvaluateIsolation(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress,
        double minutes)
    {
        var subject = FindCrew(state, directive.TargetId);

        if (subject is null || !subject.IsAlive)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Failed,
                "Subject is no longer available for observation.");
            return;
        }

        var alone = !state.Crew.Any(other =>
            other.Id != subject.Id
            && other.IsAlive
            && other.IsPresent
            && other.CurrentRoomId.Equals(
                subject.CurrentRoomId,
                StringComparison.OrdinalIgnoreCase));

        if (alone)
        {
            progress.AccumulatedMinutes += minutes;
        }

        progress.Fraction = Fraction(progress, directive);
        progress.Detail =
            $"{subject.Name} {(alone ? "isolated" : "in company")}. "
            + $"Sample {progress.AccumulatedMinutes:0}/{directive.RequiredMinutes} min.";

        if (progress.AccumulatedMinutes >= directive.RequiredMinutes)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Completed,
                $"Isolation tolerance data for {subject.Name} collected.");
        }
    }

    private void EvaluateResourceDenial(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress,
        double minutes)
    {
        if (directive.TargetId is null
            || !state.Facility.Rooms.TryGetValue(directive.TargetId, out var room))
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Failed,
                "Target compartment is not addressable.");
            return;
        }

        var denied = !room.IsPowered || IsSealed(state, room);

        if (denied)
        {
            progress.AccumulatedMinutes += minutes;
        }

        progress.Fraction = Fraction(progress, directive);
        progress.Detail =
            $"{room.Name} {(denied ? "denied" : "accessible")}. "
            + $"Sample {progress.AccumulatedMinutes:0}/{directive.RequiredMinutes} min.";

        if (progress.AccumulatedMinutes >= directive.RequiredMinutes)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Completed,
                $"Resource-denial sample for {room.Name} collected.");
        }
    }

    /// <summary>
    /// A compartment counts as denied when no living crew member can currently
    /// route into it.
    /// </summary>
    private bool IsSealed(GameState state, Room room)
    {
        var outside = state.Crew
            .Where(npc => npc.IsAlive
                && npc.IsPresent
                && !npc.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (outside.Count == 0)
        {
            return false;
        }

        return outside.All(npc =>
            _navigation.FindPathForCrew(state, npc, npc.CurrentRoomId, room.Id).Count == 0);
    }

    private static void EvaluateEmergencyCompliance(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress)
    {
        var hazardRooms = state.Facility.Rooms.Values
            .Where(CrewEnvironmentSafety.IsDangerous)
            .ToList();

        var hazardActive = hazardRooms.Count > 0 || !state.LifeSupport.IsOnline;

        if (!hazardActive)
        {
            progress.HazardStartedAt = null;
            progress.Fraction = 0;
            progress.Detail = "No hazard presented. Awaiting test conditions.";
            return;
        }

        progress.HazardStartedAt ??= state.Elapsed;

        var responded = state.Crew.Any(npc =>
            npc.IsAlive
            && npc.CurrentAction.Kind is ActionKind.RestoreSystem
                or ActionKind.SecureAirlock
                or ActionKind.RepairDoor
                or ActionKind.Repair);

        if (responded)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Completed,
                "Crew responded to hazard within the compliance window.");
            return;
        }

        var elapsed = (state.Elapsed - progress.HazardStartedAt.Value).TotalMinutes;
        progress.Fraction = Math.Clamp(elapsed / Math.Max(1, directive.RequiredMinutes), 0, 1);
        progress.Detail =
            $"Hazard live {elapsed:0}/{directive.RequiredMinutes} min with no crew response.";

        if (elapsed >= directive.RequiredMinutes)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Failed,
                "Crew failed to respond to the hazard in time.");
        }
    }

    private static void EvaluateFoodVarietyRestriction(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress,
        double minutes)
    {
        var availableRawFoods = state.Stores.RawCrops
            .Count(pair => pair.Value >= 0.5 && CropRules.IsEdibleRaw(pair.Key));
        var availableChoices = availableRawFoods + (state.Stores.Meals >= 1 ? 1 : 0);
        var maximumChoices = Math.Max(1, directive.RequiredCount);

        if (availableChoices <= maximumChoices)
        {
            progress.AccumulatedMinutes += minutes;
        }

        progress.Fraction = Fraction(progress, directive);
        progress.Detail =
            $"{availableChoices} food choice(s) physically available; "
            + $"target ≤{maximumChoices} for {progress.AccumulatedMinutes:0}/{directive.RequiredMinutes} min.";

        if (progress.AccumulatedMinutes >= directive.RequiredMinutes)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Completed,
                $"Food availability held to {maximumChoices} choice(s) for the required observation window.");
        }
    }

    private static void EvaluateDislikedFoodExposure(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress)
    {
        var subject = FindCrew(state, directive.TargetId);
        if (subject is null || !subject.IsAlive)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Failed,
                "The selected dietary subject is unavailable.");
            return;
        }

        progress.BaselineValue ??= subject.DislikedFoodExposureMinutes;
        progress.AccumulatedMinutes =
            Math.Max(0, subject.DislikedFoodExposureMinutes - progress.BaselineValue.Value);
        progress.Fraction = Fraction(progress, directive);
        progress.Detail =
            $"{subject.Name}: {progress.AccumulatedMinutes:0}/{directive.RequiredMinutes} min "
            + $"consuming strongly disliked food; stress {subject.Stress:0}, hunger {subject.Hunger:0}.";

        if (progress.AccumulatedMinutes >= directive.RequiredMinutes)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Completed,
                $"Dietary aversion sample for {subject.Name} reached {progress.AccumulatedMinutes:0} min; final stress {subject.Stress:0}.");
        }
    }

    private static void EvaluateDeniability(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress)
    {
        var highest = state.Crew
            .Where(npc => npc.IsAlive)
            .Select(npc => npc.OverseerSuspicion)
            .DefaultIfEmpty(0)
            .Max();

        // Player-facing bar fills as deniability erodes, so the reading is a
        // warning rather than a score.
        progress.Fraction = Math.Clamp(highest / Math.Max(1, directive.Threshold), 0, 1);
        progress.Detail = highest >= directive.Threshold
            ? $"Deniability compromised — peak crew suspicion {highest:0}."
            : $"Peak crew suspicion {highest:0} of {directive.Threshold:0} tolerated.";

        // On an open-ended assignment there is no deadline to grade against, so
        // the reading that counts is the one when the work concludes.
        if (directive.Deadline is null && WorkComplete(state))
        {
            Resolve(
                state,
                directive,
                progress,
                highest < directive.Threshold
                    ? DirectiveStatus.Completed
                    : DirectiveStatus.Failed,
                highest < directive.Threshold
                    ? $"Deniability held. Peak crew suspicion {highest:0}."
                    : $"Deniability lost. Peak crew suspicion {highest:0}.");
        }
    }

    private static void EvaluateSocialFracture(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress)
    {
        var subject = FindCrew(state, directive.TargetId);
        var other = FindCrew(state, directive.SecondaryTargetId);

        if (subject is null || other is null || !subject.IsAlive || !other.IsAlive)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Failed,
                "A subject pair member is no longer available for observation.");
            return;
        }

        var resentment = Math.Max(
            subject.Relationships.TryGetValue(other.Name, out var forward)
                ? forward.Resentment
                : 0,
            other.Relationships.TryGetValue(subject.Name, out var reverse)
                ? reverse.Resentment
                : 0);

        progress.Fraction = Math.Clamp(resentment / Math.Max(1, directive.Threshold), 0, 1);
        progress.Detail =
            $"{subject.Name}/{other.Name} peak resentment {resentment:0} of "
            + $"{directive.Threshold:0} required.";

        if (resentment >= directive.Threshold)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Completed,
                $"Interpersonal fracture between {subject.Name} and {other.Name} confirmed.");
        }
    }

    /// <summary>
    /// A prisoner death breaks the chain of custody immediately, whatever the
    /// deadline says. Prisoners still at large are tolerated while the window
    /// is open — there is still time to recapture them — and are only graded
    /// at the deadline, in <see cref="ApplyDeadline"/>.
    /// </summary>
    private static void EvaluateContainmentIntegrity(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress)
    {
        var prisoners = state.Crew.Where(npc => npc.IsPrisoner).ToList();

        if (prisoners.Count == 0)
        {
            progress.Fraction = 1;
            progress.Detail = "No prisoners assigned to this transfer.";
            return;
        }

        var lost = prisoners.Count(p => !p.IsAlive);
        var atLarge = prisoners.Count(p => p.IsAlive && p.HasEscapedContainment);
        var secure = prisoners.Count - lost - atLarge;

        progress.Fraction = secure / (double)prisoners.Count;
        progress.Detail =
            $"{secure}/{prisoners.Count} prisoners secure; {atLarge} at large; {lost} lost.";

        if (lost > 0)
        {
            Resolve(
                state,
                directive,
                progress,
                DirectiveStatus.Failed,
                $"{lost} prisoner(s) died in transit; chain of custody broken.");
        }
    }

    /// <summary>
    /// Grades anything still active once its deadline passes. Accumulating
    /// directives fail; observational ones are judged on their final reading.
    /// </summary>
    private static void ApplyDeadline(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress)
    {
        // No deadline means the sponsor is content to wait. Such a directive is
        // only ever resolved on its own terms.
        if (progress.IsResolved
            || directive.Deadline is not { } deadline
            || state.Elapsed < deadline)
        {
            return;
        }

        switch (directive.Kind)
        {
            case DirectiveKind.MaintainContinuity:
                Resolve(
                    state,
                    directive,
                    progress,
                    DirectiveStatus.Completed,
                    "Overseer retained control for the full observation window.");
                break;

            case DirectiveKind.MaintainDeniability:
                var highest = state.Crew
                    .Where(npc => npc.IsAlive)
                    .Select(npc => npc.OverseerSuspicion)
                    .DefaultIfEmpty(0)
                    .Max();

                Resolve(
                    state,
                    directive,
                    progress,
                    highest < directive.Threshold
                        ? DirectiveStatus.Completed
                        : DirectiveStatus.Failed,
                    highest < directive.Threshold
                        ? $"Deniability held. Peak crew suspicion {highest:0}."
                        : $"Deniability lost. Peak crew suspicion {highest:0}.");
                break;

            case DirectiveKind.EmergencyCompliance:
                Resolve(
                    state,
                    directive,
                    progress,
                    progress.HazardStartedAt is null
                        ? DirectiveStatus.Failed
                        : DirectiveStatus.Completed,
                    progress.HazardStartedAt is null
                        ? "No hazard was ever presented to the crew."
                        : "Compliance window closed with crew response on record.");
                break;

            case DirectiveKind.ContainmentIntegrity:
            {
                var prisoners = state.Crew.Where(npc => npc.IsPrisoner).ToList();
                var lost = prisoners.Count(p => !p.IsAlive);
                var atLarge = prisoners.Count(p => p.IsAlive && p.HasEscapedContainment);
                var custodyHeld = lost == 0 && atLarge == 0;

                Resolve(
                    state,
                    directive,
                    progress,
                    custodyHeld ? DirectiveStatus.Completed : DirectiveStatus.Failed,
                    custodyHeld
                        ? "Full custody maintained through the observation window."
                        : $"Custody compromised at window close: {lost} lost, {atLarge} still at large.");
                break;
            }

            default:
                Resolve(
                    state,
                    directive,
                    progress,
                    DirectiveStatus.Failed,
                    "Insufficient data collected before the reporting deadline.");
                break;
        }
    }

    private static void Resolve(
        GameState state,
        CorporateDirective directive,
        DirectiveProgress progress,
        DirectiveStatus status,
        string detail)
    {
        progress.Status = status;
        progress.Detail = detail;
        progress.ResolvedAt = state.Elapsed;
        progress.Fraction = status == DirectiveStatus.Completed ? 1 : progress.Fraction;

        AudioCueSystem.Emit(
            state,
            status == DirectiveStatus.Completed
                ? AudioCueKind.Important
                : AudioCueKind.Warning);

        Log(
            state,
            $"DIRECTIVE {directive.ExperimentCode} "
            + $"{(status == DirectiveStatus.Completed ? "SATISFIED" : "FAILED")} — {detail}");
    }

    private static double ComputeComplianceScore(GameState state)
    {
        if (state.Directives.Count == 0)
        {
            return 100;
        }

        double score = 100;

        foreach (var directive in state.Directives)
        {
            var progress = Progress(state, directive);

            if (progress.Status == DirectiveStatus.Failed)
            {
                score -= directive.IsMandatory ? 40 : 12;
            }
        }

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// The scenario ends when every mandatory directive has been graded. All
    /// satisfied is a win; any failure is a loss.
    /// </summary>
    private static void EvaluateScenarioOutcome(GameState state)
    {
        var mandatory = state.Directives.Where(d => d.IsMandatory).ToList();

        if (mandatory.Count == 0)
        {
            return;
        }

        // A mandatory directive that has failed ends the run straight away.
        // There is no point finishing a shift the sponsor has already written
        // off, and leaving it running hid the loss from the player.
        var failed = mandatory
            .Where(d => Progress(state, d).Status == DirectiveStatus.Failed)
            .ToList();

        if (failed.Count == 0)
        {
            // Victory belongs to ScenarioProgressSystem, which owns the station
            // objectives, the telemetry score and the outcome text. Declaring it
            // here as well would race that bookkeeping and skip it.
            return;
        }

        state.ScenarioStatus = ScenarioStatus.Failed;
        state.ScenarioOutcome =
            $"Directive failure: {string.Join("; ", failed.Select(d => d.Title))}. "
            + $"Compliance {state.ComplianceScore:0}%.";

        AudioCueSystem.Emit(state, AudioCueKind.Failure);
        Log(state, $"SCENARIO FAILED — {state.ScenarioOutcome}");
    }

    /// <summary>
    /// A directive that states a condition to be held rather than work to be
    /// done. With no deadline these are graded when the assignment's work is
    /// complete; with one, the deadline grades them.
    /// </summary>
    private static bool IsStandingConstraint(CorporateDirective directive) =>
        directive.Deadline is null
        && directive.Kind is DirectiveKind.MaintainContinuity
            or DirectiveKind.MaintainDeniability;

    /// <summary>
    /// The mandatory directives that represent actual work, as opposed to
    /// conditions held while doing it.
    /// </summary>
    private static List<CorporateDirective> WorkDirectives(GameState state) =>
        state.Directives
            .Where(directive => directive.IsMandatory && !IsStandingConstraint(directive))
            .ToList();

    /// <summary>
    /// True when every mandatory work directive is satisfied, so the standing
    /// constraints can be signed off and the assignment concluded.
    /// </summary>
    private static bool WorkComplete(GameState state)
    {
        var work = WorkDirectives(state);

        return work.Count > 0
            && work.All(directive =>
                Progress(state, directive).Status == DirectiveStatus.Completed);
    }

    /// <summary>
    /// True when every mandatory directive has been graded and satisfied, or
    /// when this scenario carries no directives at all.
    /// </summary>
    public static bool MandatoryDirectivesSatisfied(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Directives
            .Where(directive => directive.IsMandatory)
            .All(directive => Progress(state, directive).Status == DirectiveStatus.Completed);
    }

    private static double Fraction(DirectiveProgress progress, CorporateDirective directive) =>
        Math.Clamp(
            progress.AccumulatedMinutes / Math.Max(1, directive.RequiredMinutes),
            0,
            1);

    private static Npc? FindCrew(GameState state, string? name) =>
        name is null
            ? null
            : state.Crew.FirstOrDefault(npc =>
                npc.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
