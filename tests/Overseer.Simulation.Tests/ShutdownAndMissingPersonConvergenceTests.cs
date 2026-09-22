using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Regression coverage for the shared shutdown-coordination and
/// missing-person decision helpers, which <c>RuleBasedAiDecisionService</c>
/// and <c>BrowserMindSystem</c> both used to reimplement byte-for-byte. Both
/// now delegate to <see cref="ShutdownCoordinationSystem"/> and
/// <see cref="MissingPersonSystem"/>; this exercises those shared decisions
/// directly.
/// </summary>
public sealed class ShutdownAndMissingPersonConvergenceTests
{
    [Fact]
    public void ShouldJoinTeam_RequiresBothSuspicionAndTrust()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var invitation = new ShutdownTeamInvitation(
            "team:1", "mech:1", "Sarah Chen", "control", state.Elapsed);

        npc.OverseerSuspicion = 49;
        npc.Relationships["Sarah Chen"] = new Relationship { PersonName = "Sarah Chen", Trust = 90 };
        Assert.False(ShutdownCoordinationSystem.ShouldJoinTeam(npc, invitation));

        npc.OverseerSuspicion = 50;
        npc.Relationships["Sarah Chen"].Trust = 34;
        Assert.False(ShutdownCoordinationSystem.ShouldJoinTeam(npc, invitation));

        npc.Relationships["Sarah Chen"].Trust = 35;
        Assert.True(ShutdownCoordinationSystem.ShouldJoinTeam(npc, invitation));
    }

    [Fact]
    public void FindKnownMechanism_OnlyReturnsOnlineMechanismsThePersonPersonallyVerified()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var offline = new ShutdownMechanism { Id = "mech-a", RoomId = "control", IsOnline = false };
        var unknown = new ShutdownMechanism { Id = "mech-b", RoomId = "engineering" };
        var known = new ShutdownMechanism { Id = "mech-c", RoomId = "medical" };
        state.ShutdownMechanisms.AddRange([offline, unknown, known]);
        npc.KnownShutdownMechanismIds.Add(offline.Id);
        npc.KnownShutdownMechanismIds.Add(known.Id);

        var result = ShutdownCoordinationSystem.FindKnownMechanism(state, npc);

        Assert.Same(known, result);
    }

    [Fact]
    public void FindRecruit_PrefersMostTrustedEligibleCandidate()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var lowTrust = state.Crew[1];
        var highTrust = state.Crew[2];
        npc.Relationships[lowTrust.Name] = new Relationship { PersonName = lowTrust.Name, Trust = 20, Affinity = 20 };
        npc.Relationships[highTrust.Name] = new Relationship { PersonName = highTrust.Name, Trust = 90, Affinity = 90 };

        var recruit = ShutdownCoordinationSystem.FindRecruit(state, npc, team: null);

        Assert.Same(highTrust, recruit);
    }

    [Fact]
    public void MostPressingConcern_SkipsMerelyConcernedAndPrefersMostEscalated()
    {
        var npc = FacilitySeeder.CreateDefault().Crew[0];
        var justConcerned = new MissingPersonConcern
        {
            PersonId = Guid.NewGuid(),
            PersonName = "Just Concerned",
            ExpectedRoomId = "control",
            FirstConcernAt = TimeSpan.FromMinutes(1),
            Stage = MissingPersonConcernStage.Concerned
        };
        var searching = new MissingPersonConcern
        {
            PersonId = Guid.NewGuid(),
            PersonName = "Searching Subject",
            ExpectedRoomId = "control",
            FirstConcernAt = TimeSpan.FromMinutes(2),
            Stage = MissingPersonConcernStage.Searching
        };
        var escalated = new MissingPersonConcern
        {
            PersonId = Guid.NewGuid(),
            PersonName = "Escalated Subject",
            ExpectedRoomId = "control",
            FirstConcernAt = TimeSpan.FromMinutes(3),
            Stage = MissingPersonConcernStage.Escalated
        };
        npc.MissingPersonConcerns[justConcerned.PersonId] = justConcerned;
        npc.MissingPersonConcerns[searching.PersonId] = searching;
        npc.MissingPersonConcerns[escalated.PersonId] = escalated;

        var result = MissingPersonSystem.MostPressingConcern(npc);

        Assert.Same(escalated, result);
    }

    [Fact]
    public void ReasonFor_MentionsLastSeenTimeWhenKnown()
    {
        var state = FacilitySeeder.CreateDefault();
        var concern = new MissingPersonConcern
        {
            PersonId = Guid.NewGuid(),
            PersonName = "Marcus Reed",
            ExpectedRoomId = "control",
            FirstConcernAt = TimeSpan.FromHours(1),
            LastSeenAt = TimeSpan.FromMinutes(90)
        };

        var reason = MissingPersonSystem.ReasonFor(state, concern);

        Assert.Contains("I last saw Marcus Reed", reason);
    }

    [Fact]
    public void ReasonFor_FallsBackToUnseenThisShiftWhenNeverSighted()
    {
        var state = FacilitySeeder.CreateDefault();
        var concern = new MissingPersonConcern
        {
            PersonId = Guid.NewGuid(),
            PersonName = "Marcus Reed",
            ExpectedRoomId = "control",
            FirstConcernAt = TimeSpan.FromHours(1)
        };

        var reason = MissingPersonSystem.ReasonFor(state, concern);

        Assert.Contains("I have not seen Marcus Reed this shift", reason);
    }
}
