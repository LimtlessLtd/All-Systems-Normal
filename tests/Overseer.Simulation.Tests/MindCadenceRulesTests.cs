using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class MindCadenceRulesTests
{
    [Fact]
    public void BusyRoundRobinMember_DoesNotConsumeTheOnlyCognitionSlot()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var crew = state.Crew
            .OrderBy(npc => npc.Name, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        for (var i = 0; i < crew.Count - 1; i++)
        {
            crew[i].Intent = new NpcIntent(
                ActionKind.Work,
                crew[i].CurrentRoomId,
                "Keep working.",
                "Existing goal.",
                60,
                "Test",
                state.Elapsed);
        }

        var onlyIdle = crew[^1];
        var cursor = 0;

        var selected = MindCadenceRules.NextIdleMind(crew, ref cursor);

        Assert.Same(onlyIdle, selected);
        Assert.Equal(0, cursor);
    }

    [Fact]
    public void AfterAFullBusyRound_TheFirstClearedMindIsFoundOnTheNextSlot()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 480043);
        var crew = state.Crew
            .OrderBy(npc => npc.Name, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        foreach (var npc in crew)
        {
            npc.Intent = new NpcIntent(
                ActionKind.Work,
                npc.CurrentRoomId,
                "Keep working.",
                "Existing goal.",
                60,
                "Test",
                state.Elapsed);
        }

        var cursor = 0;
        Assert.Null(MindCadenceRules.NextIdleMind(crew, ref cursor));
        Assert.Equal(0, cursor);

        // Mirrors an intent expiring in IntentExecutionSystem after an earlier
        // cognition slot. The next four-minute slot must see it immediately,
        // rather than waiting for another whole roster rotation.
        crew[1].Intent = null;

        var selected = MindCadenceRules.NextIdleMind(crew, ref cursor);

        Assert.Same(crew[1], selected);
        Assert.Equal(2, cursor);
    }
}
