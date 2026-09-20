using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Web.Services;

public sealed class GameSession(
    IAiDecisionService aiDecisionService,
    IAiCrewGenerator crewGenerator)
{
    private readonly IAiDecisionService _aiDecisionService = aiDecisionService;
    private readonly IAiCrewGenerator _crewGenerator = crewGenerator;
    private readonly SimulationEngine _simulation = new();
    private readonly EnvironmentSystem _environment = new();
    private readonly VacuumConsequenceSystem _vacuum = new();
    private readonly MissingPersonSystem _missingPeople = new();
    private readonly CrewCounterplaySystem _counterplay = new();
    private readonly CrewRoutineSystem _crewRoutines = new();
    private readonly SocialSimulationSystem _social = new();
    private readonly IntentExecutionSystem _intentExecution = new();
    private readonly NavigationSystem _navigation = new();
    private readonly LocalMovementSystem _movement = new();
    private readonly SuspicionSystem _suspicion = new();
    private readonly ShutdownSystem _shutdown = new();
    private readonly ManualOverrideSystem _manualOverrides = new();
    private readonly ConversationPacingSystem _conversationPacing = new();
    private readonly SimulationClock _clock = new();

    private int _mindCursor;
    private bool _initialized;

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
        + State.Crew.Count(npc => !npc.IsAlive)
        + (State.LifeSupport.IsOnline ? 0 : 1)
        + State.Facility.Rooms.Values.Count(room =>
            room.CarbonDioxidePercent > 1.0
            || room.PressureKpa < 90)
        + State.Facility.Rooms.Values.Count(room =>
            room.HasExteriorHatch && room.ExteriorHatchOpen);

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        var crew = await _crewGenerator.GenerateAsync(cancellationToken);
        State = FacilitySeeder.CreateDefault(crew);
        _initialized = true;
    }

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

        if (State.ScenarioStatus != ScenarioStatus.Running)
        {
            _clock.Pause();
            return false;
        }

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

    public async Task ResetAsync(
        CancellationToken cancellationToken = default)
    {
        _clock.Pause();
        _mindCursor = 0;
        var crew = await _crewGenerator.GenerateAsync(cancellationToken);
        State = FacilitySeeder.CreateDefault(crew);
        _initialized = true;
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
        AudioCueSystem.Emit(State, AudioCueKind.System, roomId: door.RoomAId);
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
        AudioCueSystem.Emit(
            State,
            door.IsLocked ? AudioCueKind.Warning : AudioCueKind.System,
            roomId: door.RoomAId);
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

        AudioCueSystem.Emit(
            State,
            room.IsPowered ? AudioCueKind.System : AudioCueKind.Warning,
            roomId: room.Id);
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
        AudioCueSystem.Emit(State, AudioCueKind.System, roomId: room.Id);
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
        AudioCueSystem.Emit(
            State,
            room.CameraOnline ? AudioCueKind.System : AudioCueKind.Warning,
            roomId: room.Id);
        Log($"{room.Name} camera {(room.CameraOnline ? "ONLINE" : "OFFLINE")}.");
    }

    public void AdjustTemperatureSetpoint(string roomId, double deltaC)
    {
        var room = State.Facility.Rooms[roomId];

        if (!room.HasTemperatureControl || !room.IsTemperatureAiControllable)
        {
            Log($"{room.Name} temperature command refused: LOCAL/AUTONOMOUS CONTROL.");
            AudioCueSystem.Emit(State, AudioCueKind.Warning, roomId: room.Id);
            return;
        }

        if (!room.IsPowered)
        {
            Log($"{room.Name} temperature command refused: NO POWER.");
            return;
        }

        room.TemperatureSetpointC = Math.Clamp(
            room.TemperatureSetpointC + deltaC,
            5,
            40);

        AudioCueSystem.Emit(State, AudioCueKind.System, roomId: room.Id);
        Log($"{room.Name} temperature setpoint is now {room.TemperatureSetpointC:0.0}°C.");
    }

    public void ToggleTemperatureControl(string roomId)
    {
        var room = State.Facility.Rooms[roomId];

        if (!room.HasTemperatureControl || !room.IsTemperatureAiControllable)
        {
            Log($"{room.Name} climate controller refused command: LOCAL/AUTONOMOUS CONTROL.");
            AudioCueSystem.Emit(State, AudioCueKind.Warning, roomId: room.Id);
            return;
        }

        if (!room.IsPowered)
        {
            Log($"{room.Name} climate controller refused command: NO POWER.");
            return;
        }

        room.TemperatureControlOnline = !room.TemperatureControlOnline;
        AudioCueSystem.Emit(
            State,
            room.TemperatureControlOnline ? AudioCueKind.System : AudioCueKind.Warning,
            roomId: room.Id);
        Log($"{room.Name} climate control {(room.TemperatureControlOnline ? "ONLINE" : "OFFLINE")}.");
    }

    public void ToggleVentilation(string roomId)
    {
        var room = State.Facility.Rooms[roomId];

        if (!room.HasVentilationControl || !room.IsVentilationAiControllable)
        {
            Log($"{room.Name} ventilation command refused: LOCAL/AUTONOMOUS CONTROL.");
            AudioCueSystem.Emit(State, AudioCueKind.Warning, roomId: room.Id);
            return;
        }

        if (!room.IsPowered)
        {
            Log($"{room.Name} ventilation command refused: NO POWER.");
            return;
        }

        room.VentilationEnabled = !room.VentilationEnabled;
        AudioCueSystem.Emit(
            State,
            room.VentilationEnabled ? AudioCueKind.System : AudioCueKind.Warning,
            roomId: room.Id);
        Log($"{room.Name} ventilation {(room.VentilationEnabled ? "OPEN" : "ISOLATED")}.");
    }

    public void ToggleExteriorHatch(string roomId)
    {
        var room = State.Facility.Rooms[roomId];

        if (!room.HasExteriorHatch || !room.IsExteriorHatchAiControllable)
        {
            Log($"{room.Name} has no Overseer-controlled exterior hatch.");
            return;
        }

        if (!room.IsPowered)
        {
            Log($"{room.Name} exterior hatch command refused: NO POWER.");
            return;
        }

        room.ExteriorHatchOpen = !room.ExteriorHatchOpen;
        _suspicion.ObserveExteriorHatchChange(
            State,
            room,
            room.ExteriorHatchOpen);

        AudioCueSystem.Emit(
            State,
            room.ExteriorHatchOpen
                ? AudioCueKind.Critical
                : AudioCueKind.System,
            roomId: room.Id);

        Log(
            $"{room.Name} OUTER HATCH {(room.ExteriorHatchOpen ? "OPEN TO SPACE" : "SEALED")}.");
    }

    public void ToggleLifeSupport()
    {
        if (!State.LifeSupport.IsAiControllable)
        {
            Log("Life support refused command: MANUAL CONTROL ONLY.");
            AudioCueSystem.Emit(State, AudioCueKind.Warning);
            return;
        }

        State.LifeSupport.IsOnline = !State.LifeSupport.IsOnline;
        AudioCueSystem.Emit(
            State,
            State.LifeSupport.IsOnline ? AudioCueKind.System : AudioCueKind.Critical);
        Log($"PRIMARY LIFE SUPPORT {(State.LifeSupport.IsOnline ? "ONLINE" : "OFFLINE")}.");
    }

    private async Task AdvanceCoreAsync(CancellationToken cancellationToken)
    {
        if (State.ScenarioStatus != ScenarioStatus.Running) return;
        var turn = TimeSpan.FromMinutes(1);
        _environment.Tick(State, turn);
        _vacuum.Tick(State);
        _simulation.Tick(State, turn);
        _missingPeople.Tick(State);

        await ThinkIfDueAsync(cancellationToken);

        _intentExecution.Tick(State);
        _counterplay.Tick(State);
        _manualOverrides.Tick(State);
        _social.Tick(State);
        _suspicion.Tick(State);
        _conversationPacing.Tick(State);
        _crewRoutines.Tick(State);
        _movement.Tick(State, TimeSpan.FromMinutes(1));
        _shutdown.Tick(State);
    }

    private async Task ThinkIfDueAsync(CancellationToken cancellationToken)
    {
        var living = State.Crew
            .Where(npc => npc.IsAlive)
            .OrderBy(npc => npc.Name)
            .ToList();

        if (living.Count == 0)
        {
            return;
        }

        // Environmental danger is allowed to interrupt the normal cognition
        // cadence. C# still does not choose the goal: it only asks the mind to
        // reconsider immediately instead of leaving somebody committed to a
        // 24-minute-old routine while their compartment becomes unsafe.
        var emergencyNpc = living
            .Where(npc =>
            {
                var room = State.Facility.Rooms[npc.CurrentRoomId];

                return CrewEnvironmentSafety.IsDangerous(room)
                    && !IsAlreadyEscapingToSaferRoom(npc, room);
            })
            .OrderByDescending(npc =>
                CrewEnvironmentSafety.RiskScore(
                    State.Facility.Rooms[npc.CurrentRoomId]))
            .ThenBy(npc => npc.Name)
            .FirstOrDefault();

        if (emergencyNpc is not null)
        {
            emergencyNpc.Intent = null;
            emergencyNpc.Movement = null;
            emergencyNpc.RoutineUntil = TimeSpan.Zero;

            await ThinkForNpcAsync(
                emergencyNpc,
                emergency: true,
                cancellationToken);

            return;
        }

        var eventNpc = living
            .Where(npc =>
                npc.NeedsMindReconsideration
                && (npc.Intent is null || npc.Intent.Urgency < 85))
            .OrderByDescending(npc =>
                npc.MissingPersonConcerns.Values.Any(concern =>
                    concern.Stage == MissingPersonConcernStage.Escalated))
            .ThenBy(npc => npc.Name)
            .FirstOrDefault();

        if (eventNpc is not null)
        {
            eventNpc.Intent = null;
            eventNpc.Movement = null;
            eventNpc.RoutineUntil = TimeSpan.Zero;

            await ThinkForNpcAsync(
                eventNpc,
                emergency: eventNpc.MissingPersonConcerns.Count > 0,
                cancellationToken);

            eventNpc.NeedsMindReconsideration = false;
            return;
        }

        var minute = (int)Math.Floor(State.Elapsed.TotalMinutes);

        // One ordinary mind every four simulated minutes. Emergency danger
        // bypasses this cadence above.
        if (minute <= 0 || minute % 4 != 0)
        {
            return;
        }

        var npc = living[_mindCursor % living.Count];
        _mindCursor++;

        // Do not let a routine model call erase a goal that the human is already
        // physically pursuing (including mutually coordinated social routines).
        if (npc.Intent is not null)
        {
            return;
        }

        await ThinkForNpcAsync(
            npc,
            emergency: false,
            cancellationToken);
    }

    private async Task ThinkForNpcAsync(
        Npc npc,
        bool emergency,
        CancellationToken cancellationToken)
    {
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
            emergency ? NpcBubbleKind.Alert : NpcBubbleKind.Thought,
            State.Elapsed,
            State.Elapsed + TimeSpan.FromMinutes(emergency ? 4 : 3));

        AudioCueSystem.Emit(
            State,
            emergency ? AudioCueKind.Warning : AudioCueKind.Thought,
            npc.Id.ToString(),
            npc.CurrentRoomId);

        npc.Memories.Add(new Memory(
            $"I decided to: {intent.Goal}",
            State.Elapsed,
            Math.Clamp(intent.Urgency / 100d, 0.2, 0.85)));

        Log(
            $"{npc.Name} forms an intention [{intent.Source}]: {intent.Goal}");
    }

    private bool IsAlreadyEscapingToSaferRoom(Npc npc, Room currentRoom)
    {
        if (npc.Intent is { Action: ActionKind.ForceDoor }
            && npc.CurrentAction.Kind == ActionKind.ForceDoor)
        {
            return true;
        }

        if (npc.Intent is not { Action: ActionKind.Move, TargetId: { } targetId }
            || !State.Facility.Rooms.TryGetValue(targetId, out var targetRoom))
        {
            return false;
        }

        if (CrewEnvironmentSafety.RiskScore(targetRoom)
            >= CrewEnvironmentSafety.RiskScore(currentRoom))
        {
            return false;
        }

        return _navigation.FindPath(
            State.Facility,
            currentRoom.Id,
            targetRoom.Id).Count >= 2;
    }

    private void Log(string message)
    {
        var timestamp = State.Elapsed.ToString(@"hh\:mm");
        State.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
