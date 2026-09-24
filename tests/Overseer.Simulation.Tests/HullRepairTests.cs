using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class HullRepairTests
{
    [Fact]
    public void PatchHull_OnlyClearsBreachOnDeterministicCompletion()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["engineering"];
        var npc = state.Crew[0];
        npc.CurrentRoomId = room.Id;
        npc.Health = 100;
        npc.Skills["Engineering"] = 80;
        room.HasHullBreach = true;
        room.HullIntegrityPercent = 0;
        room.VentilationEnabled = false;
        room.FireIntensity = 0;

        npc.Intent = new NpcIntent(
            ActionKind.PatchHull,
            room.Id,
            "Patch the breach.",
            "The station is venting.",
            99,
            "test",
            state.Elapsed);

        var execution = new IntentExecutionSystem();
        execution.Tick(state);

        Assert.True(room.HasHullBreach);
        Assert.Equal(CrewTaskStatus.InProgress, npc.ActiveTask?.Status);
        Assert.Equal(ActionKind.PatchHull, npc.ActiveTask?.Action);

        state.Elapsed += StationHazardSystem.HullRepairRules.PatchDuration - TimeSpan.FromSeconds(1);
        execution.Tick(state);
        Assert.True(room.HasHullBreach);

        state.Elapsed += TimeSpan.FromSeconds(1);
        execution.Tick(state);

        Assert.False(room.HasHullBreach);
        Assert.True(room.VentilationEnabled);
        Assert.True(room.HullIntegrityPercent >= StationHazardSystem.HullRepairRules.RestoredHullIntegrityPercent);
        Assert.Equal(CrewTaskStatus.Succeeded, npc.ActiveTask?.Status);
    }

    [Fact]
    public void PatchHull_RejectsUnskilledWorkerAndActiveFire()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["engineering"];
        var npc = state.Crew[0];
        npc.CurrentRoomId = room.Id;
        npc.Health = 100;
        npc.Skills["Engineering"] = 0;
        npc.Skills["Electrical"] = 0;
        npc.Skills["Operations"] = 0;
        npc.Skills["Reactor"] = 0;
        room.HasHullBreach = true;
        room.FireIntensity = 0;

        Assert.False(StationHazardSystem.HullRepairRules.CanAttempt(npc, room));

        npc.Skills["Engineering"] = 80;
        room.FireIntensity = 5;

        Assert.False(StationHazardSystem.HullRepairRules.CanAttempt(npc, room));
    }

    [Fact]
    public void ChosenPatchHull_ProtectsOnlyThePatcher()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["engineering"];
        var npc = state.Crew[0];
        var bystander = state.Crew[1];
        npc.CurrentRoomId = room.Id;
        bystander.CurrentRoomId = room.Id;
        npc.Health = 100;
        npc.Skills["Engineering"] = 80;
        room.HasHullBreach = true;
        room.FireIntensity = 0;
        room.PressureKpa = 0;
        npc.Intent = PatchIntent(state, room.Id);

        new VacuumConsequenceSystem().Tick(state);

        Assert.True(npc.IsPresent);
        Assert.True(npc.IsWearingEmergencySuit);
        Assert.False(bystander.IsPresent);
    }

    [Fact]
    public void EmergencySuit_ProtectsOnTheWayInOutsideTheTargetRoom()
    {
        // #210 audit: protection was keyed to the target room, so a depth-1
        // neighbour below the ejection line could still eject the patcher.
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var corridor = state.Facility.Rooms.Values.First(candidate =>
            !candidate.Id.Equals("engineering", StringComparison.OrdinalIgnoreCase));
        npc.CurrentRoomId = corridor.Id;
        npc.Health = 100;
        corridor.PressureKpa = 10;
        corridor.OxygenPercent = 2;
        npc.Intent = PatchIntent(state, "engineering");

        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(StationHazardSystem.HullRepairRules.HasEmergencyPressureProtection(npc));
        Assert.True(npc.Health > 99, $"health {npc.Health}");
    }

    [Fact]
    public void EmergencySuit_StaysOnAfterThePatchUntilAirIsSafe()
    {
        // #210 audit: the suit ended with the task, leaving the patcher
        // unprotected in the ~0 kPa room it had just sealed.
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["engineering"];
        var npc = state.Crew[0];
        npc.CurrentRoomId = room.Id;
        npc.Health = 100;
        npc.Skills["Engineering"] = 80;
        room.HasHullBreach = true;
        room.FireIntensity = 0;
        room.PressureKpa = 0;
        room.OxygenPercent = 0;
        npc.Intent = PatchIntent(state, room.Id);

        StationHazardSystem.HullRepairRules.UpdateEmergencySuits(state);
        npc.Intent = null;
        npc.ActiveTask = null;
        room.HasHullBreach = false;

        StationHazardSystem.HullRepairRules.UpdateEmergencySuits(state);
        Assert.True(npc.IsWearingEmergencySuit);
        new SimulationEngine().Tick(state, TimeSpan.FromMinutes(1));
        Assert.True(npc.Health > 99, $"health {npc.Health}");

        room.PressureKpa = StationHazardSystem.HullRepairRules.SafePressureKpa;
        room.OxygenPercent = 20.9;
        StationHazardSystem.HullRepairRules.UpdateEmergencySuits(state);
        Assert.False(npc.IsWearingEmergencySuit);
    }

    [Fact]
    public void BrowserMind_DoesNotSendASecondPatcherToAClaimedBreach()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["engineering"];
        var first = state.Crew[0];
        var second = state.Crew[1];
        foreach (var npc in new[] { first, second })
        {
            npc.CurrentRoomId = room.Id;
            npc.Health = 100;
            npc.Skills["Engineering"] = 80;
        }

        room.HasHullBreach = true;
        room.FireIntensity = 0;
        room.PressureKpa = 20;
        first.Intent = PatchIntent(state, room.Id);

        new BrowserMindSystem().Tick(state);

        Assert.Equal(ActionKind.PatchHull, first.Intent?.Action);
        Assert.NotEqual(ActionKind.PatchHull, second.Intent?.Action);
    }

    private static NpcIntent PatchIntent(GameState state, string roomId) =>
        new(
            ActionKind.PatchHull,
            roomId,
            "Patch the breach.",
            "The station is venting.",
            99,
            "test",
            state.Elapsed);

    [Fact]
    public void BrowserMind_ChoosesReachableHullPatchBeforeFleeingVacuum()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["engineering"];
        var npc = state.Crew[0];
        npc.CurrentRoomId = room.Id;
        npc.Health = 100;
        npc.Skills["Engineering"] = 80;
        room.HasHullBreach = true;
        room.FireIntensity = 0;
        room.PressureKpa = 20;

        new BrowserMindSystem().Tick(state);

        Assert.Equal(ActionKind.PatchHull, npc.Intent?.Action);
        Assert.Equal(room.Id, npc.Intent?.TargetId);
    }

    [Fact]
    public async Task RuleBasedMind_ChoosesSameHullPatch()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["engineering"];
        var npc = state.Crew[0];
        npc.CurrentRoomId = room.Id;
        npc.Health = 100;
        npc.Skills["Engineering"] = 80;
        room.HasHullBreach = true;
        room.FireIntensity = 0;
        room.PressureKpa = 20;

        var intent = await new RuleBasedAiDecisionService().DecideAsync(npc, state);

        Assert.Equal(ActionKind.PatchHull, intent.Action);
        Assert.Equal(room.Id, intent.TargetId);
    }

    [Fact]
    public void OllamaPrompt_OffersOnlyGroundedPatchTarget()
    {
        var state = FacilitySeeder.CreateDefault();
        var room = state.Facility.Rooms["engineering"];
        var npc = state.Crew[0];
        npc.CurrentRoomId = room.Id;
        npc.Health = 100;
        npc.Skills["Engineering"] = 80;
        room.HasHullBreach = true;
        room.FireIntensity = 0;

        var prompt = NpcPromptBuilder.Build(npc, state);

        Assert.Contains("BREACHED HULL ROOMS YOU COULD PATCH:", prompt);
        Assert.Contains($"- {room.Id} = {room.Name}", prompt);
        Assert.Contains("PatchHull [breached-room]", prompt);

        room.FireIntensity = 5;
        prompt = NpcPromptBuilder.Build(npc, state);

        Assert.DoesNotContain("PatchHull [breached-room]", prompt);
        Assert.DoesNotContain("BREACHED HULL ROOMS YOU COULD PATCH:", prompt);
    }
}
