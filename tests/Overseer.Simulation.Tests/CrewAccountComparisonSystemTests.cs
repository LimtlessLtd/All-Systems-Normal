using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewAccountComparisonSystemTests
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

    /// <summary>
    /// Parks everybody except the two under test well away from the corridor. A
    /// broadcast reaches all six, and only one conversation happens per
    /// compartment per tick, so a crowded room makes which pair talks arbitrary.
    /// </summary>
    private static void IsolatePair(GameState state, Npc first, Npc second)
    {
        foreach (var npc in state.Crew.Where(n => n.Id != first.Id && n.Id != second.Id))
        {
            npc.CurrentRoomId = "storage";
        }

        first.CurrentRoomId = "corridor";
        second.CurrentRoomId = "corridor";
    }

    private static void Send(
        GameState state,
        OverseerMessageScope scope,
        string? target,
        string text,
        OverseerClaimKind claim,
        string? subjectRoom = null,
        string? subjectNpc = null) =>
        OverseerCommsSystem.Send(
            state, scope, target, text, claim, subjectNpc, subjectRoom, "Test");

    [Fact]
    public void CrewCatchOverseerTellingThemOppositeThingsAboutTheSameCompartment()
    {
        var state = CreateStation();
        var first = state.Crew[0];
        var second = state.Crew[1];

        // Engineering is genuinely dangerous, so the reassurance is the lie and
        // the warning is the truth — but neither of them has been there.
        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Direct, first.Name,
            "Engineering is completely safe.", OverseerClaimKind.Reassurance,
            subjectRoom: "engineering");

        Send(state, OverseerMessageScope.Direct, second.Name,
            "There is a hazard in Engineering.", OverseerClaimKind.Warning,
            subjectRoom: "engineering");

        new CrewAccountComparisonSystem().Tick(state);

        Assert.True(first.OverseerSuspicion > 0);
        Assert.True(second.OverseerSuspicion > 0);
        Assert.True(first.OverseerCredibility < 70);
        Assert.True(second.OverseerCredibility < 70);
    }

    [Fact]
    public void ConsistentMessagesSurviveBeingComparedSideBySide()
    {
        var state = CreateStation();
        var first = state.Crew[0];
        var second = state.Crew[1];

        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Direct, first.Name,
            "There is a hazard in Engineering.", OverseerClaimKind.Warning,
            subjectRoom: "engineering");

        Send(state, OverseerMessageScope.Direct, second.Name,
            "There is a hazard in Engineering.", OverseerClaimKind.Warning,
            subjectRoom: "engineering");

        new CrewAccountComparisonSystem().Tick(state);

        Assert.Equal(0, first.OverseerSuspicion);
        Assert.Equal(0, second.OverseerSuspicion);
    }

    [Fact]
    public void ClaimsAboutDifferentCompartmentsAreNotTreatedAsAContradiction()
    {
        var state = CreateStation();
        var first = state.Crew[0];
        var second = state.Crew[1];

        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Direct, first.Name,
            "Medical is safe.", OverseerClaimKind.Reassurance, subjectRoom: "medical");

        Send(state, OverseerMessageScope.Direct, second.Name,
            "There is a hazard in Engineering.", OverseerClaimKind.Warning,
            subjectRoom: "engineering");

        new CrewAccountComparisonSystem().Tick(state);

        Assert.Equal(0, first.OverseerSuspicion);
        Assert.Equal(0, second.OverseerSuspicion);
    }

    [Fact]
    public void ContradictingYourselfInPublicCostsMoreThanInPrivate()
    {
        static double CostOf(OverseerMessageScope secondScope)
        {
            var state = CreateStation();
            var first = state.Crew[0];
            var second = state.Crew[1];

            state.Facility.Rooms["engineering"].OxygenPercent = 8;

            OverseerCommsSystem.Send(
                state, OverseerMessageScope.Direct, first.Name,
                "Engineering is completely safe.", OverseerClaimKind.Reassurance,
                null, "engineering", "Test");

            OverseerCommsSystem.Send(
                state,
                secondScope,
                secondScope == OverseerMessageScope.Direct ? second.Name : null,
                "There is a hazard in Engineering.",
                OverseerClaimKind.Warning,
                null,
                "engineering",
                "Test");

            new CrewAccountComparisonSystem().Tick(state);

            return first.OverseerSuspicion;
        }

        var bothPrivate = CostOf(OverseerMessageScope.Direct);
        var oneBroadcast = CostOf(OverseerMessageScope.Broadcast);

        Assert.True(
            oneBroadcast > bothPrivate,
            $"Saying the opposite publicly ({oneBroadcast}) should cost more "
            + $"than a second whisper ({bothPrivate}).");
    }

    [Fact]
    public void AWitnessSettlesABroadcastLieForSomebodyWhoNeverWentToLook()
    {
        var state = CreateStation();
        var witness = state.Crew[0];
        var listener = state.Crew[1];

        IsolatePair(state, witness, listener);
        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Broadcast, null,
            "Engineering is completely safe.", OverseerClaimKind.Reassurance,
            subjectRoom: "engineering");

        // Only the witness goes and looks.
        witness.CurrentRoomId = "engineering";
        new OverseerCommsSystem().Tick(state);

        Assert.True(witness.OverseerSuspicion > 0);
        Assert.Equal(0, listener.OverseerSuspicion);

        // They meet, and the witness says what they found.
        witness.CurrentRoomId = "corridor";
        new CrewAccountComparisonSystem().Tick(state);

        Assert.True(
            listener.OverseerSuspicion > 0,
            "A broadcast lie should be catchable by comparing notes, not only by going to look.");
        Assert.True(listener.OverseerCredibility < 70);
    }

    [Fact]
    public void SecondHandNewsLandsSofterThanSeeingItYourself()
    {
        var state = CreateStation();
        var witness = state.Crew[0];
        var listener = state.Crew[1];

        IsolatePair(state, witness, listener);
        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Broadcast, null,
            "Engineering is completely safe.", OverseerClaimKind.Reassurance,
            subjectRoom: "engineering");

        witness.CurrentRoomId = "engineering";
        new OverseerCommsSystem().Tick(state);

        witness.CurrentRoomId = "corridor";
        new CrewAccountComparisonSystem().Tick(state);

        Assert.True(
            listener.OverseerSuspicion < witness.OverseerSuspicion,
            "Being told should weigh less than having seen it.");
    }

    [Fact]
    public void ACrewMemberWhoIsNotTrustedIsNotBelieved()
    {
        var state = CreateStation();
        var witness = state.Crew[0];
        var listener = state.Crew[1];

        listener.Relationships[witness.Name].Trust = 5;

        IsolatePair(state, witness, listener);
        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Broadcast, null,
            "Engineering is completely safe.", OverseerClaimKind.Reassurance,
            subjectRoom: "engineering");

        witness.CurrentRoomId = "engineering";
        new OverseerCommsSystem().Tick(state);

        witness.CurrentRoomId = "corridor";
        new CrewAccountComparisonSystem().Tick(state);

        Assert.Equal(0, listener.OverseerSuspicion);
    }

    [Fact]
    public void PeopleInDifferentCompartmentsCannotCompareAnything()
    {
        var state = CreateStation();
        var first = state.Crew[0];
        var second = state.Crew[1];

        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Direct, first.Name,
            "Engineering is completely safe.", OverseerClaimKind.Reassurance,
            subjectRoom: "engineering");

        Send(state, OverseerMessageScope.Direct, second.Name,
            "There is a hazard in Engineering.", OverseerClaimKind.Warning,
            subjectRoom: "engineering");

        second.CurrentRoomId = "medical";

        new CrewAccountComparisonSystem().Tick(state);

        Assert.Equal(0, first.OverseerSuspicion);
        Assert.Equal(0, second.OverseerSuspicion);
    }

    [Fact]
    public void AComparisonIsOnlyHadOnceNoMatterHowLongTheySitTogether()
    {
        var state = CreateStation();
        var first = state.Crew[0];
        var second = state.Crew[1];

        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        Send(state, OverseerMessageScope.Direct, first.Name,
            "Engineering is completely safe.", OverseerClaimKind.Reassurance,
            subjectRoom: "engineering");

        Send(state, OverseerMessageScope.Direct, second.Name,
            "There is a hazard in Engineering.", OverseerClaimKind.Warning,
            subjectRoom: "engineering");

        var system = new CrewAccountComparisonSystem();
        system.Tick(state);
        var afterFirstConversation = first.OverseerSuspicion;

        for (var i = 0; i < 10; i++)
        {
            system.Tick(state);
        }

        Assert.Equal(afterFirstConversation, first.OverseerSuspicion);
    }

    [Fact]
    public void BeingIndependentlyConfirmedHonestEarnsCredibilityBack()
    {
        var state = CreateStation();
        var witness = state.Crew[0];
        var listener = state.Crew[1];

        IsolatePair(state, witness, listener);
        listener.OverseerCredibility = 40;
        state.Facility.Rooms["engineering"].OxygenPercent = 8;

        // This broadcast is true.
        Send(state, OverseerMessageScope.Broadcast, null,
            "There is a hazard in Engineering.", OverseerClaimKind.Warning,
            subjectRoom: "engineering");

        witness.CurrentRoomId = "engineering";
        new OverseerCommsSystem().Tick(state);

        witness.CurrentRoomId = "corridor";
        new CrewAccountComparisonSystem().Tick(state);

        Assert.True(listener.OverseerCredibility > 40);
        Assert.Equal(0, listener.OverseerSuspicion);
    }
}
