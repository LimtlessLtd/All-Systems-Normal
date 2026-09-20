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
    private readonly RobotCountermeasureSystem _robotCountermeasures = new();
    private readonly RobotSystem _robots = new();
    private readonly TurretCountermeasureSystem _turretCountermeasures = new();
    private readonly TurretSystem _turrets = new();
    private readonly CrewRoutineSystem _crewRoutines = new();
    private readonly SocialSimulationSystem _social = new();
    private readonly BrowserMindSystem _browserMind = new();
    private readonly IntentExecutionSystem _intentExecution = new();
    private readonly InvestigationSystem _investigations = new();
    private readonly ShutdownCoordinationSystem _shutdownCoordination = new();
    private readonly LocalMovementSystem _movement = new();
    private readonly SuspicionSystem _suspicion = new();
    private readonly ShutdownSystem _shutdown = new();
    private readonly ScenarioProgressSystem _scenarioProgress = new();
    private readonly CorporateDirectiveSystem _directives = new();
    private readonly SuspicionDynamicsSystem _suspicionDynamics = new();
    private readonly OverseerCommsSystem _comms = new();
    private readonly CrewAccountComparisonSystem _accountComparison = new();
    private readonly StationUpkeepSystem _upkeep = new();
    private readonly CrewMaintenanceSystem _maintenance = new();
    private readonly CrewProvisioningSystem _provisioning = new();
    private readonly IOverseerMessageInterpreter _messageInterpreter =
        new RuleBasedOverseerMessageInterpreter();
    private readonly ManualOverrideSystem _manualOverrides = new();
    private readonly ConversationPacingSystem _conversationPacing = new();
    private readonly SimulationClock _clock = new();

    public GameState State { get; private set; } = FacilitySeeder.CreateDefault();

    public CampaignState Campaign { get; private set; } = new();

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
                || !room.AirlockSafetyInterlocksEnabled))
        + State.Robots.Count(robot =>
            robot.IsDestroyed
            || robot.Policy == RobotPolicy.Hostile
            || robot.IsNetworkIsolated
            || !robot.ChargingEnabled)
        + State.Turrets.Count(turret =>
            turret.IsDestroyed
            || (turret.IsArmed && turret.Policy != TurretPolicy.Safe)
            || turret.IsNetworkIsolated
            || !turret.PowerFeedEnabled);

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
        Campaign = new CampaignState();
        State = FacilitySeeder.CreateDefault();
    }

    public void CaptureCampaignProgress() =>
        CampaignProgressionSystem.CaptureCompletedMission(Campaign, State);

    public bool TryResolveCampaignEnding(CampaignEndgameChoice choice) =>
        CampaignProgressionSystem.TryResolveEnding(Campaign, choice, out _);

    public void RestoreCampaign(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        _clock.Pause();
        Campaign = campaign;

        var continuingCrew = CampaignProgressionSystem.CreateContinuingCrew(Campaign);
        State = continuingCrew is null
            ? FacilitySeeder.CreateDefault()
            : FacilitySeeder.CreateDefault(continuingCrew);

        var next = CampaignProgressionSystem.NextScenario(Campaign);
        var last = Campaign.MissionHistory.LastOrDefault();
        var scenario = next
            ?? (last is null ? null : ScenarioCatalog.Find(last.ScenarioId));

        if (scenario is not null)
        {
            ScenarioCatalog.Apply(State, scenario);

            if (next is null)
            {
                State.ScenarioStatus = last?.Outcome ?? ScenarioStatus.Won;
                State.ScenarioOutcome = Campaign.Ending?.Summary
                    ?? "All campaign assignments are recorded. Awaiting final Overseer decision.";
            }
        }

        CampaignProgressionSystem.ApplyCarryOver(Campaign, State);
        Campaign.CurrentScenarioId = scenario?.Id;
    }

    /// <summary>
    /// Starts only the next unlocked campaign assignment on a fresh station.
    /// Arbitrary scenario selection is intentionally rejected by the campaign
    /// layer instead of relying on UI controls for progression integrity.
    /// </summary>
    public void LoadScenario(string scenarioId)
    {
        _clock.Pause();
        CampaignProgressionSystem.CaptureCompletedMission(Campaign, State);

        if (!CampaignProgressionSystem.CanStartScenario(Campaign, scenarioId))
        {
            Log($"DIRECTIVE PACKAGE {scenarioId} is locked by campaign progression.");
            return;
        }

        var scenario = ScenarioCatalog.Find(scenarioId);
        if (scenario is null)
        {
            return;
        }

        var continuingCrew = CampaignProgressionSystem.CreateContinuingCrew(Campaign);
        State = continuingCrew is null
            ? FacilitySeeder.CreateDefault()
            : FacilitySeeder.CreateDefault(continuingCrew);

        ScenarioCatalog.Apply(State, scenario);
        CampaignProgressionSystem.ApplyCarryOver(Campaign, State);
        Campaign.CurrentScenarioId = scenario.Id;

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
        if (!door.IsOpen)
        {
            State.Telemetry.RestrictiveDoorCommands++;
        }
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
        if (door.IsLocked)
        {
            State.Telemetry.RestrictiveDoorCommands++;
        }
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

        _suspicion.ObservePlayerRoomSystemChange(
            State,
            room,
            "local power",
            becameDisruptive: !room.IsPowered,
            weight: 8);

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
        _suspicion.ObservePlayerRoomSystemChange(
            State,
            room,
            "lighting",
            becameDisruptive: !room.LightsOn,
            weight: 3);
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
        _suspicion.ObservePlayerRoomSystemChange(
            State,
            room,
            "surveillance",
            becameDisruptive: !room.CameraOnline,
            weight: 3);
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
        _suspicion.ObservePlayerRoomSystemChange(
            State,
            room,
            "climate control",
            becameDisruptive: !room.TemperatureControlOnline,
            weight: 5);
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
        _suspicion.ObservePlayerRoomSystemChange(
            State,
            room,
            "ventilation",
            becameDisruptive: !room.VentilationEnabled,
            weight: 9);
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
        var wasEnabled = State.Facility.Rooms.TryGetValue(roomId, out var room)
            && room.AirlockSafetyInterlocksEnabled;

        if (_airlockSafety.TryToggleSafetyInterlocks(
                State,
                roomId,
                out var message))
        {
            if (wasEnabled
                && State.Facility.Rooms.TryGetValue(roomId, out var changedRoom)
                && !changedRoom.AirlockSafetyInterlocksEnabled)
            {
                State.Telemetry.AirlockSafetyBypasses++;
            }

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

    public bool SetRobotPolicy(string robotId, RobotPolicy policy)
    {
        if (_robots.TrySetPolicy(State, robotId, policy, out var message))
        {
            return true;
        }

        Log(message);
        AudioCueSystem.Emit(State, AudioCueKind.Warning);
        return false;
    }

    public bool ToggleRobotRemoteShutdown(string robotId)
    {
        if (_robots.TryToggleRemoteShutdown(State, robotId, out var message))
        {
            return true;
        }

        Log(message);
        AudioCueSystem.Emit(State, AudioCueKind.Warning);
        return false;
    }

    public bool SetTurretPolicy(string turretId, TurretPolicy policy)
    {
        if (_turrets.TrySetPolicy(State, turretId, policy, out var message))
        {
            return true;
        }

        Log(message);
        AudioCueSystem.Emit(State, AudioCueKind.Warning);
        return false;
    }

    public bool SetTurretArmed(string turretId, bool armed)
    {
        if (_turrets.TrySetArmed(State, turretId, armed, out var message))
        {
            return true;
        }

        Log(message);
        AudioCueSystem.Emit(State, AudioCueKind.Warning);
        return false;
    }

    private void AdvanceCore()
    {
        if (State.ScenarioStatus != ScenarioStatus.Running) return;
        var turn = TimeSpan.FromMinutes(1);
        _upkeep.Tick(State, turn);
        _environment.Tick(State, turn);
        _airlockSafety.Tick(State, turn);
        _vacuum.Tick(State);
        _simulation.Tick(State, turn);
        _missingPeople.Tick(State);
        _browserMind.Tick(State);
        _intentExecution.Tick(State);
        _investigations.Tick(State);
        _counterplay.Tick(State);
        _robotCountermeasures.Tick(State);
        _turretCountermeasures.Tick(State);
        _manualOverrides.Tick(State);
        _shutdownCoordination.Tick(State);
        _social.Tick(State);
        _suspicion.Tick(State);
        _conversationPacing.Tick(State);
        _crewRoutines.Tick(State);
        _robots.Tick(State, turn);
        _turrets.Tick(State, turn);
        _movement.Tick(State, TimeSpan.FromMinutes(1));
        _shutdown.Tick(State);
        _provisioning.Tick(State, turn);
        _maintenance.Tick(State);
        _comms.Tick(State);
        _accountComparison.Tick(State);
        _suspicionDynamics.Tick(State, turn);

        // Directives are graded before the station layer decides the outcome, so
        // its win gate reads this tick's directive results rather than the
        // previous tick's.
        _directives.Tick(State, turn);
        _scenarioProgress.Tick(State, turn);
    }

    private void Log(string message)
    {
        var timestamp = State.Elapsed.ToString(@"hh\:mm");
        State.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
