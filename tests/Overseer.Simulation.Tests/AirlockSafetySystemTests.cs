using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class AirlockSafetySystemTests
{
    [Fact]
    public void PressurizedAirlock_RefusesNormalOuterHatchOpening()
    {
        var state = FacilitySeeder.CreateDefault();
        var system = new AirlockSafetySystem();

        var success = system.TryToggleExteriorHatch(
            state,
            "airlock",
            out var opened,
            out var message);

        Assert.False(success);
        Assert.False(opened);
        Assert.False(state.Facility.Rooms["airlock"].ExteriorHatchOpen);
        Assert.Contains("pressure", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SealedAirlock_CanDepressurizeThenOpenOuterHatch()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var system = new AirlockSafetySystem();

        Assert.True(system.TryStartCycle(
            state,
            "airlock",
            AirlockCycleMode.Depressurizing,
            out _));

        for (var i = 0; i < 4; i++)
        {
            system.Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.Equal(AirlockCycleMode.Idle, airlock.AirlockCycleMode);
        Assert.InRange(
            airlock.PressureKpa,
            AirlockSafetySystem.DepressurizedPressureKpa,
            AirlockSafetySystem.ExteriorOpenPressureKpa);

        Assert.True(system.TryToggleExteriorHatch(
            state,
            "airlock",
            out var opened,
            out _));
        Assert.True(opened);
        Assert.True(airlock.ExteriorHatchOpen);
        Assert.False(airlock.AirlockAlarmActive);
    }

    [Fact]
    public void DepressurizedAirlock_RefusesInnerHatchUntilRepressurized()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var innerDoor = AirlockSafetySystem.FindInnerDoor(state, airlock)!;
        var system = new AirlockSafetySystem();

        airlock.PressureKpa = 2;
        airlock.OxygenPercent = 0.4;

        Assert.False(system.CanToggleInnerHatch(
            state,
            innerDoor,
            opening: true,
            out var refusal));
        Assert.Contains(
            "pressure differential",
            refusal,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(system.TryStartCycle(
            state,
            "airlock",
            AirlockCycleMode.Pressurizing,
            out _));

        for (var i = 0; i < 7; i++)
        {
            system.Tick(state, TimeSpan.FromMinutes(1));
        }

        Assert.Equal(AirlockCycleMode.Idle, airlock.AirlockCycleMode);
        Assert.InRange(airlock.PressureKpa, 100.8, 101.3);
        Assert.True(system.CanToggleInnerHatch(
            state,
            innerDoor,
            opening: true,
            out _));
    }

    [Fact]
    public void SafetyBypass_PreservesDeliberatelyUnsafeDecompressionGameplay()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var victim = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var system = new AirlockSafetySystem();

        victim.CurrentRoomId = "airlock";

        Assert.True(system.TryToggleSafetyInterlocks(
            state,
            "airlock",
            out _));
        Assert.False(airlock.AirlockSafetyInterlocksEnabled);

        Assert.True(system.TryToggleExteriorHatch(
            state,
            "airlock",
            out var opened,
            out _));
        Assert.True(opened);

        new EnvironmentSystem().Tick(state, TimeSpan.FromMinutes(1));
        new VacuumConsequenceSystem().Tick(state);

        Assert.False(victim.IsAlive);
        Assert.False(victim.IsPresent);
        Assert.Contains(
            "no body remains aboard",
            victim.CauseOfDeath!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DistantCrew_DoNotMagicallyObserveUnsafeAirlock()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var distant = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var system = new AirlockSafetySystem();

        distant.CurrentRoomId = "engineering";
        airlock.AirlockSafetyInterlocksEnabled = false;

        system.Tick(state, TimeSpan.FromMinutes(1));

        Assert.DoesNotContain("airlock", distant.ObservedUnsafeAirlocks);
        Assert.False(distant.NeedsMindReconsideration);
    }

    [Fact]
    public void NearbyCapableCrew_ObserveHazardAndBrowserMindChoosesSecureAirlock()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var system = new AirlockSafetySystem();

        david.CurrentRoomId = "hall-airlock";
        airlock.AirlockSafetyInterlocksEnabled = false;

        system.Tick(state, TimeSpan.FromMinutes(1));

        Assert.Contains("airlock", david.ObservedUnsafeAirlocks);
        Assert.True(david.NeedsMindReconsideration);

        new BrowserMindSystem().Tick(state);

        Assert.NotNull(david.Intent);
        Assert.Equal(ActionKind.SecureAirlock, david.Intent!.Action);
        Assert.Equal("airlock", david.Intent.TargetId);
    }

    [Fact]
    public void SecureAirlockAction_ClosesOuterRestoresInterlocksAndStartsRepressurizing()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var actionResolver = new ActionResolver();
        var counterplay = new CrewCounterplaySystem();

        david.CurrentRoomId = "hall-airlock";
        airlock.PressureKpa = 20;
        airlock.OxygenPercent = 4;
        airlock.ExteriorHatchOpen = true;
        airlock.AirlockSafetyInterlocksEnabled = false;
        airlock.AirlockAlarmActive = true;

        Assert.True(actionResolver.TryApply(
            state,
            david.Id,
            new NpcAction(
                ActionKind.SecureAirlock,
                "airlock",
                "Secure the unsafe airlock."),
            out _));

        counterplay.Tick(state);
        Assert.True(david.RoutineUntil > state.Elapsed);

        state.Elapsed = david.RoutineUntil;
        counterplay.Tick(state);

        Assert.False(airlock.ExteriorHatchOpen);
        Assert.True(airlock.AirlockSafetyInterlocksEnabled);
        Assert.Equal(
            AirlockCycleMode.Pressurizing,
            airlock.AirlockCycleMode);
        Assert.Equal(ActionKind.Idle, david.CurrentAction.Kind);
    }

    [Fact]
    public async Task FallbackMind_ChoosesSecureAirlockOnlyWhenItCanPerceiveHazard()
    {
        var state = FacilitySeeder.CreateDefault();
        var airlock = state.Facility.Rooms["airlock"];
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var service = new RuleBasedAiDecisionService();

        airlock.AirlockSafetyInterlocksEnabled = false;
        david.CurrentRoomId = "hall-airlock";

        var nearbyIntent = await service.DecideAsync(david, state);

        Assert.Equal(ActionKind.SecureAirlock, nearbyIntent.Action);
        Assert.Equal("airlock", nearbyIntent.TargetId);

        david.CurrentRoomId = "control";
        var distantIntent = await service.DecideAsync(david, state);

        Assert.NotEqual(ActionKind.SecureAirlock, distantIntent.Action);
    }
}
