using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CognitionTelemetrySystemTests
{
    [Fact]
    public void BrowserMindDecision_IsRecordedForDiagnostics()
    {
        var state = FacilitySeeder.CreateDefault();
        state.Elapsed = TimeSpan.FromMinutes(6);

        new BrowserMindSystem().Tick(state);

        var trace = Assert.Single(state.CognitionTelemetry);
        Assert.Equal("Browser demo", trace.Source);
        Assert.NotNull(trace.Action);
        Assert.False(string.IsNullOrWhiteSpace(trace.Goal));
        Assert.False(string.IsNullOrWhiteSpace(trace.Reason));
    }

    [Fact]
    public void Recorder_IsBoundedAndTruncatesLargeModelPayloads()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var intent = new NpcIntent(
            ActionKind.Work,
            "control",
            "Do routine work.",
            "The station is calm.",
            20,
            "test",
            TimeSpan.Zero);

        for (var index = 0; index < 250; index++)
        {
            state.Elapsed = TimeSpan.FromMinutes(index);
            CognitionTelemetrySystem.Record(
                state,
                npc,
                "test",
                intent,
                new string('P', 70_000),
                new string('R', 40_000));
        }

        Assert.Equal(240, state.CognitionTelemetry.Count);
        Assert.Contains("[truncated]", state.CognitionTelemetry[0].Prompt);
        Assert.Contains("[truncated]", state.CognitionTelemetry[0].RawResponse);
        Assert.True(state.CognitionTelemetry[0].Sequence > state.CognitionTelemetry[^1].Sequence);
    }
}
