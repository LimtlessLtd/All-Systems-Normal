using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #89: Overseer's station-wide FIRE ALARM informs every mind of a
/// named compartment, is graded like any other Overseer claim, and never
/// assigns a responder.
/// </summary>
public sealed class FireAlarmTests
{
    [Fact]
    public void FireAlarm_ReachesEveryPresentCrewMemberAndWakesTheirMinds()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        PrepareFire(state);

        var message = OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        Assert.NotNull(message);
        Assert.Equal(OverseerMessageScope.Broadcast, message!.Scope);
        Assert.Equal(OverseerClaimKind.FireAlarm, message.Claim);
        Assert.Equal("engineering", message.SubjectRoomId);
        Assert.Contains(state.Facility.Rooms["engineering"].Name, message.Text);

        foreach (var npc in state.Crew.Where(npc => npc.IsAlive && npc.IsPresent))
        {
            Assert.Contains(npc.ReceivedMessages, received => received.Sequence == message.Sequence);
            Assert.True(npc.NeedsMindReconsideration);
        }
    }

    [Fact]
    public void FireAlarm_AssignsNobodyAGoal()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        PrepareFire(state);

        OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        // Core rule: the alarm is information. It must not script FightFire.
        Assert.DoesNotContain(state.Crew, npc => npc.Intent is { Action: ActionKind.FightFire });
    }

    [Fact]
    public void FireAlarm_ForABurningRoomIsHonest()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        PrepareFire(state);

        var message = OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        Assert.False(message!.WasFalseWhenSent);
    }

    [Fact]
    public void FireAlarm_ForASmokyButUnburningRoomIsStillFalse()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        PrepareFire(state);
        var room = state.Facility.Rooms["engineering"];
        room.FireIntensity = 0;
        room.SmokePercent = 90;

        var message = OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        Assert.True(message!.WasFalseWhenSent);
    }

    [Fact]
    public void FalseFireAlarm_CostsCredibilityOnceAListenerSeesTheRoom()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        PrepareFire(state);
        state.Facility.Rooms["engineering"].FireIntensity = 0;
        var listener = state.Crew.First(npc => npc.IsAlive && npc.IsPresent);
        var credibilityBefore = listener.OverseerCredibility;

        OverseerCommsSystem.SoundFireAlarm(state, "engineering");
        listener.CurrentRoomId = "engineering";
        new OverseerCommsSystem().Tick(state);

        Assert.True(listener.OverseerCredibility < credibilityBefore);
    }

    [Fact]
    public void FireAlarm_RefusesUnknownRoomAndStoppedScenario()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);

        Assert.Null(OverseerCommsSystem.SoundFireAlarm(state, "no-such-room"));

        state.ScenarioStatus = ScenarioStatus.Failed;
        Assert.Null(OverseerCommsSystem.SoundFireAlarm(state, "engineering"));
        Assert.Empty(state.OverseerMessages);
    }

    [Fact]
    public void TypedFireAlarmClaim_IsDowngradedToAnOrdinaryWarning()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);

        var intent = OverseerMessageValidator.Validate(
            new OverseerMessageReading { Claim = "FireAlarm", SubjectRoomId = "engineering" },
            state,
            "test");

        Assert.Equal(OverseerClaimKind.Warning, intent.Claim);
    }

    [Fact]
    public void CredibleFireAlarm_LetsASecondFallbackResponderJoinTheFire()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var (first, second) = PrepareTwoResponders(state);

        Assert.Null(StationHazardSystem.FindRemoteFireForResponder(state, second, new NavigationSystem()));

        OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        var target = StationHazardSystem.FindRemoteFireForResponder(state, second, new NavigationSystem());
        Assert.Equal("engineering", target?.Id);
        Assert.Equal(ActionKind.FightFire, first.Intent!.Action);
    }

    [Fact]
    public void FireAlarm_DoesNotRecruitAThirdResponder()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var (_, second) = PrepareTwoResponders(state);
        var third = state.Crew.First(npc => npc.Id != second.Id && npc.Intent is null);
        MakeCapable(third);
        OverseerCommsSystem.SoundFireAlarm(state, "engineering");
        second.Intent = FightFireIntent(state);

        Assert.Null(StationHazardSystem.FindRemoteFireForResponder(state, third, new NavigationSystem()));
    }

    [Fact]
    public void DistrustedFireAlarm_DoesNotChangeTheFallbackResponderLimit()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var (_, second) = PrepareTwoResponders(state);
        second.OverseerCredibility = 20;

        OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        Assert.Null(StationHazardSystem.FindRemoteFireForResponder(state, second, new NavigationSystem()));
    }

    [Fact]
    public void StaleFireAlarm_NoLongerChangesTheFallbackResponderLimit()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var (_, second) = PrepareTwoResponders(state);

        OverseerCommsSystem.SoundFireAlarm(state, "engineering");
        state.Elapsed += StationHazardSystem.FireAlarmResponseWindow + TimeSpan.FromMinutes(1);

        Assert.Null(StationHazardSystem.FindRemoteFireForResponder(state, second, new NavigationSystem()));
    }

    [Fact]
    public async Task RuleBasedFallback_AnswersACredibleAlarmAlongsideAnExistingResponder()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var (_, second) = PrepareTwoResponders(state);
        OverseerCommsSystem.SoundFireAlarm(state, "engineering");

        var intent = await new RuleBasedAiDecisionService().DecideAsync(second, state);

        Assert.Equal(ActionKind.FightFire, intent.Action);
        Assert.Equal("engineering", intent.TargetId);
    }

    [Fact]
    public void NpcPrompt_ShowsTheFireAlarmToOllamaCognition()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        PrepareFire(state);
        var listener = state.Crew.First(npc => npc.IsAlive && npc.IsPresent);

        OverseerCommsSystem.SoundFireAlarm(state, "engineering");
        var prompt = NpcPromptBuilder.Build(listener, state);

        Assert.Contains("FIRE ALARM", prompt);
        Assert.Contains(state.Facility.Rooms["engineering"].Name, prompt);
    }

    [Fact]
    public void HomeToolbar_ExposesAFireAlarmThatTargetsTheNextSelectedRoom()
    {
        var razor = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "Overseer.Web.UI",
            "Pages",
            "Home.razor"));

        Assert.Contains("fire-alarm-toggle", razor);
        Assert.Contains("Session.SoundFireAlarm(roomId)", razor);
    }

    private static (Npc First, Npc Second) PrepareTwoResponders(GameState state)
    {
        PrepareFire(state);
        var first = state.Crew.First(npc => npc.Role == CrewRole.Engineer);
        var second = state.Crew.First(npc => npc.Id != first.Id);
        MakeCapable(first);
        MakeCapable(second);
        first.Intent = FightFireIntent(state);
        return (first, second);
    }

    private static NpcIntent FightFireIntent(GameState state) =>
        new(
            ActionKind.FightFire,
            "engineering",
            "Respond to the fire.",
            "Already committed.",
            94,
            "Test",
            state.Elapsed);

    private static void MakeCapable(Npc npc)
    {
        npc.Hunger = 0;
        npc.Fatigue = 0;
        npc.Stress = 0;
        npc.Health = 100;
        npc.OverseerCredibility = 70;
        npc.OverseerSuspicion = 0;
        npc.Skills["Engineering"] = 85;
    }

    private static void PrepareFire(GameState state)
    {
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = "control";
            npc.Intent = null;
            npc.ActiveTask = null;
            npc.NeedsMindReconsideration = false;
        }

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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Overseer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
