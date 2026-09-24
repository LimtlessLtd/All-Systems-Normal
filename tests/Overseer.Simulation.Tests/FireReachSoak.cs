using Overseer.Domain;
using Overseer.Simulation;
using Overseer.AI;

namespace Overseer.Simulation.Tests;

public sealed class FireReachSoak
{
    [Fact]
    public async Task Soak()
    {
        var seeds = Enumerable.Range(1, 20).ToList();
        var results = await Task.WhenAll(seeds.Select(seed => Task.Run(() => RunSeed(seed))));
        var lines = results.Select(r => $"seed {r.Seed}: dead {r.Dead}/{r.Crew} burns {r.Burns} fireRoomMinutes {r.FireMinutes} peakRooms {r.PeakRooms}").ToList();
        lines.Add($"TOTAL dead {results.Sum(r => r.Dead)}/{results.Sum(r => r.Crew)} burns {results.Sum(r => r.Burns)} fireRoomMinutes {results.Sum(r => r.FireMinutes)}");
        File.WriteAllLines(Environment.GetEnvironmentVariable("SOAK_OUT") ?? "/tmp/soak.txt", lines);
    }

    private static async Task<(int Seed, int Crew, int Dead, int Burns, int FireMinutes, int PeakRooms)> RunSeed(int seed)
    {
        var state = FacilitySeeder.CreateDefault(SeededCrewRosterGenerator.Generate(seed, 12), upkeepSeed: seed, stationSeed: seed);
        var session = new Session(state);
        var fireMinutes = 0;
        var peak = 0; var breach = -1; var firstDeath = -1; List<string> deathLog = []; var trace = new List<string>();
        for (var minute = 0; minute < 36 * 60; minute++)
        {
            state.ScenarioStatus = ScenarioStatus.Running;
            await session.AdvanceMinutesAsync(1);
            var burning = state.Facility.Rooms.Values.Count(room => room.FireIntensity > 0);
            fireMinutes += burning;
            peak = Math.Max(peak, burning);
            if (seed == 15 && minute >= 1186 && minute <= 1215)
            {
                var ls = state.LifeSupport;
                var crewRooms = state.Crew.Where(n => n.IsAlive).Select(n => state.Facility.Rooms[n.CurrentRoomId]).ToList();
                trace.Add($"m{minute} P={state.Facility.Rooms.Values.Average(r=>r.PressureKpa):0.0} openDoors={state.Facility.Doors.Count(d=>d.IsOpen)} lowO2Rooms=[{string.Join(",", state.Facility.Rooms.Values.Where(r=>r.OxygenPercent<10).Select(r=>$"{r.Id}:{r.OxygenPercent:0}/{r.PressureKpa:0}kPa"))}] LS online={ls.IsOnline} req={ls.RequestedOnline} o2gen={ls.OxygenGeneratorOnline} scrub={ls.CarbonScrubberOnline} reserve={ls.OxygenReservePercent:0.0} power={state.Power.DistributionEfficiencyPercent:0} alive={crewRooms.Count} minCrewO2={(crewRooms.Count>0?crewRooms.Min(r=>r.OxygenPercent):0):0.0} avgO2={state.Facility.Rooms.Values.Average(r=>r.OxygenPercent):0.0} fires=[{string.Join(",", state.Facility.Rooms.Values.Where(r=>r.FireIntensity>0).Select(r=>$"{r.Id}:{r.FireIntensity:0}"))}] breach=[{string.Join(",", state.Facility.Rooms.Values.Where(r=>r.HasHullBreach).Select(r=>r.Id))}] devicesDown=[{string.Join(",", state.Devices.Values.Where(d=>!d.IsOperational && d.Kind is StationSystemKind.LifeSupport or StationSystemKind.OxygenGenerator or StationSystemKind.CarbonScrubber or StationSystemKind.PowerGenerator).Select(d=>$"{d.Id}(en={d.IsEnabled},fail={d.IsFailed},c={d.Condition:0})"))}]");
            }
            if (breach < 0 && state.Facility.Rooms.Values.Any(r => r.HasHullBreach)) breach = minute;
            if (firstDeath < 0 && state.Crew.Any(n => !n.IsAlive)) { firstDeath = minute; deathLog = state.EventLog.TakeLast(40).ToList(); }
        }

        File.WriteAllLines((Environment.GetEnvironmentVariable("SOAK_OUT") ?? "/tmp/soak.txt") + $".{seed}.deaths",
            state.Crew.Where(n => !n.IsAlive).Select(n => $"{n.Name} [{n.Role}] at {n.CurrentRoomId}: {n.CauseOfDeath}")
                .Append($"breach@{breach} firstDeath@{firstDeath} vacuumRooms={EnvironmentSystem.FindVacuumDepths(state).Count}").Concat(trace).Concat(deathLog));
        return (seed, state.Crew.Count, state.Crew.Count(n => !n.IsAlive),
            state.Crew.Count(n => !n.IsAlive && (n.CauseOfDeath ?? "").Contains("burn", StringComparison.OrdinalIgnoreCase)),
            fireMinutes, peak);
    }

    private sealed class Session(GameState state)
        : StationSession(new RuleBasedOverseerMessageInterpreter(), state)
    {
        private readonly BrowserMindSystem _mind = new();
        public override Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task RegenerateStationAsync(int? seed = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task RestoreCampaignAsync(CampaignState campaign, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task LoadScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task LoadStandaloneScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        protected override Task ThinkAsync(CancellationToken cancellationToken) { _mind.Tick(State); return Task.CompletedTask; }
    }
}
