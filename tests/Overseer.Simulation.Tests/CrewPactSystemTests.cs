using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewPactSystemTests
{
    [Fact]
    public void Create_RecordsStructuredPactWithDeterministicIdentity()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        state.Elapsed = TimeSpan.FromHours(3);

        var created = CrewPactSystem.TryCreate(
            state,
            promisor.Id,
            promisee.Id,
            CrewPactKind.CoverShift,
            "I will cover your next maintenance shift.",
            state.Elapsed + TimeSpan.FromHours(1),
            state.Elapsed + TimeSpan.FromHours(4),
            out var pact,
            out var reason);

        Assert.True(created, reason);
        Assert.NotNull(pact);
        Assert.Equal("pact-0001", pact.Id);
        Assert.Equal(CrewPactKind.CoverShift, pact.Kind);
        Assert.Equal(CrewPactStatus.Active, pact.Status);
        Assert.Equal(state.Elapsed + TimeSpan.FromHours(1), pact.TriggerAt);
        Assert.Equal(state.Elapsed + TimeSpan.FromHours(4), pact.Deadline);
        Assert.Contains(promisor.Memories, memory => memory.Description.Contains("I promised", StringComparison.Ordinal));
        Assert.Contains(promisee.Memories, memory => memory.Description.Contains("promised me", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_RejectsInvalidPartiesTimingAndDuplicates()
    {
        var state = FacilitySeeder.CreateDefault();
        var first = state.Crew[0];
        var second = state.Crew[1];
        state.Elapsed = TimeSpan.FromHours(8);

        Assert.False(CrewPactSystem.TryCreate(
            state, first.Id, first.Id, CrewPactKind.Other, "Personal reminder.",
            null, null, out _, out _));

        Assert.False(CrewPactSystem.TryCreate(
            state, first.Id, second.Id, CrewPactKind.CoverShift, "Cover the late shift.",
            state.Elapsed - TimeSpan.FromMinutes(1),
            state.Elapsed + TimeSpan.FromHours(1),
            out _, out _));

        Assert.False(CrewPactSystem.TryCreate(
            state, first.Id, second.Id, CrewPactKind.CoverShift, "Cover the late shift.",
            state.Elapsed + TimeSpan.FromHours(2),
            state.Elapsed + TimeSpan.FromHours(1),
            out _, out _));

        Assert.True(CrewPactSystem.TryCreate(
            state, first.Id, second.Id, CrewPactKind.OweFavor, "I owe you one.",
            null, null, out _, out _));

        Assert.False(CrewPactSystem.TryCreate(
            state, first.Id, second.Id, CrewPactKind.OweFavor, " i owe you one. ",
            null, null, out _, out _));
    }

    [Fact]
    public void Fulfill_ImprovesRelationshipAndCannotSettleAgain()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        var relationship = promisee.Relationships[promisor.Name];
        var trustBefore = relationship.Trust;

        CrewPactSystem.TryCreate(
            state, promisor.Id, promisee.Id, CrewPactKind.Other, "Bring a meal.",
            null, null, out var pact, out _);

        state.Elapsed += TimeSpan.FromHours(2);
        Assert.True(CrewPactSystem.TryFulfill(state, pact!.Id, "Completed.", out var reason), reason);

        Assert.Equal(CrewPactStatus.Fulfilled, pact.Status);
        Assert.True(relationship.Trust > trustBefore);
        Assert.Contains(promisee.Memories, memory => memory.Description.Contains("kept their promise", StringComparison.Ordinal));
        Assert.False(CrewPactSystem.TryBreak(state, pact.Id, null, out _));
    }

    [Fact]
    public void UnmetCommitment_CreatesRelationshipAndMemoryConsequences()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        var relationship = promisee.Relationships[promisor.Name];
        relationship.Resentment = 5;
        var trustBefore = relationship.Trust;
        var stressBefore = promisee.Stress;

        CrewPactSystem.TryCreate(
            state, promisor.Id, promisee.Id, CrewPactKind.CoverShift, "Cover the shift.",
            null, state.Elapsed + TimeSpan.FromHours(3), out var pact, out _);

        state.Elapsed += TimeSpan.FromHours(5);
        Assert.True(CrewPactSystem.TryBreak(state, pact!.Id, "Commitment not met.", out var reason), reason);

        Assert.Equal(CrewPactStatus.Broken, pact.Status);
        Assert.True(relationship.Trust < trustBefore);
        Assert.True(relationship.Resentment > 5);
        Assert.True(promisee.Stress > stressBefore);
        Assert.Contains(promisee.Memories, memory => memory.Description.Contains("broke their promise", StringComparison.Ordinal));
    }
}
