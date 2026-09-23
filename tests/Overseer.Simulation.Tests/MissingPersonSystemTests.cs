using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class MissingPersonSystemTests
{
    [Fact]
    public void RemoteIsPresentStateAlone_DoesNotCreateOmniscientConcern()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        marcus.Health = 0;
        marcus.IsPresent = false;
        marcus.CauseOfDeath = "Lost to space; no body remains aboard.";
        nadia.CurrentRoomId = "medical";
        state.Elapsed = TimeSpan.FromMinutes(360);

        new MissingPersonSystem().Tick(state);

        Assert.DoesNotContain(marcus.Id, nadia.MissingPersonConcerns.Keys);
        Assert.Empty(nadia.DiscoveredBodies);
    }

    [Fact]
    public void OrdinarySeparation_ForElevenHours_DoesNotCreateConcern()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var system = new MissingPersonSystem();

        nadia.CurrentRoomId = "medical";
        marcus.CurrentRoomId = "storage";
        state.Elapsed = TimeSpan.Zero;
        system.Tick(state);

        state.Elapsed = TimeSpan.FromHours(11);
        var expected = CrewDutySchedule.ExpectedDutyRoomId(marcus.Role, state.Elapsed);
        nadia.CurrentRoomId = expected;
        system.Tick(state);

        Assert.DoesNotContain(marcus.Id, nadia.MissingPersonConcerns.Keys);
        Assert.False(nadia.NeedsMindReconsideration);
    }

    [Fact]
    public void LongMissedExpectedDuty_CreatesConcernWithoutKnowingDeathTruth()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");

        marcus.Health = 0;
        marcus.IsPresent = false;
        marcus.CauseOfDeath = "Lost to space; no body remains aboard.";
        state.Elapsed = TimeSpan.FromHours(12);

        var expected = CrewDutySchedule.ExpectedDutyRoomId(marcus.Role, state.Elapsed);
        nadia.CurrentRoomId = expected;

        new MissingPersonSystem().Tick(state);

        var concern = Assert.Single(nadia.MissingPersonConcerns).Value;
        Assert.Equal(marcus.Id, concern.PersonId);
        Assert.Equal(expected, concern.ExpectedRoomId);
        Assert.Equal(MissingPersonConcernStage.Concerned, concern.Stage);
        Assert.Equal(0, nadia.OverseerSuspicion);
        Assert.DoesNotContain(
            nadia.Memories,
            memory => memory.Description.Contains("space", StringComparison.OrdinalIgnoreCase));
        Assert.False(nadia.NeedsMindReconsideration);
    }

    [Fact]
    public void PhysicalSearchOfLikelyRooms_EscalatesConcern()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var system = new MissingPersonSystem();

        var expectedAtConcern = CrewDutySchedule.ExpectedDutyRoomId(
            marcus.Role,
            TimeSpan.FromHours(12));
        var lastSeenRoom = state.Facility.Rooms.Keys.First(id =>
            !id.Equals(expectedAtConcern, StringComparison.OrdinalIgnoreCase));

        nadia.CurrentRoomId = lastSeenRoom;
        marcus.CurrentRoomId = lastSeenRoom;
        // Record a direct sighting at the start of the shift. Ordinary separation
        // is allowed for hours before a missed-duty absence becomes notable.
        state.Elapsed = TimeSpan.Zero;
        system.Tick(state);

        marcus.Health = 0;
        marcus.IsPresent = false;
        marcus.CauseOfDeath = "Lost to space; no body remains aboard.";

        state.Elapsed = TimeSpan.FromHours(12);
        var expected = CrewDutySchedule.ExpectedDutyRoomId(marcus.Role, state.Elapsed);
        nadia.CurrentRoomId = expected;
        system.Tick(state);

        state.Elapsed = TimeSpan.FromMinutes(725);
        system.Tick(state);

        var concern = nadia.MissingPersonConcerns[marcus.Id];
        Assert.Equal(MissingPersonConcernStage.Searching, concern.Stage);
        Assert.Contains(expected, concern.CheckedRoomIds);

        nadia.CurrentRoomId = lastSeenRoom;
        state.Elapsed = TimeSpan.FromMinutes(730);
        system.Tick(state);

        Assert.Equal(MissingPersonConcernStage.Escalated, concern.Stage);
        Assert.Contains(lastSeenRoom, concern.CheckedRoomIds);
        Assert.True(concern.CheckedRoomIds.Count >= 2);
    }

    [Fact]
    public void DirectBloodEvidenceAllowsEarlierMissingPersonConcern()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var system = new MissingPersonSystem();

        nadia.CurrentRoomId = "medical";
        marcus.CurrentRoomId = "medical";
        state.Elapsed = TimeSpan.Zero;
        system.Tick(state);

        marcus.CurrentRoomId = "storage";
        var blood = new BloodEvidence(
            "blood:marcus:test",
            marcus.Id,
            "medical",
            50,
            50,
            80,
            TimeSpan.FromMinutes(5));
        state.BloodEvidence.Add(blood);
        nadia.ObservedBloodEvidenceIds.Add(blood.Id);

        state.Elapsed = TimeSpan.FromMinutes(35);
        nadia.CurrentRoomId = CrewDutySchedule.ExpectedDutyRoomId(
            marcus.Role,
            state.Elapsed);

        system.Tick(state);

        Assert.Contains(marcus.Id, nadia.MissingPersonConcerns.Keys);
    }

    [Fact]
    public void ConcernSpreadsOnlyThroughCoLocatedTrustedCommunication()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var system = new MissingPersonSystem();

        marcus.Health = 0;
        marcus.IsPresent = false;
        state.Elapsed = TimeSpan.FromHours(5);

        nadia.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            ExpectedRoomId = "quarters",
            FirstConcernAt = state.Elapsed - TimeSpan.FromHours(1),
            LastUpdatedAt = state.Elapsed,
            Stage = MissingPersonConcernStage.Searching
        };

        nadia.CurrentRoomId = "medical";
        sarah.CurrentRoomId = "control";
        system.Tick(state);

        Assert.DoesNotContain(marcus.Id, sarah.MissingPersonConcerns.Keys);

        nadia.CurrentRoomId = "control";
        state.Elapsed += TimeSpan.FromMinutes(5);
        system.Tick(state);

        var shared = Assert.Single(
            sarah.MissingPersonConcerns,
            pair => pair.Key == marcus.Id).Value;
        Assert.Equal(nadia.Name, shared.SourceNpcName);
    }

    [Fact]
    public void FindingLivingCrewMember_ResolvesConcern()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var system = new MissingPersonSystem();

        nadia.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            ExpectedRoomId = "quarters",
            FirstConcernAt = TimeSpan.FromMinutes(30),
            LastUpdatedAt = TimeSpan.FromMinutes(30),
            Stage = MissingPersonConcernStage.Searching
        };

        nadia.CurrentRoomId = "medical";
        marcus.CurrentRoomId = "medical";
        state.Elapsed = TimeSpan.FromMinutes(31);

        system.Tick(state);

        Assert.DoesNotContain(marcus.Id, nadia.MissingPersonConcerns.Keys);
        Assert.Contains(
            nadia.Memories,
            memory => memory.Description.Contains(
                "Found Marcus Reed safe",
                StringComparison.OrdinalIgnoreCase));
        Assert.True(nadia.NeedsMindReconsideration);
    }

    [Fact]
    public void MissingAfterWitnessedAirlockEvent_AddsOnlyContextualSuspicion()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var system = new MissingPersonSystem();

        state.Elapsed = TimeSpan.FromMinutes(20);
        SuspicionSystem.AddEvidence(
            state,
            nadia,
            "I witnessed the exterior airlock hatch open while the station was occupied.",
            22);

        nadia.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            LastKnownRoomId = "storage",
            LastSeenAt = TimeSpan.FromMinutes(10),
            ExpectedRoomId = "quarters",
            FirstConcernAt = TimeSpan.FromMinutes(20),
            LastUpdatedAt = TimeSpan.FromMinutes(20),
            Stage = MissingPersonConcernStage.Searching
        };

        marcus.Health = 0;
        marcus.IsPresent = false;
        nadia.CurrentRoomId = "quarters";
        state.Elapsed = TimeSpan.FromMinutes(25);
        system.Tick(state);
        nadia.CurrentRoomId = "storage";
        state.Elapsed = TimeSpan.FromMinutes(30);
        system.Tick(state);

        Assert.Contains(
            nadia.OverseerEvidence,
            evidence => evidence.Description.Contains(
                "Marcus Reed is missing",
                StringComparison.OrdinalIgnoreCase));
        Assert.True(nadia.OverseerSuspicion > 22);
    }

    [Fact]
    public void ResolveAsk_AskedOfHasANewerSighting_UpdatesAskersBeliefAndConcern()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        state.Elapsed = TimeSpan.FromHours(4);
        nadia.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            LastKnownRoomId = "storage",
            LastSeenAt = TimeSpan.FromHours(1),
            ExpectedRoomId = "reactor",
            FirstConcernAt = TimeSpan.FromHours(2),
            LastUpdatedAt = TimeSpan.FromHours(2),
            Stage = MissingPersonConcernStage.Concerned
        };
        emma.LastSeenCrew[marcus.Id] = new CrewSighting(
            marcus.Id,
            marcus.Name,
            "reactor",
            TimeSpan.FromHours(3));

        MissingPersonSystem.ResolveAsk(state, nadia, emma);

        var concern = nadia.MissingPersonConcerns[marcus.Id];
        Assert.Equal("reactor", concern.LastKnownRoomId);
        Assert.Equal(TimeSpan.FromHours(3), concern.LastSeenAt);
        Assert.True(nadia.NeedsMindReconsideration);
        Assert.Contains(
            nadia.Beliefs,
            belief => belief.Statement.Contains("Emma Voss told me", StringComparison.OrdinalIgnoreCase)
                && belief.Statement.Contains(marcus.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            nadia.Memories,
            memory => memory.Description.Contains("Emma Voss told me", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveAsk_AskedOfKnowsNothingNewer_LeavesTheConcernAndBeliefUnchanged()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        state.Elapsed = TimeSpan.FromHours(4);
        nadia.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            LastKnownRoomId = "storage",
            LastSeenAt = TimeSpan.FromHours(3),
            ExpectedRoomId = "reactor",
            FirstConcernAt = TimeSpan.FromHours(2),
            LastUpdatedAt = TimeSpan.FromHours(2),
            Stage = MissingPersonConcernStage.Concerned
        };
        // Emma has no sighting at all of Marcus, so she has nothing to add.
        Assert.False(emma.LastSeenCrew.ContainsKey(marcus.Id));

        MissingPersonSystem.ResolveAsk(state, nadia, emma);

        var concern = nadia.MissingPersonConcerns[marcus.Id];
        Assert.Equal("storage", concern.LastKnownRoomId);
        Assert.Equal(TimeSpan.FromHours(3), concern.LastSeenAt);
        Assert.False(nadia.NeedsMindReconsideration);
        Assert.DoesNotContain(
            nadia.Beliefs,
            belief => belief.Statement.Contains("Emma Voss told me", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            emma.Memories,
            memory => memory.Description.Contains("asked me if I had seen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveAsk_WithACapturedSubject_ResolvesThatConcernEvenIfADifferentOneBecomesMorePressingMeanwhile()
    {
        // Regression: without a captured subject, ResolveAsk re-derives "the
        // most pressing concern" at resolution time, which can silently
        // differ from the concern that was most pressing (and thus intended)
        // when the ask was originally decided -- e.g. after the asker spends
        // several ticks walking to reach the askee.
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var david = state.Crew.Single(npc => npc.Name == "David Hale");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");

        state.Elapsed = TimeSpan.FromHours(4);
        nadia.MissingPersonConcerns[marcus.Id] = new MissingPersonConcern
        {
            PersonId = marcus.Id,
            PersonName = marcus.Name,
            ExpectedRoomId = "reactor",
            FirstConcernAt = TimeSpan.FromHours(1),
            LastUpdatedAt = TimeSpan.FromHours(1),
            Stage = MissingPersonConcernStage.Searching
        };
        nadia.MissingPersonConcerns[david.Id] = new MissingPersonConcern
        {
            PersonId = david.Id,
            PersonName = david.Name,
            ExpectedRoomId = "hydroponics",
            FirstConcernAt = TimeSpan.FromHours(3),
            LastUpdatedAt = TimeSpan.FromHours(3),
            Stage = MissingPersonConcernStage.Concerned
        };

        // Marcus is most pressing when the ask is decided; this is what gets
        // captured as the subject at that moment.
        var subjectId = MissingPersonSystem.MostPressingAskableConcern(nadia)?.PersonId;
        Assert.Equal(marcus.Id, subjectId);

        // While the asker is still travelling to reach Emma, David's concern
        // escalates past Marcus's.
        nadia.MissingPersonConcerns[david.Id].Stage = MissingPersonConcernStage.Escalated;
        Assert.Equal(david.Id, MissingPersonSystem.MostPressingAskableConcern(nadia)?.PersonId);

        emma.LastSeenCrew[marcus.Id] = new CrewSighting(marcus.Id, marcus.Name, "reactor", TimeSpan.FromHours(3.5));
        emma.LastSeenCrew[david.Id] = new CrewSighting(david.Id, david.Name, "hydroponics", TimeSpan.FromHours(3.5));

        MissingPersonSystem.ResolveAsk(state, nadia, emma, subjectId);

        Assert.Equal("reactor", nadia.MissingPersonConcerns[marcus.Id].LastKnownRoomId);
        Assert.Null(nadia.MissingPersonConcerns[david.Id].LastKnownRoomId);
        Assert.Contains(
            nadia.Beliefs,
            belief => belief.Statement.Contains(marcus.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveAsk_AskerHasNoActiveConcern_DoesNothing()
    {
        var state = FacilitySeeder.CreateDefault();
        var nadia = state.Crew.Single(npc => npc.Name == "Nadia Okafor");
        var emma = state.Crew.Single(npc => npc.Name == "Emma Voss");
        var memoriesBefore = emma.Memories.Count;
        var beliefsBefore = nadia.Beliefs.Count;

        MissingPersonSystem.ResolveAsk(state, nadia, emma);

        Assert.Equal(memoriesBefore, emma.Memories.Count);
        Assert.Equal(beliefsBefore, nadia.Beliefs.Count);
    }
}
