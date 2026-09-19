using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Web.Services;

public sealed class GameSession(IAiDecisionService aiDecisionService)
{
    private readonly IAiDecisionService _aiDecisionService = aiDecisionService;
    private readonly SimulationEngine _simulation = new();
    private readonly CrewRoutineSystem _crewRoutines = new();
    private readonly SocialSimulationSystem _social = new();
    private readonly IntentExecutionSystem _intentExecution = new();
    private readonly LocalMovementSystem _movement = new();
    private readonly SuspicionSystem _suspicion = new();
    private readonly ShutdownSystem _shutdown = new();
    private readonly ManualOverrideSystem _manualOverrides = new();
    private readonly SimulationClock _clock = new();

    private int _mindCursor;

    public GameState State { get; private set; } = FacilitySeeder.CreateDefault();

    public bool IsRunning => _clock.IsRunning;

    public int PoweredRoomCount =>
        State.Facility.Rooms.Values.Count(room => room.IsPowered);

    public int CameraCount =>
        State.Facility.Rooms.Values.Count(room => room.HasVisualFeed);

    public int LivingCrewCount =>
        State.Crew.Count(npc => npc.IsAlive);

    public int AlertCount =>
        State.Facility.Rooms.Values.Count(room =>
            !room.IsPowered
            || !room.CameraOnline
            || room.OxygenPercent < 19.5
            || room.TemperatureC is < 16 or > 28)
        + State.Crew.Count(npc => !npc.IsAlive);

    public (bool Started, long Generation) StartClock() =>
        _clock.Start();

    public void PauseClock() =>
        _clock.Pause();

    public async Task<bool> TryAdvanceRunningAsync(
        long generation,
        CancellationToken cancellationToken = default)
    {
        if (!_clock.IsActive(generation))
        {
            return false;
        }

        await AdvanceCoreAsync(cancellationToken);
        return _clock.IsActive(generation);
    }

    public Task AdvanceOneMinuteAsync(
        CancellationToken cancellationToken = default) =>
        AdvanceCoreAsync(cancellationToken);

    public async Task AdvanceMinutesAsync(
        int minutes,
        CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < Math.Max(0, minutes); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AdvanceCoreAsync(cancellationToken);
        }
    }

    public void Reset()
    {
        _clock.Pause();
        _mindCursor = 0;
        State = FacilitySeeder.CreateDefault();
    }

    public void ToggleDoor(string doorId)
    {
        var door = State.Facility.Doors.First(door => door.Id == doorId);

        if (!door.IsAiControllable || door.IsManuallyOverridden)
        {
            Log($"{door.Id} refused OPEN/CLOSE command: MANUAL CONTROL ONLY.");
            return;
        }

        if (!door.IsPowered)
        {
            Log($"{door.Id} refused OPEN/CLOSE command: NO POWER.");
            return;
        }

        if (door.IsLocked)
        {
            Log($"{door.Id} refused OPEN/CLOSE command: LOCKED.");
            return;
        }

        door.IsOpen = !door.IsOpen;
        _suspicion.ObservePlayerDoorChange(State, door, becameRestrictive: !door.IsOpen);
        Log($"{door.Id} is now {(door.IsOpen ? "OPEN" : "CLOSED")}.");
    }

    public void ToggleLock(string doorId)
    {
        var door = State.Facility.Doors.First(door => door.Id == doorId);

        if (!door.IsAiControllable || door.IsManuallyOverridden)
        {
            Log($"{door.Id} refused LOCK command: MANUAL CONTROL ONLY.");
            return;
        }

        if (!door.IsPowered)
        {
            Log($"{door.Id} refused LOCK command: NO POWER.");
            return;
        }

        if (!door.IsLocked && door.IsOpen)
        {
            door.IsOpen = false;
        }

        door.IsLocked = !door.IsLocked;
        _suspicion.ObservePlayerDoorChange(State, door, becameRestrictive: door.IsLocked);
        Log($"{door.Id} is now {(door.IsLocked ? "LOCKED" : "UNLOCKED")}.");
    }

    public void ToggleRoomPower(string roomId)
    {
        var room = State.Facility.Rooms[roomId];
        room.IsPowered = !room.IsPowered;

        if (!room.IsPowered)
        {
            room.LightsOn = false;
            room.CameraOnline = false;
        }

        Log($"{room.Name} power {(room.IsPowered ? "RESTORED" : "CUT")}.");
    }

    public void ToggleLights(string roomId)
    {
        var room = State.Facility.Rooms[roomId];

        if (!room.IsPowered)
        {
            Log($"{room.Name} lighting command refused: NO POWER.");
            return;
        }

        room.LightsOn = !room.LightsOn;
        Log($"{room.Name} lights {(room.LightsOn ? "ON" : "OFF")}.");
    }

    public void ToggleCamera(string roomId)
    {
        var room = State.Facility.Rooms[roomId];

        if (!room.IsPowered)
        {
            Log($"{room.Name} camera command refused: NO POWER.");
            return;
        }

        room.CameraOnline = !room.CameraOnline;
        Log($"{room.Name} camera {(room.CameraOnline ? "ONLINE" : "OFFLINE")}.");
    }

    private async Task AdvanceCoreAsync(CancellationToken cancellationToken)
    {
        if (State.ScenarioStatus != ScenarioStatus.Running) return;
        _simulation.Tick(State, TimeSpan.FromMinutes(1));

        await ThinkIfDueAsync(cancellationToken);

        _intentExecution.Tick(State);
        _manualOverrides.Tick(State);
        _social.Tick(State);
        _suspicion.Tick(State);
        _crewRoutines.Tick(State);
        _movement.Tick(State, TimeSpan.FromMinutes(1));
        _shutdown.Tick(State);
    }

    private async Task ThinkIfDueAsync(CancellationToken cancellationToken)
    {
        var minute = (int)Math.Floor(State.Elapsed.TotalMinutes);

        // One mind every four simulated minutes. With six crew this gives each
        // person a fresh deliberate thought roughly every 24 simulated minutes,
        // while keeping local-model latency and token usage under control.
        if (minute <= 0 || minute % 4 != 0)
        {
            return;
        }

        var living = State.Crew
            .Where(npc => npc.IsAlive)
            .OrderBy(npc => npc.Name)
            .ToList();

        if (living.Count == 0)
        {
            return;
        }

        var npc = living[_mindCursor % living.Count];
        _mindCursor++;

        // Do not let a fresh model call erase a goal that the human is already
        // physically pursuing (including mutually coordinated social routines).
        if (npc.Intent is not null)
        {
            return;
        }

        var intent = await _aiDecisionService.DecideAsync(
            npc,
            State,
            cancellationToken);

        npc.Intent = intent;
        npc.MindMode = intent.Source;
        npc.LastThought = intent.Reason;
        npc.LastThoughtAt = State.Elapsed;
        npc.Bubble = new NpcBubble(
            intent.Goal,
            NpcBubbleKind.Thought,
            State.Elapsed,
            State.Elapsed + TimeSpan.FromMinutes(4));

        npc.Memories.Add(new Memory(
            $"I decided to: {intent.Goal}",
            State.Elapsed,
            Math.Clamp(intent.Urgency / 100d, 0.2, 0.75)));

        Log(
            $"{npc.Name} forms an intention [{intent.Source}]: {intent.Goal}");
    }

    private void Log(string message)
    {
        var timestamp = State.Elapsed.ToString(@"hh\:mm");
        State.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
