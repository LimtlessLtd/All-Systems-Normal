using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Web.Client.Services;

public sealed class GameSession
{
    private readonly SimulationEngine _simulation = new();
    private readonly EnvironmentSystem _environment = new();
    private readonly AirlockSafetySystem _airlockSafety = new();
    private readonly VacuumConsequenceSystem _vacuum = new();
    private readonly MissingPersonSystem _missingPeople = new();
    private readonly CrewCounterplaySystem _counterplay = new();
    private readonly CrewRoutineSystem _crewRoutines = new();
    private readonly SocialSimulationSystem _social = new();
    private readonly BrowserMindSystem _browserMind = new();
    private readonly IntentExecutionSystem _intentExecution = new();
    private readonly LocalMovementSystem _movement = new();
    private readonly SuspicionSystem _suspicion = new();
    private readonly ShutdownSystem _shutdown = new();
    private readonly CorporateDirectiveSystem _directives = new();
    private readonly SuspicionDynamicsSystem _suspicionDynamics = new();
    private readonly OverseerCommsSystem _comms = new();
    private readonly IOverseerMessageInterpreter _messageInterpreter =
        new RuleBasedOverseerMessageInterpreter();
    private readonly ManualOverrideSystem _manualOverrides = new();
    private readonly ConversationPacingSystem _conversationPacing = new();
    private readonly SimulationClock _clock = new();

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
            room.HasExteriorHatch
            && (room.ExteriorHatchOpen
                || room.AirlockAlarmActive
                || !room.AirlockSafetyInterlocksEnabled));

    public (bool Started, long Generation) StartClock() =>
        _clock.Start();

    public void PauseClock() =>
        _clock.Pause();

    public bool TryAdvanceRunning(long generation)
    {
        if (!_clock.IsActive(generation))
        {
            return false;
        }

        AdvanceCore();

        if (State.ScenarioStatus != ScenarioStatus.Running)
        {
            _clock.Pause();
            return false;
        }

        return true;
    }

    public void AdvanceOneMinute() =>
        AdvanceCore();

    public void AdvanceMinutes(int minutes)
    {
        for (var i = 0; i < Math.Max(0, minutes); i++)
        {
            AdvanceCore();
        }
    }

    public void Reset()
    {
        _clock.Pause();
        State = FacilitySeeder.CreateDefault();
    }

    /// <summary>
    /// Starts a named campaign scenario on a fresh station. Directives, crew and
    /// station state are all reseeded so a mission is reproducible.
    /// </summary>
    public void LoadScenario(string scenarioId)
    {
        var scenario = ScenarioCatalog.Find(scenarioId);

        if (scenario is null)
        {
            return;
        }

        _clock.Pause();
        State = FacilitySeeder.CreateDefault();
        ScenarioCatalog.Apply(State, scenario);

        Log($"DIRECTIVE PACKAGE LOADED — {scenario.Title}.");
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

        var opening = !door.IsOpen;

        if (!_airlockSafety.CanToggleInnerHatch(
                State,
                door,
                opening,
                out var airlockSafetyMessage))
        {
            Log(airlockSafetyMessage);
            AudioCueSystem.Emit(
                State,
                AudioCueKind.Warning,
                roomId: door.RoomAId);
            return;
        }

        door.IsOpen = opening;
        _suspicion.ObservePlayerDoorChange(State, door, becameRestrictive: !door.IsOpen);

        if (opening)
        {
            SuspicionDynamicsSystem.RecordBenignAct(
                State,
                door.RoomAId,
                $"Overseer opened {door.Id} without being asked.",
                2);
            SuspicionDynamicsSystem.RecordBenignAct(
                State,
                door.RoomBId,
                $"Overseer opened {door.Id} without being asked.",
                2);
        }
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

        if (room.IsPowered)
        {
            SuspicionDynamicsSystem.RecordBenignAct(
                State,
                room.Id,
                $"Overseer restored power to {room.Name}.",
                8);
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
        AudioCueSystem.Emit(State, AudioCueKind.System, roomId: room.Id);

        if (room.LightsOn)
        {
            SuspicionDynamicsSystem.RecordBenignAct(
                State,
                room.Id,
                $"Overseer brought the lights back up in {room.Name}.",
                3);
        }

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

        if (room.TemperatureControlOnline)
        {
            SuspicionDynamicsSystem.RecordBenignAct(
                State,
                room.Id,
                $"Overseer restored climate control in {room.Name}.",
                5);
        }

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

        if (room.VentilationEnabled)
        {
            SuspicionDynamicsSystem.RecordBenignAct(
                State,
                room.Id,
                $"Overseer reopened the air loop to {room.Name}.",
                6);
        }

        Log($"{room.Name} ventilation {(room.VentilationEnabled ? "OPEN" : "ISOLATED")}.");
    }

    public void ToggleExteriorHatch(string roomId)
    {
        var room = State.Facility.Rooms[roomId];

        if (!_airlockSafety.TryToggleExteriorHatch(
                State,
                roomId,
                out var opened,
                out var message))
        {
            Log(message);
            AudioCueSystem.Emit(
                State,
                AudioCueKind.Warning,
                roomId: room.Id);
            return;
        }

        _suspicion.ObserveExteriorHatchChange(
            State,
            room,
            opened);

        AudioCueSystem.Emit(
            State,
            opened ? AudioCueKind.Critical : AudioCueKind.System,
            roomId: room.Id);

        Log(message);
    }

    public void SetAirlockCycle(
        string roomId,
        AirlockCycleMode mode)
    {
        if (_airlockSafety.TryStartCycle(
                State,
                roomId,
                mode,
                out var message))
        {
            Log(message);
            return;
        }

        Log(message);
        AudioCueSystem.Emit(
            State,
            AudioCueKind.Warning,
            roomId: roomId);
    }

    public void ToggleAirlockSafetyInterlocks(string roomId)
    {
        if (_airlockSafety.TryToggleSafetyInterlocks(
                State,
                roomId,
                out var message))
        {
            Log(message);
            return;
        }

        Log(message);
        AudioCueSystem.Emit(
            State,
            AudioCueKind.Warning,
            roomId: roomId);
    }

    /// <summary>
    /// Overseer's voice. The player writes whatever they like; an interpreter
    /// reads it into a structured claim, and the simulation decides what that
    /// claim does to the people who hear it. Nothing here edits a belief
    /// directly, and a false claim is recorded as false the moment it is sent.
    /// </summary>
    public async Task<bool> SendMessageAsync(
        OverseerMessageScope scope,
        string? targetNpcName,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || State.ScenarioStatus != ScenarioStatus.Running)
        {
            return false;
        }

        if (scope == OverseerMessageScope.Direct
            && !State.Crew.Any(npc =>
                npc.IsAlive
                && npc.IsPresent
                && npc.Name.Equals(targetNpcName, StringComparison.OrdinalIgnoreCase)))
        {
            Log($"CHANNEL FAILED: {targetNpcName ?? "unknown recipient"} is not reachable.");
            return false;
        }

        var intent = await _messageInterpreter.InterpretAsync(
            text,
            scope,
            targetNpcName,
            State,
            cancellationToken);

        OverseerCommsSystem.Send(
            State,
            scope,
            targetNpcName,
            text,
            intent.Claim,
            intent.SubjectNpcName,
            intent.SubjectRoomId,
            intent.Source);

        return true;
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

        if (State.LifeSupport.IsOnline)
        {
            // Bringing the air back is the loudest possible reassurance, and
            // everyone aboard witnesses it.
            foreach (var occupiedRoomId in State.Crew
                         .Where(npc => npc.IsAlive && npc.IsPresent)
                         .Select(npc => npc.CurrentRoomId)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList())
            {
                SuspicionDynamicsSystem.RecordBenignAct(
                    State,
                    occupiedRoomId,
                    "Overseer brought primary life support back online.",
                    12);
            }
        }

        Log($"PRIMARY LIFE SUPPORT {(State.LifeSupport.IsOnline ? "ONLINE" : "OFFLINE")}.");
    }

    private void AdvanceCore()
    {
        if (State.ScenarioStatus != ScenarioStatus.Running) return;
        var turn = TimeSpan.FromMinutes(1);
        _environment.Tick(State, turn);
        _airlockSafety.Tick(State, turn);
        _vacuum.Tick(State);
        _simulation.Tick(State, turn);
        _missingPeople.Tick(State);
        _browserMind.Tick(State);
        _intentExecution.Tick(State);
        _counterplay.Tick(State);
        _manualOverrides.Tick(State);
        _social.Tick(State);
        _suspicion.Tick(State);
        _conversationPacing.Tick(State);
        _crewRoutines.Tick(State);
        _movement.Tick(State, TimeSpan.FromMinutes(1));
        _shutdown.Tick(State);
        _comms.Tick(State);
        _suspicionDynamics.Tick(State, turn);
        _directives.Tick(State, turn);
    }

    private void Log(string message)
    {
        var timestamp = State.Elapsed.ToString(@"hh\:mm");
        State.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
