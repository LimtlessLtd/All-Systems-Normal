using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #91: each crew member keeps a bounded log of what changed their
/// core stats, recorded at the deterministic consequence sites.
/// </summary>
public sealed class StatLogTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void ContinuousMetabolism_IsOneEntryThatAccumulates()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.CurrentAction = new NpcAction(ActionKind.Work, null, "Working.");
        var engine = new SimulationEngine();
        var hungerBefore = npc.Hunger;

        for (var i = 0; i < 30; i++)
        {
            npc.CurrentAction = new NpcAction(ActionKind.Work, null, "Working.");
            engine.Tick(state, Minute);
        }

        var metabolism = Assert.Single(npc.StatLog, entry => entry.Stat == CrewStat.Hunger);
        Assert.Equal("metabolism", metabolism.Cause);
        Assert.Equal(npc.Hunger - hungerBefore, metabolism.Delta, 6);
        Assert.Equal(TimeSpan.FromMinutes(1), metabolism.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(30), metabolism.LastAt);
    }

    [Fact]
    public void EatingSplitsMetabolismIntoSeparateEvents()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        npc.Hunger = 60;
        state.Stores.Meals = 50;
        var engine = new SimulationEngine();

        Run(engine, state, npc, ActionKind.Work, 10);
        Run(engine, state, npc, ActionKind.Eat, 10);
        Run(engine, state, npc, ActionKind.Work, 10);

        var hunger = npc.StatLog.Where(entry => entry.Stat == CrewStat.Hunger).ToList();
        Assert.Equal(["metabolism", "eating a prepared meal", "metabolism"], hunger.Select(entry => entry.Cause));
        Assert.True(hunger[1].Delta < -15, $"meal delta {hunger[1].Delta}");
        Assert.All(hunger.Where(entry => entry.Cause == "metabolism"), entry => Assert.True(entry.Delta > 0));
    }

    [Fact]
    public void RecordingNeverChangesTheSimulationArithmetic()
    {
        // The log must be observational: the same run with the log cleared
        // every tick ends in exactly the same stats.
        var logged = Run(clearLog: false);
        var cleared = Run(clearLog: true);

        Assert.Equal(cleared, logged);

        static (double, double, double, double, double) Run(bool clearLog)
        {
            var state = FacilitySeeder.CreateDefault(upkeepSeed: 1);
            var npc = state.Crew[0];
            var room = state.Facility.Rooms[npc.CurrentRoomId];
            room.OxygenPercent = 16;
            room.CarbonDioxidePercent = 3.5;
            room.LightsOn = false;
            room.SmokePercent = 60;
            npc.Hunger = 97;
            var engine = new SimulationEngine();
            var hazards = new StationHazardSystem();
            for (var i = 0; i < 20; i++)
            {
                engine.Tick(state, Minute);
                hazards.Tick(state, Minute);
                if (clearLog)
                {
                    npc.StatLog.Clear();
                }
            }

            return (npc.Health, npc.Stress, npc.Fear, npc.Hunger, npc.Fatigue);
        }
    }

    [Fact]
    public void EnvironmentalHarm_IsAttributedToItsCause()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        room.OxygenPercent = 15;
        room.LightsOn = false;
        npc.Stress = 40;

        new SimulationEngine().Tick(state, Minute);

        var health = Assert.Single(npc.StatLog, entry => entry.Stat == CrewStat.Health);
        Assert.Equal("oxygen deprivation", health.Cause);
        Assert.Equal(-(17 - 15) * 0.12, health.Delta, 6);
        Assert.Contains(npc.StatLog, entry => entry.Stat == CrewStat.Stress && entry.Cause == "low oxygen" && entry.Delta > 0);
        Assert.Contains(npc.StatLog, entry => entry.Stat == CrewStat.Stress && entry.Cause == "lights off" && entry.Delta > 0);
        Assert.Contains(npc.StatLog, entry => entry.Stat == CrewStat.Fear && entry.Cause == "low oxygen");

        // The stress parts add up to the real change.
        var stressTotal = npc.StatLog.Where(entry => entry.Stat == CrewStat.Stress).Sum(entry => entry.Delta);
        Assert.Equal(npc.Stress - 40, stressTotal, 6);
    }

    [Fact]
    public void ClampedStress_LogsTheRealChangeUnderTheDominantCause()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        state.Facility.Rooms[npc.CurrentRoomId].OxygenPercent = 18;
        npc.Stress = 99.5;

        new SimulationEngine().Tick(state, Minute);

        Assert.Equal(100, npc.Stress);
        var stress = Assert.Single(npc.StatLog, entry => entry.Stat == CrewStat.Stress);
        Assert.Equal("low oxygen", stress.Cause);
        Assert.Equal(0.5, stress.Delta, 6);
    }

    [Fact]
    public void DescribeShowsTheStatSignedChangeAndCause()
    {
        var state = FacilitySeeder.CreateDefault();
        var victim = state.Crew[0];
        var aggressor = state.Crew[1];

        StatLogSystem.Set(state, victim, CrewStat.Health, victim.Health - 12, $"attacked by {aggressor.Name}");
        StatLogSystem.Set(state, aggressor, CrewStat.Stress, aggressor.Stress + 8, $"attacking {victim.Name}");

        var hit = Assert.Single(victim.StatLog);
        Assert.Equal(-12, hit.Delta, 6);
        Assert.False(StatLogSystem.IsImprovement(hit));
        Assert.Contains($"HEALTH -12.0 · attacked by {aggressor.Name}", StatLogSystem.Describe(hit));
        Assert.Single(aggressor.StatLog);
    }

    [Fact]
    public void TheLogIsBoundedAndNewestFirst()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        for (var i = 0; i < StatLogSystem.Capacity + 15; i++)
        {
            state.Elapsed += TimeSpan.FromMinutes(10);
            StatLogSystem.Record(state, npc, CrewStat.Stress, 1, $"event {i}");
        }

        Assert.Equal(StatLogSystem.Capacity, npc.StatLog.Count);
        Assert.Equal($"event {StatLogSystem.Capacity + 14}", npc.StatLog[0].Cause);
        Assert.Equal("event 15", npc.StatLog[^1].Cause);
    }

    [Fact]
    public void TinyAccumulatedChangesAreHiddenFromTheInspector()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        StatLogSystem.Record(state, npc, CrewStat.Fear, -0.04, "calming down");
        StatLogSystem.Record(state, npc, CrewStat.Health, 5, "treated by Dr. Test");

        var row = Assert.Single(StatLogSystem.DisplayRows(npc));
        Assert.Equal(CrewStat.Health, row.Stat);
        Assert.True(StatLogSystem.IsImprovement(row));
    }

    [Fact]
    public void FireInTheRoomIsLogged()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var room = state.Facility.Rooms[npc.CurrentRoomId];
        room.FireIntensity = 60;

        new StationHazardSystem().Tick(state, Minute);

        Assert.Contains(npc.StatLog, entry => entry.Stat == CrewStat.Health && entry.Cause == "burns" && entry.Delta < 0);
        Assert.Contains(npc.StatLog, entry => entry.Stat == CrewStat.Fear && entry.Cause == $"fire in {room.Name}");
    }

    private static void Run(SimulationEngine engine, GameState state, Npc npc, ActionKind action, int minutes)
    {
        for (var i = 0; i < minutes; i++)
        {
            npc.CurrentAction = new NpcAction(action, null, action.ToString());
            engine.Tick(state, Minute);
        }
    }
}
