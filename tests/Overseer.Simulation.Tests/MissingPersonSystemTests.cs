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

        nadia.CurrentRoomId = "storage";
        marcus.CurrentRoomId = "storage";
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

        nadia.CurrentRoomId = "storage";
        state.Elapsed = TimeSpan.FromMinutes(730);
        system.Tick(state);

        Assert.Equal(MissingPersonConcernStage.Escalated, concern.Stage);
        Assert.Contains("storage", concern.CheckedRoomIds);
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
}
