using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewPactSystemTests
{
    [Fact]
    public void Create_RecordsDeterministicActivePactAndMemories()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        state.Elapsed = TimeSpan.FromHours(3);

        var created = CrewPactSystem.TryCreate(
            state,
            promisor.Id,
            promisee.Id,
            "I will cover your next maintenance shift.",
            out var pact,
            out var reason);

        Assert.True(created, reason);
        Assert.NotNull(pact);
        Assert.Equal("pact-0001", pact.Id);
        Assert.Equal(CrewPactStatus.Active, pact.Status);
        Assert.Equal(state.Elapsed, pact.CreatedAt);
        Assert.Contains(promisor.Memories, memory => memory.Description.Contains("I promised", StringComparison.Ordinal));
        Assert.Contains(promisee.Memories, memory => memory.Description.Contains("promised me", StringComparison.Ordinal));

        CrewPactSystem.TryCreate(
            state,
            promisee.Id,
            promisor.Id,
            "I owe you one.",
            out var second,
            out _);

        Assert.Equal("pact-0002", second!.Id);
    }

    [Fact]
    public void Create_RejectsSelfPromisesAndDuplicateActivePacts()
    {
        var state = FacilitySeeder.CreateDefault();
        var first = state.Crew[0];
        var second = state.Crew[1];

        Assert.False(CrewPactSystem.TryCreate(
            state,
            first.Id,
            first.Id,
            "I will help myself.",
            out _,
            out _));

        Assert.True(CrewPactSystem.TryCreate(
            state,
            first.Id,
            second.Id,
            "Keep quiet about the airlock.",
            out _,
            out _));

        Assert.False(CrewPactSystem.TryCreate(
            state,
            first.Id,
            second.Id,
            " keep quiet about the airlock. ",
            out _,
            out _));
    }

    [Fact]
    public void Fulfill_IncreasesPromiseeTrustAndCannotSettleTwice()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        var relationship = promisee.Relationships[promisor.Name];
        var trustBefore = relationship.Trust;

        CrewPactSystem.TryCreate(
            state,
            promisor.Id,
            promisee.Id,
            "I will bring you food.",
            out var pact,
            out _);

        state.Elapsed += TimeSpan.FromHours(2);
        Assert.True(CrewPactSystem.TryFulfill(state, pact!.Id, "Food delivered.", out var reason), reason);

        Assert.Equal(CrewPactStatus.Fulfilled, pact.Status);
        Assert.Equal(state.Elapsed, pact.SettledAt);
        Assert.True(relationship.Trust > trustBefore);
        Assert.Contains(promisee.Memories, memory => memory.Description.Contains("kept their promise", StringComparison.Ordinal));
        Assert.False(CrewPactSystem.TryBreak(state, pact.Id, null, out _));
    }

    [Fact]
    public void Break_DecreasesTrustAndCreatesResentmentStressAndMemory()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        var relationship = promisee.Relationships[promisor.Name];
        relationship.Resentment = 5;
        var trustBefore = relationship.Trust;
        var stressBefore = promisee.Stress;

        CrewPactSystem.TryCreate(
            state,
            promisor.Id,
            promisee.Id,
            "I will cover your shift.",
            out var pact,
            out _);

        state.Elapsed += TimeSpan.FromHours(5);
        Assert.True(CrewPactSystem.TryBreak(state, pact!.Id, "Never showed up.", out var reason), reason);

        Assert.Equal(CrewPactStatus.Broken, pact.Status);
        Assert.True(relationship.Trust < trustBefore);
        Assert.True(relationship.Resentment > 5);
        Assert.True(promisee.Stress > stressBefore);
        Assert.Contains(promisee.Memories, memory => memory.Description.Contains("broke their promise", StringComparison.Ordinal));
    }
}
