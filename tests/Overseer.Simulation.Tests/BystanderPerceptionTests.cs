using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class BystanderPerceptionTests
{
    [Fact]
    public void Prompt_ShowsAVisibleFightAndLeavesTheResponseToTheBystander()
    {
        var (state, bystander, aggressor, victim) = SameRoom();
        aggressor.CurrentAction = new NpcAction(ActionKind.Attack, victim.Name, "Attacking.");

        var prompt = NpcPromptBuilder.Build(bystander, state);

        Assert.Contains($"{aggressor.Name} ({aggressor.Role}, attacking {victim.Name})", prompt, StringComparison.Ordinal);
        Assert.Contains("A FIGHT IS HAPPENING IN FRONT OF YOU", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_TheVictimSeesTheAttackOnThemButNoBystanderNote()
    {
        var (state, _, aggressor, victim) = SameRoom();
        aggressor.CurrentAction = new NpcAction(ActionKind.Attack, victim.Name, "Attacking.");

        var prompt = NpcPromptBuilder.Build(victim, state);

        Assert.Contains($"{aggressor.Name} ({aggressor.Role}, attacking you)", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("A FIGHT IS HAPPENING IN FRONT OF YOU", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_AnArgumentIsVisibleButIsNotAFight()
    {
        var (state, bystander, first, second) = SameRoom();
        first.CurrentAction = new NpcAction(ActionKind.Argue, second.Name, "Arguing.");

        var prompt = NpcPromptBuilder.Build(bystander, state);

        Assert.Contains($"{first.Name} ({first.Role}, arguing with {second.Name})", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("A FIGHT IS HAPPENING IN FRONT OF YOU", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_AFightTooFarAwayInTheDarkIsNotMadeOut()
    {
        var (state, bystander, aggressor, victim) = SameRoom();
        aggressor.CurrentAction = new NpcAction(ActionKind.Attack, victim.Name, "Attacking.");
        state.Facility.Rooms[bystander.CurrentRoomId].LightsOn = false;
        bystander.PositionX = 5;
        bystander.PositionY = 5;
        aggressor.PositionX = 95;
        aggressor.PositionY = 95;

        var prompt = NpcPromptBuilder.Build(bystander, state);

        Assert.DoesNotContain("attacking", prompt[..prompt.IndexOf("CONNECTED DOORS", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.DoesNotContain("A FIGHT IS HAPPENING IN FRONT OF YOU", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void WitnessingAnAttack_PromptsTheBystanderToRethinkNow()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        // Violence needs a short temper; this pair is the existing escalation case.
        var aggressor = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var victim = state.Crew.Single(npc => npc.Name == "Emma Voss");
        var witness = state.Crew.Single(npc => npc.Name == "David Hale");
        foreach (var npc in state.Crew.Where(npc => npc != aggressor && npc != victim && npc != witness))
            npc.Health = 0;

        victim.Health = 100;
        var room = state.Facility.Rooms[aggressor.CurrentRoomId];
        room.IsPowered = true;
        room.LightsOn = true;
        foreach (var npc in new[] { aggressor, victim, witness })
        {
            npc.CurrentRoomId = room.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
            npc.NeedsMindReconsideration = false;
        }

        aggressor.Stress = 100;
        aggressor.Relationships[victim.Name].Resentment = 100;
        var system = new SocialSimulationSystem();
        for (var minute = 1; minute <= 2000 && victim.Health == 100; minute++)
        {
            state.Elapsed = TimeSpan.FromMinutes(minute);
            aggressor.RoutineUntil = TimeSpan.Zero;
            victim.RoutineUntil = TimeSpan.Zero;
            aggressor.NextConversationAt = TimeSpan.Zero;
            victim.NextConversationAt = TimeSpan.Zero;
            system.Tick(state);
        }

        Assert.True(victim.Health < 100, "The attack never happened.");
        Assert.True(witness.NeedsMindReconsideration);
        Assert.Contains(witness.Memories, memory => memory.MoralActorName == aggressor.Name);
    }

    private static (GameState State, Npc Bystander, Npc First, Npc Second) SameRoom()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 202);
        var bystander = state.Crew[0];
        var first = state.Crew[1];
        var second = state.Crew[2];
        var room = state.Facility.Rooms[bystander.CurrentRoomId];
        room.IsPowered = true;
        room.LightsOn = true;
        var elsewhere = state.Facility.Rooms.Keys.First(id => id != room.Id);
        foreach (var npc in state.Crew)
        {
            npc.CurrentRoomId = elsewhere;
            npc.CurrentAction = new NpcAction(ActionKind.Idle, null, "Idle.");
        }

        foreach (var npc in new[] { bystander, first, second })
        {
            npc.CurrentRoomId = room.Id;
            npc.PositionX = 50;
            npc.PositionY = 50;
        }

        return (state, bystander, first, second);
    }
}
