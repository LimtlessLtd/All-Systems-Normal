using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #27, slice 1: Overseer speaks through the intercom, so its
/// messages reach only crew standing in a powered room while the control
/// network is online.
/// </summary>
public sealed class CommsCoverageTests
{
    [Fact]
    public void ABroadcast_IsNotHeardInAnUnpoweredRoom()
    {
        var state = CreateStation();
        var dark = state.Crew[0];
        dark.CurrentRoomId = "medical";
        state.Facility.Rooms["medical"].IsPowered = false;
        var listeners = state.Crew.Skip(1).Where(npc => npc.IsAlive && npc.IsPresent).ToList();

        Broadcast(state, "Reactor inspection at noon.");

        Assert.Empty(dark.ReceivedMessages);
        Assert.DoesNotContain(dark.Memories, memory => memory.Description.Contains("Overseer announced"));
        Assert.False(dark.NeedsMindReconsideration);
        Assert.All(listeners, npc => Assert.Single(npc.ReceivedMessages));
        Assert.EndsWith("(1 crew out of intercom coverage)", state.EventLog[0]);
    }

    [Fact]
    public void WithTheControlNetworkDown_NobodyHearsOverseer()
    {
        var state = CreateStation();
        state.ControlNetworkOnline = false;
        var present = state.Crew.Count(npc => npc.IsAlive && npc.IsPresent);

        Broadcast(state, "Everyone to the galley.");
        var alarm = OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        Assert.NotNull(alarm);
        Assert.All(state.Crew, npc =>
        {
            Assert.Empty(npc.ReceivedMessages);
            Assert.Empty(npc.PendingOverseerClaims);
        });
        Assert.Contains(state.EventLog, line => line.EndsWith($"({present} crew out of intercom coverage)"));
        Assert.Equal(2, state.OverseerMessages.Count);
    }

    [Fact]
    public void APrivateMessage_ToSomeoneOutOfCoverage_IsNotDelivered()
    {
        var state = CreateStation();
        var target = state.Crew[0];
        target.CurrentRoomId = "medical";
        state.Facility.Rooms["medical"].IsPowered = false;

        OverseerCommsSystem.Send(
            state, OverseerMessageScope.Direct, target.Name, "Are you all right?",
            OverseerClaimKind.None, null, null, "Test");

        Assert.Empty(target.ReceivedMessages);
        Assert.EndsWith("(not delivered: no intercom coverage)", state.EventLog[0]);

        state.Facility.Rooms["medical"].IsPowered = true;
        OverseerCommsSystem.Send(
            state, OverseerMessageScope.Direct, target.Name, "Are you all right?",
            OverseerClaimKind.None, null, null, "Test");

        Assert.Single(target.ReceivedMessages);
        Assert.EndsWith("\"Are you all right?\"", state.EventLog[0]);
    }

    [Fact]
    public void Coverage_NeedsBothRoomPowerAndTheControlNetwork()
    {
        var state = CreateStation();
        var room = state.Facility.Rooms["medical"];

        Assert.True(CommsCoverageRules.HasIntercomCoverage(state, room));
        room.IsPowered = false;
        Assert.False(CommsCoverageRules.HasIntercomCoverage(state, room));
        room.IsPowered = true;
        state.ControlNetworkOnline = false;
        Assert.False(CommsCoverageRules.HasIntercomCoverage(state, room));
    }

    private static void Broadcast(GameState state, string text) =>
        OverseerCommsSystem.Send(
            state, OverseerMessageScope.Broadcast, null, text,
            OverseerClaimKind.None, null, null, "Test");

    private static GameState CreateStation()
    {
        var state = FacilitySeeder.CreateDefault();
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = "corridor";
            npc.NeedsMindReconsideration = false;
        }

        foreach (var room in state.Facility.Rooms.Values)
            room.IsPowered = true;

        state.ControlNetworkOnline = true;
        return state;
    }
}
