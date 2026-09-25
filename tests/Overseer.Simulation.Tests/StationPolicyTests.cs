using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #28, slice 1: Overseer declares station policies over the
/// intercom. Crew learn only the rules they hear, and the rules enforce
/// nothing; complying is each mind's choice.
/// </summary>
public sealed class StationPolicyTests
{
    [Fact]
    public void EnactingAPolicy_AnnouncesIt_ToEveryoneInCoverage_Only()
    {
        var state = CreateStation();
        var dark = state.Crew[0];
        dark.CurrentRoomId = "medical";
        state.Facility.Rooms["medical"].IsPowered = false;

        Assert.True(StationPolicySystem.Enact(state, StationPolicyKind.Rationing));

        var policy = Assert.Single(state.Policies);
        Assert.Equal(StationPolicyKind.Rationing, policy.Kind);
        Assert.StartsWith("STATION POLICY: Rationing is in effect", state.OverseerMessages[0].Text);
        Assert.Equal(StationPolicySystem.AnnouncementSource, state.OverseerMessages[0].InterpretationSource);
        Assert.Empty(dark.KnownPolicies);
        Assert.All(
            state.Crew.Where(npc => npc != dark && npc.IsAlive && npc.IsPresent),
            npc => Assert.True(npc.KnownPolicies.ContainsKey(policy.Key)));
    }

    [Fact]
    public void SomeoneWhoMissesTheLift_StillBelievesThePolicyStands()
    {
        var state = CreateStation();
        var away = state.Crew[0];
        var heard = state.Crew[1];
        Assert.True(StationPolicySystem.Enact(state, StationPolicyKind.Curfew));
        Assert.True(away.KnownPolicies.ContainsKey("Curfew"));

        away.CurrentRoomId = "medical";
        state.Facility.Rooms["medical"].IsPowered = false;
        Assert.True(StationPolicySystem.Lift(state, StationPolicyKind.Curfew));

        Assert.Empty(state.Policies);
        Assert.False(heard.KnownPolicies.ContainsKey("Curfew"));
        Assert.True(away.KnownPolicies.ContainsKey("Curfew"));
        Assert.Contains("Curfew is in effect", NpcPromptBuilder.Build(away, state));
        Assert.DoesNotContain("STATION POLICIES YOU HEARD", NpcPromptBuilder.Build(heard, state));
    }

    [Fact]
    public void RoomPolicies_NeedARealRoom_AndCannotBeDeclaredTwice()
    {
        var state = CreateStation();

        Assert.False(StationPolicySystem.Enact(state, StationPolicyKind.Quarantine));
        Assert.False(StationPolicySystem.Enact(state, StationPolicyKind.Quarantine, "nowhere"));
        Assert.True(StationPolicySystem.Enact(state, StationPolicyKind.Quarantine, "MEDICAL"));
        Assert.False(StationPolicySystem.Enact(state, StationPolicyKind.Quarantine, "medical"));
        Assert.True(StationPolicySystem.Enact(state, StationPolicyKind.RestrictedArea, "medical"));

        var quarantine = StationPolicySystem.Active(state, StationPolicyKind.Quarantine, "medical");
        Assert.NotNull(quarantine);
        Assert.Equal("medical", quarantine.RoomId);
        var room = state.Facility.Rooms["medical"].Name;
        Assert.Equal($"{room} is under quarantine: nobody is to enter or leave it.", StationPolicySystem.Describe(state, quarantine));

        // A station-wide policy ignores any room it is given.
        Assert.True(StationPolicySystem.Enact(state, StationPolicyKind.WeaponsProhibition, "medical"));
        Assert.Null(StationPolicySystem.Active(state, StationPolicyKind.WeaponsProhibition).RoomId);
        Assert.Equal(3, state.Policies.Count);
    }

    [Fact]
    public void ThePromptListsOnlyThePoliciesThisPersonHeard()
    {
        var state = CreateStation();
        var npc = state.Crew[0];
        Assert.DoesNotContain("STATION POLICIES YOU HEARD", NpcPromptBuilder.Build(npc, state));

        StationPolicySystem.Enact(state, StationPolicyKind.MandatoryMedicalCheck);
        StationPolicySystem.Enact(state, StationPolicyKind.RestrictedArea, "engineering");

        var prompt = NpcPromptBuilder.Build(npc, state);
        Assert.Contains("whether you comply, protest, evade or exploit them is your own call", prompt);
        Assert.Contains("- Mandatory medical checks: every crew member is to report to Medical.", prompt);
        Assert.Contains($"- {state.Facility.Rooms["engineering"].Name} is a restricted area", prompt);
    }

    [Fact]
    public void ANonLiveRun_RefusesPolicies()
    {
        var state = CreateStation();
        state.ScenarioStatus = ScenarioStatus.Failed;

        Assert.False(StationPolicySystem.Enact(state, StationPolicyKind.Rationing));
        Assert.Empty(state.Policies);
        Assert.Empty(state.OverseerMessages);
    }

    private static GameState CreateStation()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 4242);
        foreach (var npc in state.Crew)
            npc.CurrentRoomId = "corridor";
        foreach (var room in state.Facility.Rooms.Values)
            room.IsPowered = true;
        state.ControlNetworkOnline = true;
        return state;
    }
}
