using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class RemoteFireResponderTests
{
    [Fact]
    public async Task RuleBasedFallback_RespondsToReachableRemoteFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, responder);

        var intent = await new RuleBasedAiDecisionService().DecideAsync(responder, state);

        Assert.Equal(ActionKind.FightFire, intent.Action);
        Assert.Equal("engineering", intent.TargetId);
        Assert.True(intent.Urgency >= 90);
    }

    [Fact]
    public void BrowserFallback_RespondsToReachableRemoteFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        state.Elapsed = TimeSpan.FromMinutes(6);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, responder);

        foreach (var other in state.Crew.Where(npc => npc.Id != responder.Id))
        {
            CrewTaskSystem.Start(
                state,
                other,
                ActionKind.Work,
                other.CurrentRoomId,
                "Protected test work.",
                TimeSpan.FromHours(1));
        }

        new BrowserMindSystem().Tick(state);

        Assert.NotNull(responder.Intent);
        Assert.Equal(ActionKind.FightFire, responder.Intent.Action);
        Assert.Equal("engineering", responder.Intent.TargetId);
    }

    [Fact]
    public void RemoteFireSelector_DoesNotRecruitSecondResponderForSameFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var first = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        var second = state.Crew.First(npc => npc.Id != first.Id);
        PrepareSafeRemoteFireScenario(state, first);
        PrepareSafeRemoteFireScenario(state, second);

        first.Intent = new NpcIntent(
            ActionKind.FightFire,
            "engineering",
            "Respond to the fire.",
            "Already committed.",
            94,
            "Test",
            state.Elapsed);

        var target = StationHazardSystem.FindRemoteFireForResponder(
            state,
            second,
            new NavigationSystem());

        Assert.Null(target);
    }

    [Fact]
    public void RemoteFireSelector_DoesNotSendCrewIntoUnsafelyIntenseFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, responder);
        state.Facility.Rooms["engineering"].FireIntensity = 80;

        var target = StationHazardSystem.FindRemoteFireForResponder(
            state,
            responder,
            new NavigationSystem());

        Assert.Null(target);
    }

    private static void PrepareSafeRemoteFireScenario(GameState state, Npc responder)
    {
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = "control";
            npc.Intent = null;
            npc.ActiveTask = null;
            npc.NeedsMindReconsideration = false;
        }

        responder.Hunger = 0;
        responder.Fatigue = 0;
        responder.Stress = 0;
        responder.Skills["Engineering"] = 85;
        responder.Skills["Security"] = Math.Max(
            responder.Skills.GetValueOrDefault("Security"),
            45);

        var current = state.Facility.Rooms["control"];
        current.FireIntensity = 0;
        current.SmokePercent = 0;
        current.OxygenPercent = 21;
        current.PressureKpa = 101;
        current.TemperatureC = 21;

        var fireRoom = state.Facility.Rooms["engineering"];
        fireRoom.FireIntensity = 40;
        fireRoom.OxygenPercent = 21;
        fireRoom.PressureKpa = 101;
        fireRoom.SmokePercent = 8;
    }
}
