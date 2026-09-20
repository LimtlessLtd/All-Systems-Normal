using Overseer.Domain;

namespace Overseer.Simulation;

public sealed class ScenarioProgressSystem
{
    public void Tick(GameState state, TimeSpan delta)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running
            || state.Scenario is null)
        {
            return;
        }

        state.Telemetry.SimulatedMinutes += delta.TotalMinutes;
        if (state.LifeSupport.IsOnline)
        {
            state.Telemetry.LifeSupportOnlineMinutes += delta.TotalMinutes;
        }

        var livingCrew = state.Crew.Count(npc => npc.IsAlive && npc.IsPresent);
        var uptimePercent = state.Telemetry.SimulatedMinutes <= 0
            ? 100
            : (state.Telemetry.LifeSupportOnlineMinutes
                / state.Telemetry.SimulatedMinutes) * 100;

        foreach (var objective in state.Scenario.Objectives)
        {
            if (!state.ObjectiveProgress.TryGetValue(objective.Id, out var progress))
                continue;

            switch (objective.Kind)
            {
                case ScenarioObjectiveKind.SurviveMinutes:
                    progress.Current = Math.Min(
                        state.Elapsed.TotalMinutes,
                        objective.Target);
                    progress.IsComplete =
                        state.Elapsed.TotalMinutes >= objective.Target;
                    progress.StatusText =
                        $"{progress.Current:0}/{objective.Target:0} min";
                    break;

                case ScenarioObjectiveKind.KeepCrewAlive:
                    progress.Current = livingCrew;
                    progress.IsFailed = livingCrew < objective.Target;
                    progress.StatusText =
                        $"{livingCrew}/{objective.Target:0} crew alive";
                    break;

                case ScenarioObjectiveKind.LifeSupportUptimePercent:
                    progress.Current = uptimePercent;
                    progress.StatusText =
                        $"{uptimePercent:0.0}% uptime";
                    break;

                case ScenarioObjectiveKind.DirectivesSatisfied:
                {
                    var mandatory = state.Directives.Where(d => d.IsMandatory).ToList();
                    var satisfied = mandatory.Count(d =>
                        CorporateDirectiveSystem.Progress(state, d).Status
                            == DirectiveStatus.Completed);

                    progress.Current = satisfied;
                    progress.Target = mandatory.Count;
                    progress.IsComplete = mandatory.Count > 0 && satisfied == mandatory.Count;
                    progress.StatusText =
                        $"{satisfied}/{mandatory.Count} sponsor directives satisfied";
                    break;
                }
            }
        }

        var requiredComplete = state.Scenario.Objectives
            .Where(objective => !objective.IsOptional)
            .All(objective =>
                state.ObjectiveProgress.TryGetValue(objective.Id, out var progress)
                && progress.IsComplete);

        // The station's own objectives are only half the picture. A scenario is
        // not won while the corporate sponsor is still grading its directives,
        // and never won once a mandatory one has failed.
        if (requiredComplete && CorporateDirectiveSystem.MandatoryDirectivesSatisfied(state))
        {
            CompleteScenario(state, livingCrew, uptimePercent);
        }

        UpdateScore(state, livingCrew, uptimePercent);
    }

    private static void CompleteScenario(
        GameState state,
        int livingCrew,
        double uptimePercent)
    {
        if (state.Scenario is null)
            return;

        foreach (var objective in state.Scenario.Objectives.Where(o => o.IsOptional))
        {
            if (!state.ObjectiveProgress.TryGetValue(objective.Id, out var progress))
                continue;

            switch (objective.Kind)
            {
                case ScenarioObjectiveKind.KeepCrewAlive:
                    progress.IsComplete = livingCrew >= objective.Target;
                    progress.IsFailed = !progress.IsComplete;
                    break;

                case ScenarioObjectiveKind.LifeSupportUptimePercent:
                    progress.IsComplete = uptimePercent >= objective.Target;
                    progress.IsFailed = !progress.IsComplete;
                    break;
            }
        }

        state.ScenarioStatus = ScenarioStatus.Won;
        var optionalComplete = state.Scenario.Objectives
            .Where(o => o.IsOptional)
            .Count(o =>
                state.ObjectiveProgress.TryGetValue(o.Id, out var progress)
                && progress.IsComplete);

        state.ScenarioOutcome =
            $"Primary directive complete. {optionalComplete}/"
            + $"{state.Scenario.Objectives.Count(o => o.IsOptional)} optional objectives achieved.";

        AudioCueSystem.Emit(state, AudioCueKind.Important);
        state.EventLog.Insert(
            0,
            $"T+{state.Elapsed:hh\\:mm}: SCENARIO COMPLETE — {state.ScenarioOutcome}");
    }

    private static void UpdateScore(
        GameState state,
        int livingCrew,
        double uptimePercent)
    {
        var objectives = state.Scenario?.Objectives ?? [];
        var primaryObjectives = objectives
            .Where(objective => !objective.IsOptional)
            .ToList();

        var primaryProgress = primaryObjectives.Count == 0
            ? 0
            : primaryObjectives.Average(objective =>
            {
                if (!state.ObjectiveProgress.TryGetValue(objective.Id, out var progress)
                    || objective.Target <= 0)
                {
                    return 0d;
                }

                return Math.Clamp(progress.Current / objective.Target, 0, 1);
            });

        var completedOptional = objectives
            .Where(objective => objective.IsOptional)
            .Count(objective =>
                state.ObjectiveProgress.TryGetValue(objective.Id, out var progress)
                && progress.IsComplete);

        var deaths = state.Crew.Count(npc => !npc.IsAlive || !npc.IsPresent);
        var score =
            (int)Math.Round(primaryProgress * 500)
            + (livingCrew * 50)
            + (int)Math.Round(Math.Clamp(uptimePercent, 0, 100) * 2)
            + (completedOptional * 200)
            - (deaths * 150)
            - (state.Telemetry.RestrictiveDoorCommands * 4)
            - (state.Telemetry.AirlockSafetyBypasses * 35);

        if (state.ScenarioStatus == ScenarioStatus.Won)
        {
            score += 500;
        }

        state.Telemetry.Score = Math.Max(0, score);
    }
}
