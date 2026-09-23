using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Coverage for the propose/accept cognition wiring around
/// <see cref="CrewPactSystem"/>: a ProposePact CurrentAction becomes a
/// <see cref="PactProposal"/> on the target, and an AcceptPact CurrentAction
/// turns a matching proposal into a real <see cref="CrewPact"/>.
/// </summary>
public sealed class PactCoordinationSystemTests
{
    [Fact]
    public void Propose_CreatesAPendingProposalOnTheCoLocatedTarget()
    {
        var state = FacilitySeeder.CreateDefault();
        var proposer = state.Crew[0];
        var target = state.Crew[1];
        target.CurrentRoomId = proposer.CurrentRoomId;

        proposer.CurrentAction = new NpcAction(
            ActionKind.ProposePact,
            target.Name,
            "I'll cover your next shift.");

        new PactCoordinationSystem().Tick(state);

        var proposal = target.PendingPactProposal;
        Assert.NotNull(proposal);
        Assert.Equal(proposer.Id, proposal!.FromNpcId);
        Assert.Equal(proposer.Name, proposal.FromNpcName);
        Assert.Equal("I'll cover your next shift.", proposal.PromiseText);
        Assert.Empty(state.CrewPacts);
        Assert.Equal(ActionKind.Idle, proposer.CurrentAction.Kind);
        Assert.Contains(target.Memories, memory => memory.Description.Contains("proposed", StringComparison.Ordinal));
    }

    [Fact]
    public void Propose_ClearsActionWithoutAProposalWhenTargetIsNotCoLocated()
    {
        var state = FacilitySeeder.CreateDefault();
        var proposer = state.Crew[0];
        var target = state.Crew[1];
        target.CurrentRoomId = "storage";
        proposer.CurrentRoomId = "control";

        proposer.CurrentAction = new NpcAction(
            ActionKind.ProposePact,
            target.Name,
            "I'll cover your next shift.");

        new PactCoordinationSystem().Tick(state);

        Assert.Null(target.PendingPactProposal);
        Assert.Equal(ActionKind.Idle, proposer.CurrentAction.Kind);
    }

    [Fact]
    public void Accept_CreatesTheRealPactAndClearsTheProposal()
    {
        var state = FacilitySeeder.CreateDefault();
        var proposer = state.Crew[0];
        var accepter = state.Crew[1];

        accepter.PendingPactProposal = new PactProposal(
            proposer.Id,
            proposer.Name,
            CrewPactKind.Other,
            "I'll cover your next shift.",
            TriggerAt: null,
            Deadline: null,
            OfferedAt: state.Elapsed);

        accepter.CurrentAction = new NpcAction(
            ActionKind.AcceptPact,
            proposer.Name,
            "I agree.");

        new PactCoordinationSystem().Tick(state);

        Assert.Null(accepter.PendingPactProposal);
        var pact = Assert.Single(state.CrewPacts);
        Assert.Equal(proposer.Id, pact.PromisorId);
        Assert.Equal(accepter.Id, pact.PromiseeId);
        Assert.Equal("I'll cover your next shift.", pact.PromiseText);
        Assert.Equal(CrewPactStatus.Active, pact.Status);
        Assert.Equal(ActionKind.Idle, accepter.CurrentAction.Kind);
    }

    [Fact]
    public void Accept_ClearsActionWithoutCreatingAPactWhenTheresNoMatchingProposal()
    {
        var state = FacilitySeeder.CreateDefault();
        var accepter = state.Crew[1];

        accepter.CurrentAction = new NpcAction(
            ActionKind.AcceptPact,
            "Someone Who Never Asked",
            "I agree.");

        new PactCoordinationSystem().Tick(state);

        Assert.Empty(state.CrewPacts);
        Assert.Equal(ActionKind.Idle, accepter.CurrentAction.Kind);
    }

    [Fact]
    public void Fulfill_SettlesTheActivePactAndAppliesTrustConsequences()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        Assert.True(CrewPactSystem.TryCreate(
            state, promisor.Id, promisee.Id, CrewPactKind.Other,
            "I'll cover your next shift.", null, null, out var pact, out _));

        promisor.CurrentAction = new NpcAction(
            ActionKind.FulfillPact,
            pact!.Id,
            "I said I would, so I will.");

        new PactCoordinationSystem().Tick(state);

        Assert.Equal(CrewPactStatus.Fulfilled, pact.Status);
        Assert.Equal(ActionKind.Idle, promisor.CurrentAction.Kind);
        Assert.True(promisor.NeedsMindReconsideration);
        Assert.Contains(promisee.Memories, memory => memory.Description.Contains("kept their promise", StringComparison.Ordinal));
    }

    [Fact]
    public void Break_SettlesTheActivePactAndAppliesResentmentConsequences()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        Assert.True(CrewPactSystem.TryCreate(
            state, promisor.Id, promisee.Id, CrewPactKind.Other,
            "I'll cover your next shift.", null, null, out var pact, out _));

        promisor.CurrentAction = new NpcAction(
            ActionKind.BreakPact,
            pact!.Id,
            "Something more important came up.");

        new PactCoordinationSystem().Tick(state);

        Assert.Equal(CrewPactStatus.Broken, pact.Status);
        Assert.Equal(ActionKind.Idle, promisor.CurrentAction.Kind);
        Assert.Contains(promisee.Memories, memory => memory.Description.Contains("broke their promise", StringComparison.Ordinal));
    }

    [Fact]
    public void Fulfill_ClearsActionWithoutSettlingWhenTheNpcIsNotThePromisor()
    {
        var state = FacilitySeeder.CreateDefault();
        var promisor = state.Crew[0];
        var promisee = state.Crew[1];
        Assert.True(CrewPactSystem.TryCreate(
            state, promisor.Id, promisee.Id, CrewPactKind.Other,
            "I'll cover your next shift.", null, null, out var pact, out _));

        promisee.CurrentAction = new NpcAction(
            ActionKind.FulfillPact,
            pact!.Id,
            "Let's call it settled.");

        new PactCoordinationSystem().Tick(state);

        Assert.Equal(CrewPactStatus.Active, pact.Status);
        Assert.Equal(ActionKind.Idle, promisee.CurrentAction.Kind);
    }

    [Fact]
    public void PendingProposal_ExpiresAfterFifteenMinutesUnanswered()
    {
        var state = FacilitySeeder.CreateDefault();
        var proposer = state.Crew[0];
        var target = state.Crew[1];

        target.PendingPactProposal = new PactProposal(
            proposer.Id,
            proposer.Name,
            CrewPactKind.Other,
            "I'll cover your next shift.",
            TriggerAt: null,
            Deadline: null,
            OfferedAt: state.Elapsed);

        state.Elapsed += TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1);
        new PactCoordinationSystem().Tick(state);

        Assert.Null(target.PendingPactProposal);
    }
}
