using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class PresentationCueSystemTests
{
    [Fact]
    public void AudioCueSystem_AssignsMonotonicSequencesAndBoundsHistory()
    {
        var state = FacilitySeeder.CreateDefault();

        for (var i = 0; i < 110; i++)
        {
            AudioCueSystem.Emit(state, AudioCueKind.System, sourceId: $"source-{i}");
        }

        Assert.Equal(96, state.AudioCues.Count);
        Assert.True(state.AudioCues.Zip(
            state.AudioCues.Skip(1),
            (first, second) => second.Sequence > first.Sequence).All(value => value));
        Assert.Equal(111, state.NextAudioCueSequence);
    }

    [Fact]
    public void CrossingSuspicionThreshold_EmitsSuspicionCue()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew.Single(crew => crew.Name == "Marcus Reed");

        SuspicionSystem.AddEvidence(
            state,
            npc,
            "I saw Overseer seal an emergency route.",
            30);

        Assert.Contains(
            state.AudioCues,
            cue => cue.Kind == AudioCueKind.Suspicion
                && cue.SourceId == npc.Id.ToString());
    }
}
