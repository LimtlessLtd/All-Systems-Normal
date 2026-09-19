using Overseer.Domain;

namespace Overseer.Simulation;

public static class AudioCueSystem
{
    private const int MaxBufferedCues = 96;

    public static void Emit(
        GameState state,
        AudioCueKind kind,
        string? sourceId = null,
        string? roomId = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.AudioCues.Add(new AudioCue(
            state.NextAudioCueSequence++,
            kind,
            state.Elapsed,
            sourceId,
            roomId));

        if (state.AudioCues.Count > MaxBufferedCues)
        {
            state.AudioCues.RemoveRange(
                0,
                state.AudioCues.Count - MaxBufferedCues);
        }
    }
}

/// <summary>
/// Presents already-decided NPC dialogue at readable intervals.
/// It does not decide what anyone wants or mutate physical world state.
/// </summary>
public sealed class ConversationPacingSystem
{
    public void Tick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive))
        {
            if (npc.Bubble is { } active
                && active.CreatedAt <= state.Elapsed
                && active.ExpiresAt > state.Elapsed)
            {
                continue;
            }

            var next = npc.PendingBubbles
                .Where(item => item.StartsAt <= state.Elapsed)
                .OrderBy(item => item.StartsAt)
                .FirstOrDefault();

            if (next is null)
            {
                continue;
            }

            npc.PendingBubbles.Remove(next);
            npc.Bubble = new NpcBubble(
                next.Text,
                next.Kind,
                state.Elapsed,
                state.Elapsed + next.Duration);

            AudioCueSystem.Emit(
                state,
                next.Kind switch
                {
                    NpcBubbleKind.Speech => AudioCueKind.Speech,
                    NpcBubbleKind.Alert => AudioCueKind.Warning,
                    _ => AudioCueKind.Thought
                },
                npc.Id.ToString(),
                npc.CurrentRoomId);
        }
    }

    public static void Schedule(
        Npc npc,
        string text,
        NpcBubbleKind kind,
        TimeSpan startsAt,
        int durationMinutes)
    {
        ArgumentNullException.ThrowIfNull(npc);

        npc.PendingBubbles.Add(new ScheduledNpcBubble(
            text,
            kind,
            startsAt,
            TimeSpan.FromMinutes(Math.Max(1, durationMinutes))));
    }
}
