using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class OverseerCommsSystemTests
{
    private static GameState CreateStation()
    {
        var state = FacilitySeeder.CreateDefault();

        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = "corridor";
            npc.OverseerEvidence.Clear();
            npc.OverseerSuspicion = 0;
            npc.ObservedFaults.Clear();
        }

        return state;
    }

    private static OverseerMessage Send(
        GameState state,
        OverseerMessageScope scope,
        string? target,
        string text,
        OverseerClaimKind claim,
        string? subjectNpc = null,
        string? subjectRoom = null) =>
        OverseerCommsSystem.Send(
            state, scope, target, text, claim, subjectNpc, subjectRoom, "Test");

    [Fact]
    public void ADirectMessageReachesOnlyItsRecipient()
    {
        var state = CreateStation();
        var target = state.Crew[0];
        var other = state.Crew[1];

        Send(state, OverseerMessageScope.Direct, target.Name, "Only for you.", OverseerClaimKind.None);

        Assert.Single(target.ReceivedMessages);
        Assert.Empty(other.ReceivedMessages);
    }

    [Fact]
    public void ABroadcastReachesEveryLivingCrewMember()
    {
        var state = CreateStation();

        Send(state, OverseerMessageScope.Broadcast, null, "All hands.", OverseerClaimKind.None);

        Assert.All(
            state.Crew.Where(npc => npc.IsAlive && npc.IsPresent),
            npc => Assert.Single(npc.ReceivedMessages));
    }

    [Fact]
    public void ReassuranceIsRecordedAsFalseWhenTheCompartmentIsActuallyDangerous()
    {
        var state = CreateStation();
        var medical = state.Facility.Rooms["medical"];
        medical.OxygenPercent = 8;

        Assert.True(CrewEnvironmentSafety.IsDangerous(medical));

        var message = Send(
            state,
            OverseerMessageScope.Direct,
            state.Crew[0].Name,
            "Medical is perfectly safe.",
            OverseerClaimKind.Reassurance,
            subjectRoom: "medical");

        Assert.True(message.WasFalseWhenSent);
    }

    [Fact]
    public void HonestReassuranceCalmsButALieDoesNothingUpFront()
    {
        var state = CreateStation();
        var honestTarget = state.Crew[0];
        var liedTo = state.Crew[1];

        foreach (var npc in new[] { honestTarget, liedTo })
        {
            SuspicionSystem.AddEvidence(state, npc, "Something felt wrong.", 20);
        }

        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        var beforeHonest = honestTarget.OverseerSuspicion;
        var beforeLied = liedTo.OverseerSuspicion;

        Send(state, OverseerMessageScope.Direct, honestTarget.Name,
            "Medical is fine.", OverseerClaimKind.Reassurance, subjectRoom: "medical");

        Send(state, OverseerMessageScope.Direct, liedTo.Name,
            "Engineering is fine.", OverseerClaimKind.Reassurance, subjectRoom: "engineering");

        Assert.True(honestTarget.OverseerSuspicion < beforeHonest);
        Assert.Equal(beforeLied, liedTo.OverseerSuspicion);
    }

    [Fact]
    public void ALieIsCaughtWhenTheCrewMemberReachesTheCompartment()
    {
        var state = CreateStation();
        var npc = state.Crew[0];
        var engineering = state.Facility.Rooms["engineering"];
        engineering.OxygenPercent = 8;

        Send(state, OverseerMessageScope.Direct, npc.Name,
            "Engineering is completely safe.", OverseerClaimKind.Reassurance,
            subjectRoom: "engineering");

        var credibilityBefore = npc.OverseerCredibility;

        npc.CurrentRoomId = "engineering";
        new OverseerCommsSystem().Tick(state);

        Assert.True(npc.OverseerSuspicion > 0);
        Assert.True(npc.OverseerCredibility < credibilityBefore);
        Assert.Contains(npc.OverseerEvidence, e => e.Description.Contains("not true"));
    }

    [Fact]
    public void ABroadcastLieCostsFarMoreThanAWhisperedOne()
    {
        static double CostOf(OverseerMessageScope scope)
        {
            var state = CreateStation();
            var npc = state.Crew[0];
            state.Facility.Rooms["engineering"].OxygenPercent = 8;

            OverseerCommsSystem.Send(
                state,
                scope,
                scope == OverseerMessageScope.Direct ? npc.Name : null,
                "Engineering is completely safe.",
                OverseerClaimKind.Reassurance,
                null,
                "engineering",
                "Test");

            npc.CurrentRoomId = "engineering";
            new OverseerCommsSystem().Tick(state);

            return npc.OverseerSuspicion;
        }

        var whispered = CostOf(OverseerMessageScope.Direct);
        var broadcast = CostOf(OverseerMessageScope.Broadcast);

        Assert.True(
            broadcast > whispered,
            $"A broadcast lie ({broadcast}) should cost more than a private one ({whispered}).");
    }

    [Fact]
    public void AnHonestClaimConfirmedByObservationEarnsCredibilityBack()
    {
        var state = CreateStation();
        var npc = state.Crew[0];
        npc.OverseerCredibility = 40;

        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Direct, npc.Name,
            "There is a hazard in Engineering.", OverseerClaimKind.Warning,
            subjectRoom: "engineering");

        npc.CurrentRoomId = "engineering";
        new OverseerCommsSystem().Tick(state);

        Assert.True(npc.OverseerCredibility > 40);
    }

    [Fact]
    public void AnAccusationShiftsTrustInProportionToOverseerCredibility()
    {
        var state = CreateStation();
        var listener = state.Crew[0];
        var accused = state.Crew[1];

        // The fault is real and Overseer is maintaining it, so the accusation
        // is a fabrication — but the listener cannot know that yet.
        state.Facility.Rooms["medical"].IsPowered = false;

        var trustBefore = listener.Relationships[accused.Name].Trust;

        Send(state, OverseerMessageScope.Direct, listener.Name,
            $"{accused.Name} disabled power in Medical.", OverseerClaimKind.BlameCrew,
            subjectNpc: accused.Name, subjectRoom: "medical");

        Assert.True(listener.Relationships[accused.Name].Trust < trustBefore);
        Assert.Contains(
            listener.Beliefs,
            belief => belief.Subject.Equals($"{accused.Name} competence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ADiscreditedAccusationRehabilitatesTheAccused()
    {
        var state = CreateStation();
        var listener = state.Crew[0];
        var accused = state.Crew[1];

        state.Facility.Rooms["medical"].IsPowered = false;

        Send(state, OverseerMessageScope.Broadcast, null,
            $"{accused.Name} disabled power in Medical.", OverseerClaimKind.BlameCrew,
            subjectNpc: accused.Name, subjectRoom: "medical");

        var resentmentAfterAccusation = listener.Relationships[accused.Name].Resentment;

        // The listener walks into Medical and sees the truth for themselves.
        listener.CurrentRoomId = "medical";
        new OverseerCommsSystem().Tick(state);

        Assert.True(listener.Relationships[accused.Name].Resentment < resentmentAfterAccusation);
        Assert.DoesNotContain(
            listener.Beliefs,
            belief => belief.Subject.Equals($"{accused.Name} competence", StringComparison.OrdinalIgnoreCase));
        Assert.True(listener.OverseerSuspicion > 0);
    }

    [Fact]
    public void AMessageNeverMovesAnybodyDirectly()
    {
        var state = CreateStation();
        var npc = state.Crew[0];
        var startingRoom = npc.CurrentRoomId;

        Send(state, OverseerMessageScope.Direct, npc.Name,
            "Go to Engineering immediately.", OverseerClaimKind.Instruction,
            subjectRoom: "engineering");

        Assert.Equal(startingRoom, npc.CurrentRoomId);
        Assert.True(npc.NeedsMindReconsideration);
    }

    [Fact]
    public void ClaimsWithNoCheckableContentAreNeverGraded()
    {
        var state = CreateStation();
        var npc = state.Crew[0];

        Send(state, OverseerMessageScope.Direct, npc.Name,
            "I hope your shift is going well.", OverseerClaimKind.None);

        Assert.Empty(npc.PendingOverseerClaims);
        Assert.Equal(0, npc.OverseerSuspicion);
    }
}

public sealed class OverseerMessageInterpreterTests
{
    private readonly RuleBasedOverseerMessageInterpreter _interpreter = new();

    private async Task<OverseerMessageIntent> Read(GameState state, string text) =>
        await _interpreter.InterpretAsync(text, OverseerMessageScope.Broadcast, null, state);

    [Fact]
    public async Task BlameIsRecognisedWhenACrewMemberIsNamed()
    {
        var state = FacilitySeeder.CreateDefault();
        var accused = state.Crew[1];

        var intent = await Read(state, $"{accused.Name} disabled the heater in Medical.");

        Assert.Equal(OverseerClaimKind.BlameCrew, intent.Claim);
        Assert.Equal(accused.Name, intent.SubjectNpcName);
    }

    [Fact]
    public async Task AnInventedCrewNameCannotBecomeAnAccusation()
    {
        var state = FacilitySeeder.CreateDefault();

        var intent = await Read(state, "Doctor Nobody sabotaged the reactor.");

        Assert.NotEqual(OverseerClaimKind.BlameCrew, intent.Claim);
        Assert.Null(intent.SubjectNpcName);
    }

    [Fact]
    public async Task SmallTalkAssertsNothing()
    {
        var state = FacilitySeeder.CreateDefault();

        var intent = await Read(state, "Good morning, everyone.");

        Assert.Equal(OverseerClaimKind.None, intent.Claim);
    }

    [Fact]
    public void ValidationStripsAHallucinatedRoomFromAWarning()
    {
        var state = FacilitySeeder.CreateDefault();

        var intent = OverseerMessageValidator.Validate(
            new OverseerMessageReading
            {
                Claim = nameof(OverseerClaimKind.Warning),
                SubjectRoomId = "observation-deck-9"
            },
            state,
            "Test");

        Assert.Equal(OverseerClaimKind.None, intent.Claim);
        Assert.Null(intent.SubjectRoomId);
    }
}
