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
        Assert.Equal(ActionKind.FightFire, responder.Intent!.Action);
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

    [Fact]
    public void ShouldFightFire_DoesNotTreatOxygenStarvedCompartmentAsSurvivable()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        var room = state.Facility.Rooms["engineering"];
        responder.Skills["Engineering"] = 100;
        room.FireIntensity = 30;
        room.OxygenPercent = 8;
        room.PressureKpa = 101;
        room.SmokePercent = 5;

        Assert.False(StationHazardSystem.ShouldFightFire(responder, room));
    }

    [Fact]
    public void RemoteFire_WakesOneAvailableResponderBeforeTheNormalBrowserMindCadence()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        state.Elapsed = TimeSpan.FromMinutes(1);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, responder);

        // Protect every other crew member with committed work so the hazard
        // event has exactly one eligible responder to wake.
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

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        var awakened = Assert.Single(state.Crew, npc => npc.NeedsMindReconsideration);

        // Minute 1 is deliberately outside BrowserMindSystem's ordinary
        // six-minute rotation. Event reconsideration must still run now.
        new BrowserMindSystem().Tick(state);

        Assert.NotNull(awakened.Intent);
        Assert.Equal(ActionKind.FightFire, awakened.Intent!.Action);
        Assert.Equal("engineering", awakened.Intent.TargetId);
    }

    [Fact]
    public void RemoteFire_WakesCrewWhoseCriticalNeedIntentWouldOtherwiseMaskFirePriority()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        state.Elapsed = TimeSpan.FromMinutes(1);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, responder);

        // #156 deliberately made a viable remote fire outrank critical hunger
        // in both fallback minds. The hazard wake filter still excluded any
        // existing urgency >=85 intent, so the mind never got a chance to apply
        // that ordering once an Eat intent already existed.
        responder.Hunger = CrewNeedThresholds.HungerCritical;
        responder.Intent = new NpcIntent(
            ActionKind.Eat,
            null,
            "Find food now.",
            "I am critically hungry.",
            92,
            "Test",
            state.Elapsed);

        // Make every other person physically incapable of safe firefighting so
        // the nomination is deterministic and specifically exercises the
        // critical-need responder.
        foreach (var other in state.Crew.Where(npc => npc.Id != responder.Id))
        {
            other.Health = 30;
        }

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(responder.NeedsMindReconsideration);
        Assert.Equal(ActionKind.Eat, responder.Intent?.Action);

        new BrowserMindSystem().Tick(state);

        Assert.Equal(ActionKind.FightFire, responder.Intent?.Action);
        Assert.Equal("engineering", responder.Intent?.TargetId);
        Assert.False(responder.NeedsMindReconsideration);
    }

    [Fact]
    public void RemoteFire_DoesNotRewakeCriticalNeedResponderAlreadyTravellingToFightIt()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        state.Elapsed = TimeSpan.FromMinutes(1);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, responder);
        responder.Hunger = CrewNeedThresholds.HungerCritical;
        responder.Intent = new NpcIntent(
            ActionKind.Eat,
            null,
            "Find food now.",
            "I am critically hungry.",
            92,
            "Test",
            state.Elapsed);

        foreach (var other in state.Crew.Where(npc => npc.Id != responder.Id))
        {
            other.Health = 30;
        }

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));
        new BrowserMindSystem().Tick(state);
        var chosenFireIntent = responder.Intent;

        Assert.Equal(ActionKind.FightFire, chosenFireIntent?.Action);
        Assert.False(responder.NeedsMindReconsideration);

        state.Elapsed += TimeSpan.FromMinutes(1);
        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.False(responder.NeedsMindReconsideration);
        Assert.Same(chosenFireIntent, responder.Intent);
    }

    [Fact]
    public void RemoteFire_PrefersIdleCapableResponderOverMoreSkilledCommittedWorker()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        state.Elapsed = TimeSpan.FromMinutes(1);
        var committedExpert = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, committedExpert);

        var idleResponder = state.Crew.First(npc => npc.Id != committedExpert.Id);
        idleResponder.Skills["Engineering"] = 45;
        idleResponder.Skills["Security"] = 45;

        committedExpert.Skills["Engineering"] = 100;

        // Keep the comparison focused: exactly one idle capable responder and
        // one more-skilled committed responder are eligible for this fire.
        foreach (var worker in state.Crew.Where(npc => npc.Id != idleResponder.Id))
        {
            CrewTaskSystem.Start(
                state,
                worker,
                ActionKind.Work,
                worker.CurrentRoomId,
                "Important routine work.",
                TimeSpan.FromHours(1));
        }

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        var awakened = Assert.Single(state.Crew, npc => npc.NeedsMindReconsideration);
        Assert.Equal(idleResponder.Id, awakened.Id);
        Assert.Equal(CrewTaskStatus.InProgress, committedExpert.ActiveTask?.Status);
    }

    [Fact]
    public void RemoteFire_CanInterruptMundaneCommittedWorkAfterCognitionChoosesResponse()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        state.Elapsed = TimeSpan.FromMinutes(1);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, responder);

        foreach (var npc in state.Crew)
        {
            CrewTaskSystem.Start(
                state,
                npc,
                ActionKind.Work,
                npc.CurrentRoomId,
                "Committed routine work.",
                TimeSpan.FromHours(1));
        }

        new StationHazardSystem().Tick(state, TimeSpan.FromMinutes(1));

        var awakened = Assert.Single(state.Crew, npc => npc.NeedsMindReconsideration);
        Assert.Equal(CrewTaskStatus.InProgress, awakened.ActiveTask?.Status);

        // C# has only raised the event. Browser cognition now chooses the
        // response, and deterministic execution validates the interruption.
        new BrowserMindSystem().Tick(state);
        Assert.Equal(ActionKind.FightFire, awakened.Intent?.Action);
        Assert.Equal("engineering", awakened.Intent?.TargetId);

        new IntentExecutionSystem().Tick(state);

        Assert.Equal(CrewTaskStatus.Interrupted, awakened.ActiveTask?.Status);
        Assert.Equal(ActionKind.FightFire, awakened.Intent?.Action);
        Assert.Equal(ActionKind.Move, awakened.CurrentAction.Kind);
    }

    [Fact]
    public async Task RuleBasedFallback_PrioritisesViableRemoteFireOverCriticalHunger()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var responder = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        PrepareSafeRemoteFireScenario(state, responder);
        responder.Hunger = CrewNeedThresholds.HungerCritical;

        var intent = await new RuleBasedAiDecisionService().DecideAsync(responder, state);

        Assert.Equal(ActionKind.FightFire, intent.Action);
        Assert.Equal("engineering", intent.TargetId);
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
        responder.Health = 100;
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
