using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// P1 ladder convergence, final piece: <c>RuleBasedAiDecisionService</c> had
/// no fire-fighting branch at all — a room on fire always sent the
/// server-side fallback mind fleeing (or idling), regardless of the NPC's
/// skill or courage, while <c>BrowserMindSystem</c> would actively choose
/// <see cref="ActionKind.FightFire"/>. The owner confirmed (2026-09-23,
/// #agentic-problems) the fallback should match: fighting a survivable fire
/// is the default position given enough courage/skill. Both minds now
/// delegate to the same <see cref="StationHazardSystem.ShouldFightFire"/>.
/// </summary>
public sealed class FireFightingConvergenceTests
{
    private static void SuppressCourage(Npc npc) =>
        npc.Traits.Add(new CrewTrait(
            "Test Rig (low courage)",
            "Deliberately suppressed for this test.",
            [new CrewTraitEffect(TraitEffectKind.Courage, -100)]));

    private static void MaximizeCourage(Npc npc) =>
        npc.Traits.Add(new CrewTrait(
            "Test Rig (high courage)",
            "Deliberately maximized for this test.",
            [new CrewTraitEffect(TraitEffectKind.Courage, 100)]));

    [Fact]
    public void ShouldFightFire_SkilledCrewQualifiesEvenWithLowCourage()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        room.FireIntensity = 30;
        npc.Skills["Engineering"] = 80;
        npc.Traits.Clear();
        SuppressCourage(npc);

        Assert.True(StationHazardSystem.ShouldFightFire(npc, room));
    }

    [Fact]
    public void ShouldFightFire_CourageousCrewQualifiesEvenWithLowSkill()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        room.FireIntensity = 30;
        foreach (var skill in npc.Skills.Keys.ToList()) npc.Skills[skill] = 0;
        npc.Traits.Clear();
        MaximizeCourage(npc);

        Assert.True(StationHazardSystem.ShouldFightFire(npc, room));
    }

    [Fact]
    public void ShouldFightFire_RefusesAnOverwhelmingFireRegardlessOfSkillOrCourage()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        room.FireIntensity = 90;
        npc.Skills["Engineering"] = 100;
        npc.Traits.Clear();
        MaximizeCourage(npc);

        Assert.False(StationHazardSystem.ShouldFightFire(npc, room));
    }

    [Fact]
    public void ShouldFightFire_RefusesWhenTooStressedOrExhausted()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        room.FireIntensity = 30;
        npc.Skills["Engineering"] = 100;
        npc.Traits.Clear();
        MaximizeCourage(npc);
        npc.Stress = 95;

        Assert.False(StationHazardSystem.ShouldFightFire(npc, room));
    }

    [Fact]
    public async Task FallbackMind_NowFightsASurvivableFireInsteadOfAlwaysFleeing()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var room = state.Facility.Rooms[david.CurrentRoomId];
        room.FireIntensity = 30;
        david.Skills["Engineering"] = 80;
        david.Traits.Clear();

        var intent = await new RuleBasedAiDecisionService().DecideAsync(david, state);

        Assert.Equal(ActionKind.FightFire, intent.Action);
        Assert.Equal(room.Id, intent.TargetId);
    }

    [Fact]
    public async Task FallbackMind_StillFleesAFireItCannotWin()
    {
        var state = FacilitySeeder.CreateDefault();
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var room = state.Facility.Rooms[david.CurrentRoomId];
        room.FireIntensity = 90;
        david.Skills["Engineering"] = 100;
        david.Traits.Clear();

        var intent = await new RuleBasedAiDecisionService().DecideAsync(david, state);

        Assert.NotEqual(ActionKind.FightFire, intent.Action);
    }

    [Fact]
    public async Task BothLaddersAgreeOnFightingTheSameSurvivableFire()
    {
        var browserState = FacilitySeeder.CreateDefault();
        var browserDavid = browserState.Crew.Single(npc => npc.Name == "David Hale");
        var browserRoom = browserState.Facility.Rooms[browserDavid.CurrentRoomId];
        browserRoom.FireIntensity = 30;
        browserDavid.Skills["Engineering"] = 80;
        browserDavid.Traits.Clear();
        browserState.Elapsed = TimeSpan.FromMinutes(6);

        new BrowserMindSystem().Tick(browserState);

        var fallbackState = FacilitySeeder.CreateDefault();
        var fallbackDavid = fallbackState.Crew.Single(npc => npc.Name == "David Hale");
        var fallbackRoom = fallbackState.Facility.Rooms[fallbackDavid.CurrentRoomId];
        fallbackRoom.FireIntensity = 30;
        fallbackDavid.Skills["Engineering"] = 80;
        fallbackDavid.Traits.Clear();

        var fallbackIntent = await new RuleBasedAiDecisionService().DecideAsync(fallbackDavid, fallbackState);

        Assert.Equal(ActionKind.FightFire, fallbackIntent.Action);
        Assert.NotNull(browserDavid.Intent);
        Assert.Equal(ActionKind.FightFire, browserDavid.Intent!.Action);
    }
    // Owner report 2026-09-24: a crew member walked into a room, saw a small
    // fire and walked out. Below the "dangerous" threshold neither ladder had
    // a branch for a fire in the person's own room (the remote-fire search
    // skips it), so both minds ignored it.
    [Fact]
    public async Task BothLaddersPutOutASmallFireInTheirOwnRoom()
    {
        var browserState = FacilitySeeder.CreateDefault();
        var browserDavid = browserState.Crew.Single(npc => npc.Name == "David Hale");
        var browserRoom = browserState.Facility.Rooms[browserDavid.CurrentRoomId];
        FireFrontRules.Ignite(browserRoom, 50, 50, intensity: 5);
        Assert.False(CrewEnvironmentSafety.IsDangerous(browserRoom));
        browserDavid.Skills["Engineering"] = 80;
        browserDavid.Traits.Clear();
        browserDavid.Intent = null;
        browserDavid.RoutineUntil = TimeSpan.Zero;
        foreach (var npc in browserState.Crew)
        {
            npc.NeedsMindReconsideration = npc.Id == browserDavid.Id;
        }

        new BrowserMindSystem().Tick(browserState);

        var fallbackState = FacilitySeeder.CreateDefault();
        var fallbackDavid = fallbackState.Crew.Single(npc => npc.Name == "David Hale");
        var fallbackRoom = fallbackState.Facility.Rooms[fallbackDavid.CurrentRoomId];
        FireFrontRules.Ignite(fallbackRoom, 50, 50, intensity: 5);
        fallbackDavid.Skills["Engineering"] = 80;
        fallbackDavid.Traits.Clear();

        var fallbackIntent = await new RuleBasedAiDecisionService().DecideAsync(fallbackDavid, fallbackState);

        Assert.Equal(ActionKind.FightFire, fallbackIntent.Action);
        Assert.Equal(fallbackRoom.Id, fallbackIntent.TargetId);
        Assert.NotNull(browserDavid.Intent);
        Assert.Equal(ActionKind.FightFire, browserDavid.Intent!.Action);
        Assert.Equal(browserRoom.Id, browserDavid.Intent.TargetId);
    }
}
