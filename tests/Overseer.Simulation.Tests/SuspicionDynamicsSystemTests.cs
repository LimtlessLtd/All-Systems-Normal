using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class SuspicionDynamicsSystemTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static GameState CreateQuietStation()
    {
        var state = FacilitySeeder.CreateDefault();

        // Park everyone somewhere unremarkable so ambient fault observation does
        // not interfere with the behaviour under test.
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = "corridor";
            npc.OverseerEvidence.Clear();
            npc.OverseerSuspicion = 0;
            npc.ObservedFaults.Clear();
        }

        return state;
    }

    private static void Advance(GameState state, SuspicionDynamicsSystem system, int minutes)
    {
        for (var i = 0; i < minutes; i++)
        {
            state.Elapsed += Minute;
            system.Tick(state, Minute);
        }
    }

    [Fact]
    public void EvidenceFadesAndSuspicionFallsWithIt()
    {
        var state = CreateQuietStation();
        var npc = state.Crew[0];

        SuspicionSystem.AddEvidence(state, npc, "Overseer did something alarming.", 20);
        var initial = npc.OverseerSuspicion;

        Advance(state, new SuspicionDynamicsSystem(), 90);

        Assert.True(
            npc.OverseerSuspicion < initial,
            $"Expected suspicion to decay from {initial}, got {npc.OverseerSuspicion}.");
    }

    [Fact]
    public void HearsayFadesFasterThanFirsthandObservation()
    {
        var state = CreateQuietStation();
        var witness = state.Crew[0];
        var listener = state.Crew[1];

        SuspicionSystem.AddEvidence(
            state, witness, "I saw Overseer seal the hatch.", 20,
            origin: EvidenceOrigin.Direct);

        SuspicionSystem.AddEvidence(
            state, listener, "Someone told me Overseer sealed the hatch.", 20,
            source: witness.Name,
            origin: EvidenceOrigin.Hearsay);

        Advance(state, new SuspicionDynamicsSystem(), 60);

        Assert.True(
            listener.OverseerSuspicion < witness.OverseerSuspicion,
            "Second-hand belief should erode faster than what was witnessed directly.");
    }

    [Fact]
    public void AClaimTheStationContradictsIsDiscreditedOnInspection()
    {
        var state = CreateQuietStation();
        var npc = state.Crew[0];

        // The NPC believes Medical lost power, and is standing in Medical.
        npc.CurrentRoomId = "medical";
        state.Facility.Rooms["medical"].IsPowered = true;

        SuspicionSystem.AddEvidence(
            state, npc, "Overseer cut the power in Medical.", 20,
            origin: EvidenceOrigin.Direct,
            claim: EvidenceClaim.PowerCut,
            subjectRoomId: "medical");

        Advance(state, new SuspicionDynamicsSystem(), 1);

        var evidence = Assert.Single(
            npc.OverseerEvidence,
            e => e.Claim == EvidenceClaim.PowerCut);
        Assert.True(evidence.IsDiscredited);
        Assert.True(evidence.CurrentWeight < 20);
    }

    [Fact]
    public void ARumourContradictedByTheStationCostsTheTellerTrust()
    {
        var state = CreateQuietStation();
        var teller = state.Crew[0];
        var listener = state.Crew[1];

        listener.CurrentRoomId = "medical";
        state.Facility.Rooms["medical"].IsPowered = true;

        var trustBefore = listener.Relationships[teller.Name].Trust;

        SuspicionSystem.AddEvidence(
            state, listener, $"{teller.Name} told me Overseer cut the power in Medical.", 12,
            source: teller.Name,
            origin: EvidenceOrigin.Hearsay,
            claim: EvidenceClaim.PowerCut,
            subjectRoomId: "medical");

        Advance(state, new SuspicionDynamicsSystem(), 1);

        Assert.True(
            listener.Relationships[teller.Name].Trust < trustBefore,
            "A rumour that fails inspection should cost the teller, not Overseer.");
        Assert.True(listener.Relationships[teller.Name].Resentment > 0);
    }

    [Fact]
    public void VisibleHelpReducesSuspicionForWitnessesOnly()
    {
        var state = CreateQuietStation();
        var witness = state.Crew[0];
        var absent = state.Crew[1];

        witness.CurrentRoomId = "medical";
        absent.CurrentRoomId = "engineering";

        foreach (var npc in new[] { witness, absent })
        {
            SuspicionSystem.AddEvidence(state, npc, "Overseer is behaving oddly.", 20);
        }

        var witnessBefore = witness.OverseerSuspicion;
        var absentBefore = absent.OverseerSuspicion;

        SuspicionDynamicsSystem.RecordBenignAct(
            state, "medical", "Overseer restored power to Medical.", 8);

        Assert.True(witness.OverseerSuspicion < witnessBefore);
        Assert.Equal(absentBefore, absent.OverseerSuspicion);
    }

    [Fact]
    public void ReassuranceBarelyWorksOnTheAlreadyConvinced()
    {
        var state = CreateQuietStation();
        var uneasy = state.Crew[0];
        var convinced = state.Crew[1];

        uneasy.CurrentRoomId = "medical";
        convinced.CurrentRoomId = "medical";

        SuspicionSystem.AddEvidence(state, uneasy, "A hatch closed oddly.", 15);

        for (var i = 0; i < 5; i++)
        {
            SuspicionSystem.AddEvidence(
                state, convinced, $"Overseer did something indefensible. ({i})", 20);
        }

        var uneasyBefore = uneasy.OverseerSuspicion;
        var convincedBefore = convinced.OverseerSuspicion;

        SuspicionDynamicsSystem.RecordBenignAct(
            state, "medical", "Overseer restored power to Medical.", 10);

        var uneasyDrop = uneasyBefore - uneasy.OverseerSuspicion;
        var convincedDrop = convincedBefore - convinced.OverseerSuspicion;

        Assert.True(
            uneasyDrop > convincedDrop,
            $"Expected a favour to land better on the uneasy ({uneasyDrop}) "
            + $"than the convinced ({convincedDrop}).");
    }

    [Fact]
    public void CrewNoticeAnUnexplainedFaultInTheirOwnCompartment()
    {
        var state = CreateQuietStation();
        var npc = state.Crew[0];

        npc.CurrentRoomId = "medical";
        state.Facility.Rooms["medical"].IsPowered = false;

        Advance(state, new SuspicionDynamicsSystem(), 1);

        Assert.True(npc.OverseerSuspicion > 0);
        Assert.Contains(npc.OverseerEvidence, e => e.Claim == EvidenceClaim.PowerCut);
    }

    [Fact]
    public void BlameLandsOnACrewmateWhenOneWasRecentlySeenAtTheFault()
    {
        var state = CreateQuietStation();
        var observer = state.Crew[0];
        var suspect = state.Crew[1];

        observer.CurrentRoomId = "medical";

        // The observer personally saw the suspect working in Medical, and
        // already does not think much of them.
        observer.LastSeenCrew[suspect.Id] = new CrewSighting(
            suspect.Id,
            suspect.Name,
            "medical",
            state.Elapsed);

        var relationship = observer.Relationships[suspect.Name];
        relationship.Trust = 15;
        relationship.Resentment = 35;

        state.Facility.Rooms["medical"].IsPowered = false;

        Advance(state, new SuspicionDynamicsSystem(), 1);

        Assert.Equal(0, observer.OverseerSuspicion);
        Assert.Contains(
            observer.Beliefs,
            belief => belief.Subject.Equals($"{suspect.Name} competence", StringComparison.OrdinalIgnoreCase));
        Assert.True(observer.Relationships[suspect.Name].Resentment > 35);
    }

    [Fact]
    public void ATrustedColleagueIsNotBlamedAndOverseerTakesTheSuspicionInstead()
    {
        var state = CreateQuietStation();
        var observer = state.Crew[0];
        var colleague = state.Crew[1];

        observer.CurrentRoomId = "medical";

        observer.LastSeenCrew[colleague.Id] = new CrewSighting(
            colleague.Id,
            colleague.Name,
            "medical",
            state.Elapsed);

        var relationship = observer.Relationships[colleague.Name];
        relationship.Trust = 95;
        relationship.Resentment = 0;

        state.Facility.Rooms["medical"].IsPowered = false;

        Advance(state, new SuspicionDynamicsSystem(), 1);

        Assert.True(observer.OverseerSuspicion > 0);
        Assert.DoesNotContain(
            observer.Beliefs,
            belief => belief.Subject.Equals($"{colleague.Name} competence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ARecurringFaultIsNoticedAgainAfterItHasBeenFixed()
    {
        var state = CreateQuietStation();
        var npc = state.Crew[0];
        var system = new SuspicionDynamicsSystem();

        npc.CurrentRoomId = "medical";
        var medical = state.Facility.Rooms["medical"];

        medical.IsPowered = false;
        Advance(state, system, 1);
        Assert.Contains("medical:power", npc.ObservedFaults);

        medical.IsPowered = true;
        Advance(state, system, 1);
        Assert.DoesNotContain("medical:power", npc.ObservedFaults);

        medical.IsPowered = false;
        Advance(state, system, 1);
        Assert.Contains("medical:power", npc.ObservedFaults);
    }

    [Fact]
    public void SuspicionNeverExceedsTheClampedRange()
    {
        var state = CreateQuietStation();
        var npc = state.Crew[0];

        for (var i = 0; i < 20; i++)
        {
            SuspicionSystem.AddEvidence(state, npc, $"Overseer incident {i}.", 25);
        }

        Advance(state, new SuspicionDynamicsSystem(), 1);

        Assert.InRange(npc.OverseerSuspicion, 0, 100);
    }
}
