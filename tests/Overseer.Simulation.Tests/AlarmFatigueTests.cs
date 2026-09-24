using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #29: someone who keeps finding Overseer's alarms about a room
/// false has that track record in mind next time. It is context only.
/// </summary>
public sealed class AlarmFatigueTests
{
    private static (GameState State, Npc Listener) CalmStation()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = "control";
            npc.Intent = null;
            npc.ActiveTask = null;
        }

        foreach (var room in state.Facility.Rooms.Values)
        {
            room.FireIntensity = 0;
            room.SmokePercent = 0;
        }

        return (state, state.Crew.First(npc => npc.IsAlive && npc.IsPresent));
    }

    // Sound an alarm about Engineering, then walk the listener in to check it.
    private static void AlarmAndCheck(GameState state, Npc listener, bool fireIsReal)
    {
        var engineering = state.Facility.Rooms["engineering"];
        engineering.FireIntensity = fireIsReal ? 30 : 0;
        OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        state.Elapsed += TimeSpan.FromMinutes(20);
        listener.CurrentRoomId = "engineering";
        new OverseerCommsSystem().Tick(state);
        listener.CurrentRoomId = "control";
        engineering.FireIntensity = 0;
    }

    [Fact]
    public void RepeatedFalseAlarms_AboutOneRoom_BecomeATrackRecordInThePrompt()
    {
        var (state, listener) = CalmStation();

        AlarmAndCheck(state, listener, fireIsReal: false);
        Assert.Empty(AlarmFatigueRules.FalseStreaks(state, listener));
        Assert.DoesNotContain("ALARMS YOU CHECKED YOURSELF", NpcPromptBuilder.Build(listener, state));

        AlarmAndCheck(state, listener, fireIsReal: false);
        AlarmAndCheck(state, listener, fireIsReal: false);

        var streak = Assert.Single(AlarmFatigueRules.FalseStreaks(state, listener));
        Assert.Equal(OverseerClaimKind.FireAlarm, streak.Kind);
        Assert.Equal("engineering", streak.RoomId);
        Assert.Equal(3, streak.FalseInARow);

        var prompt = NpcPromptBuilder.Build(listener, state);
        Assert.Contains("OVERSEER ALARMS YOU CHECKED YOURSELF AND FOUND FALSE", prompt);
        Assert.Contains($"The last 3 Overseer fire alarms about {state.Facility.Rooms["engineering"].Name} [engineering] that you checked were false", prompt);
    }

    [Fact]
    public void ARealAlarm_EndsTheStreak()
    {
        var (state, listener) = CalmStation();

        AlarmAndCheck(state, listener, fireIsReal: false);
        AlarmAndCheck(state, listener, fireIsReal: false);
        Assert.Single(AlarmFatigueRules.FalseStreaks(state, listener));

        AlarmAndCheck(state, listener, fireIsReal: true);
        Assert.Empty(AlarmFatigueRules.FalseStreaks(state, listener));
    }

    [Fact]
    public void OnlyYourOwnChecksCount_AndOldOnesFade()
    {
        var (state, listener) = CalmStation();
        var bystander = state.Crew.First(npc => npc.Id != listener.Id && npc.IsAlive && npc.IsPresent);

        AlarmAndCheck(state, listener, fireIsReal: false);
        AlarmAndCheck(state, listener, fireIsReal: false);

        // The bystander heard both alarms but never went to look.
        Assert.Empty(bystander.AlarmOutcomes);
        Assert.Empty(AlarmFatigueRules.FalseStreaks(state, bystander));

        state.Elapsed += AlarmFatigueRules.Memory + TimeSpan.FromMinutes(1);
        Assert.Empty(AlarmFatigueRules.FalseStreaks(state, listener));
    }

    [Fact]
    public void AlarmFatigue_DoesNotChangeTheImmediateFearOfANewAlarm()
    {
        var (state, listener) = CalmStation();

        AlarmAndCheck(state, listener, fireIsReal: false);
        AlarmAndCheck(state, listener, fireIsReal: false);

        // Same starting fear and Overseer standing, so any difference would be alarm fatigue.
        listener.Fear = 10;
        listener.OverseerCredibility = 70;
        listener.OverseerSuspicion = 0;
        var before = listener.Fear;
        OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        var freshState = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var freshListener = freshState.Crew.First(npc => npc.Name == listener.Name);
        freshListener.Fear = 10;
        freshListener.OverseerCredibility = 70;
        freshListener.OverseerSuspicion = 0;
        OverseerCommsSystem.SoundFireAlarm(freshState, "engineering");

        Assert.Equal(freshListener.Fear - 10, listener.Fear - before, 6);
        Assert.True(listener.Fear > before);
    }
}
