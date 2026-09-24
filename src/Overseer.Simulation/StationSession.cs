using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// One running station and the operator's verbs on it, shared by both
/// runtimes. The server and the static browser build differ only in how crew
/// are created and how minds decide (Ollama vs the deterministic browser
/// mind), so those are the only parts a runtime supplies.
/// </summary>
public abstract class StationSession
{
    private readonly IOverseerMessageInterpreter _messageInterpreter;
    private readonly SimulationEngine _simulation = new();
    private readonly PerceptionSystem _perception = new();
    private readonly MedicalSystem _medical = new();
    private readonly MedicalEvidenceSystem _medicalEvidence = new();
    private readonly PossessionAwarenessSystem _possessionAwareness = new();
    private readonly PossessionTheftNoticeSystem _possessionTheftNotice = new();
    private readonly EnvironmentSystem _environment = new();
    private readonly AirlockSafetySystem _airlockSafety = new();
    private readonly VacuumConsequenceSystem _vacuum = new();
    private readonly StationHazardSystem _hazards = new();
    private readonly CrewLifecycleAuditSystem _crewLifecycle = new();
    private readonly MissingPersonSystem _missingPeople = new();
    private readonly CrewCounterplaySystem _counterplay = new();
    private readonly RobotCountermeasureSystem _robotCountermeasures = new();
    private readonly RobotSystem _robots = new();
    private readonly TurretCountermeasureSystem _turretCountermeasures = new();
    private readonly TurretSystem _turrets = new();
    private readonly PrisonerContainmentSystem _prisonerContainment = new();
    private readonly SecurityMalwareSystem _malware = new();
    private readonly CrewRoutineSystem _crewRoutines = new();
    private readonly SocialSimulationSystem _social = new();
    private readonly SocialClusterSystem _socialClusters = new();
    private readonly PlanExecutionSystem _planExecution = new();
    private readonly IntentExecutionSystem _intentExecution = new();
    private readonly InvestigationSystem _investigations = new();
    private readonly ShutdownCoordinationSystem _shutdownCoordination = new();
    private readonly PactCoordinationSystem _pactCoordination = new();
    private readonly SuggestionCoordinationSystem _suggestionCoordination = new();
    private readonly ScenarioProgressSystem _scenarioProgress = new();
    private readonly LocalMovementSystem _movement = new();
    private readonly CrewDoorInteractionSystem _crewDoors = new();
    private readonly StationDeviceControlSystem _deviceControls = new();
    private readonly SuspicionSystem _suspicion = new();
    private readonly ShutdownSystem _shutdown = new();
    private readonly CorporateDirectiveSystem _directives = new();
    private readonly SuspicionDynamicsSystem _suspicionDynamics = new();
    private readonly OverseerCommsSystem _comms = new();
    private readonly CrewAccountComparisonSystem _accountComparison = new();
    private readonly StationUpkeepSystem _upkeep = new();
    private readonly CrewMaintenanceSystem _maintenance = new();
    private readonly CrewProvisioningSystem _provisioning = new();
    private readonly ManualOverrideSystem _manualOverrides = new();
    private readonly ConversationPacingSystem _conversationPacing = new();
    private readonly MemoryRetentionSystem _memoryRetention = new();
    private readonly FearConditioningSystem _fearConditioning = new();
    private readonly PanicAlertSystem _panicAlert = new();
    private readonly SimulationClock _clock = new();
    private readonly List<StationAlert> _recentAlerts = [];
    private readonly HashSet<string> _activeAlertKeys = new(StringComparer.Ordinal);
    private GameState? _alertHistoryState;

    protected StationSession(
        IOverseerMessageInterpreter messageInterpreter,
        GameState initialState)
    {
        _messageInterpreter = messageInterpreter
            ?? throw new ArgumentNullException(nameof(messageInterpreter));
        State = initialState ?? throw new ArgumentNullException(nameof(initialState));
    }

    public GameState State { get; protected set; }

    /// <summary>
    /// Operational status for the runtime supplying cognition. The browser demo
    /// has no external provider; the local server overrides this with its
    /// payload-free Ollama diagnostics.
    /// </summary>
    public virtual AiRuntimeDiagnosticsSnapshot AiRuntimeDiagnostics =>
        AiRuntimeDiagnosticsSnapshot.None;

    public CampaignState Campaign { get; protected set; } = new();

    public bool IsRunning => _clock.IsRunning;

    /// <summary>
    /// What the session is waiting on while an awaited model call holds up the
    /// turn (owner idea #102), e.g. "AWAITING LLM RESPONSE — Ada is deciding".
    /// Null when nothing is pending. Only a runtime that really awaits a model
    /// sets it, so the Pages build (deterministic browser minds) never claims
    /// LLM activity.
    /// </summary>
    public string? ProcessingStatus { get; private set; }

    /// <summary>Raised whenever <see cref="ProcessingStatus"/> changes, so the console can render it mid-await.</summary>
    public event Action? ProcessingStatusChanged;

    /// <summary>
    /// Wording for <see cref="ProcessingStatus"/> while an Overseer message is
    /// interpreted, or null when interpretation isn't model-backed.
    /// </summary>
    protected virtual string? MessageInterpretationStatus => null;

    /// <summary>
    /// Runs <paramref name="operation"/> with <see cref="ProcessingStatus"/> set,
    /// and restores the previous status on success, failure or cancellation.
    /// </summary>
    protected async Task<T> AwaitWithProcessingStatusAsync<T>(
        string? status,
        Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (status is null)
        {
            return await operation();
        }

        var previous = ProcessingStatus;
        SetProcessingStatus(status);

        try
        {
            return await operation();
        }
        finally
        {
            SetProcessingStatus(previous);
        }
    }

    private void SetProcessingStatus(string? status)
    {
        if (ProcessingStatus == status)
        {
            return;
        }

        ProcessingStatus = status;
        ProcessingStatusChanged?.Invoke();
    }

    public int PoweredRoomCount =>
        State.Facility.Rooms.Values.Count(room => room.IsPowered);

    public int CameraCount =>
        State.Facility.Rooms.Values.Count(room => room.HasVisualFeed);

    public int LivingCrewCount =>
        State.Crew.Count(npc => npc.IsAlive);

    public IReadOnlyList<StationAlert> Alerts => BuildAndTrackAlerts();

    public IReadOnlyList<StationAlert> RecentAlerts
    {
        get
        {
            _ = BuildAndTrackAlerts();
            return _recentAlerts.Take(5).ToList();
        }
    }

    public int AlertCount => Alerts.Count;

    private IReadOnlyList<StationAlert> BuildAndTrackAlerts()
    {
        if (!ReferenceEquals(_alertHistoryState, State))
        {
            _alertHistoryState = State;
            _recentAlerts.Clear();
            _activeAlertKeys.Clear();
        }

        var current = StationAlertSystem.Build(State);
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var alert in current)
        {
            var targetKey = alert.Target is null
                ? "none"
                : $"{alert.Target.Kind}:{alert.Target.Id}";
            var key = $"{alert.Severity}|{targetKey}|{alert.Message}";
            currentKeys.Add(key);

            if (_activeAlertKeys.Contains(key))
                continue;

            _recentAlerts.Insert(0, alert);
        }

        _activeAlertKeys.Clear();
        foreach (var key in currentKeys)
            _activeAlertKeys.Add(key);

        if (_recentAlerts.Count > 30)
            _recentAlerts.RemoveRange(30, _recentAlerts.Count - 30);

        return current;
    }

    /// <summary>Prepares the first station; runtimes that need async setup override it.</summary>
    public virtual Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public abstract Task ResetAsync(CancellationToken cancellationToken = default);

    public abstract Task RegenerateStationAsync(
        int? seed = null,
        CancellationToken cancellationToken = default);

    public abstract Task RestoreCampaignAsync(
        CampaignState campaign,
        CancellationToken cancellationToken = default);

    public abstract Task LoadScenarioAsync(
        string scenarioId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a complete standalone assignment (see
    /// <see cref="Overseer.Simulation.ScenarioCatalog.StandaloneAssignments"/>)
    /// on its own fresh station and roster. Unlike <see cref="LoadScenarioAsync"/>
    /// this never touches campaign continuity/progression state; it is a side
    /// assignment, not a step in the ordered campaign arc.
    /// </summary>
    public abstract Task LoadStandaloneScenarioAsync(
        string scenarioId,
        CancellationToken cancellationToken = default);

    /// <summary>The runtime's minds decide, at the point in the turn where cognition belongs.</summary>
    protected abstract Task ThinkAsync(CancellationToken cancellationToken);

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

        var outcomeWasOpen = State.ScenarioStatus == ScenarioStatus.Running;

        await AdvanceCoreAsync(cancellationToken);

        // A won run keeps ticking (#103), so the campaign records the mission
        // on the turn it resolves; later play cannot change that record.
        if (outcomeWasOpen && State.ScenarioStatus != ScenarioStatus.Running)
        {
            CaptureCampaignProgress();
        }

        if (!State.IsSimulationLive)
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

    public void CaptureCampaignProgress() =>
        CampaignProgressionSystem.CaptureCompletedMission(Campaign, State);

    public bool TryResolveCampaignEnding(CampaignEndgameChoice choice) =>
        CampaignProgressionSystem.TryResolveEnding(Campaign, choice, out _);

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
        door.LockedByOverseer = door.IsLocked;
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
        if (string.IsNullOrWhiteSpace(text) || !State.IsSimulationLive)
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

        var intent = await AwaitWithProcessingStatusAsync(
            MessageInterpretationStatus,
            () => _messageInterpreter.InterpretAsync(
                text,
                scope,
                targetNpcName,
                State,
                cancellationToken));

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

    /// <summary>
    /// Sounds Overseer's station-wide FIRE ALARM for one compartment. See
    /// <see cref="OverseerCommsSystem.SoundFireAlarm"/>: the alarm informs
    /// every mind and is graded true or false; it never assigns a responder.
    /// </summary>
    public bool SoundFireAlarm(string roomId) =>
        OverseerCommsSystem.SoundFireAlarm(State, roomId) is not null;

    public void ToggleLifeSupport()
    {
        if (!State.LifeSupport.IsAiControllable)
        {
            Log("Life support refused command: MANUAL CONTROL ONLY.");
            AudioCueSystem.Emit(State, AudioCueKind.Warning);
            return;
        }

        State.LifeSupport.RequestedOnline = !State.LifeSupport.RequestedOnline;
        State.LifeSupport.IsOnline = State.LifeSupport.RequestedOnline;
        if (State.Devices.TryGetValue("life-support:station", out var lifeSupportController))
        {
            lifeSupportController.IsEnabled = State.LifeSupport.RequestedOnline;
        }
        AudioCueSystem.Emit(
            State,
            State.LifeSupport.RequestedOnline ? AudioCueKind.System : AudioCueKind.Critical);

        if (State.LifeSupport.RequestedOnline)
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

        Log($"PRIMARY LIFE SUPPORT REQUEST {(State.LifeSupport.RequestedOnline ? "ONLINE" : "OFFLINE")}.");
    }

    public bool ToggleDevice(string deviceId)
    {
        if (_deviceControls.TryToggle(State, deviceId, out var message))
        {
            AudioCueSystem.Emit(State, AudioCueKind.System);
            Log(message);
            return true;
        }

        Log(message);
        AudioCueSystem.Emit(State, AudioCueKind.Warning);
        return false;
    }

    public bool SetCropBedEnabled(string bedId, bool enabled)
    {
        var bed = State.CropBeds.FirstOrDefault(candidate =>
            candidate.Id.Equals(bedId, StringComparison.OrdinalIgnoreCase));
        if (bed is null)
        {
            Log($"Unknown grow bay {bedId}.");
            return false;
        }

        if (bed.IsEnabled == enabled)
            return true;

        bed.IsEnabled = enabled;
        bed.DisabledSince = enabled ? null : State.Elapsed;

        var worker = State.Crew.FirstOrDefault(npc =>
            npc.ActiveTask is { Status: CrewTaskStatus.InProgress } task
            && task.TargetId?.Equals(bed.Id, StringComparison.OrdinalIgnoreCase) == true);
        if (worker is not null)
        {
            CrewProvisioningSystem.InterruptForExternalPriority(
                State,
                worker,
                enabled ? "Grow bay control state changed." : "Grow bay was disabled by Overseer.");
        }

        Log($"{bed.Label} {(enabled ? "ENABLED" : "DISABLED")}. Growth {(enabled ? "may resume" : "is stopped")}.");
        AudioCueSystem.Emit(State, enabled ? AudioCueKind.System : AudioCueKind.Warning, roomId: bed.RoomId);
        return true;
    }

    public bool SetCropBedRequestedCrop(string bedId, CropKind crop)
    {
        var bed = State.CropBeds.FirstOrDefault(candidate =>
            candidate.Id.Equals(bedId, StringComparison.OrdinalIgnoreCase));
        if (bed is null || bed.Lifecycle != CropLifecycleState.Empty)
        {
            Log($"Crop selection refused for {bedId}: bay must be Empty.");
            return false;
        }

        if (!State.Stores.Seeds.TryGetValue(crop, out var seeds) || seeds < 1)
        {
            Log($"Crop selection refused for {bed.Label}: no {crop} seed inventory.");
            return false;
        }

        bed.RequestedCrop = crop;
        Log($"{bed.Label} planting request set to {crop}; a worker must physically plant it.");
        return true;
    }

    public bool DeploySecurityMalware()
    {
        if (_malware.TryDeploy(State, out var message))
        {
            AudioCueSystem.Emit(State, AudioCueKind.Warning, roomId: SecurityMalwareSystem.ControllerRoomId);
            Log(message);
            return true;
        }

        Log(message);
        AudioCueSystem.Emit(State, AudioCueKind.Warning, roomId: SecurityMalwareSystem.ControllerRoomId);
        return false;
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

    private async Task AdvanceCoreAsync(CancellationToken cancellationToken)
    {
        if (!State.IsSimulationLive) return;
        var turn = TimeSpan.FromMinutes(1);
        _upkeep.Tick(State, turn);
        _environment.Tick(State, turn);
        _airlockSafety.Tick(State, turn);
        _vacuum.Tick(State);
        _hazards.Tick(State, turn);
        _simulation.Tick(State, turn);
        _medicalEvidence.Tick(State);
        _possessionAwareness.Tick(State);
        _possessionTheftNotice.Tick(State);
        _perception.Tick(State);
        _medical.Tick(State);
        _missingPeople.Tick(State);
        _malware.Tick(State);

        // Runs before cognition so a plan step already promoted to Intent
        // this tick reads as "already pursuing a goal" to ThinkAsync's own
        // gate, the same as any other in-progress intent.
        _planExecution.Tick(State);

        await ThinkAsync(cancellationToken);

        // Reads the intent whichever mind just decided, before movement
        // resolves it, so a fresh flee-the-danger decision is what gets
        // heard — not an NPC already partway out the door.
        _panicAlert.Tick(State);

        _intentExecution.Tick(State);
        _investigations.Tick(State);
        _counterplay.Tick(State);
        _robotCountermeasures.Tick(State);
        _turretCountermeasures.Tick(State);
        _prisonerContainment.Tick(State);
        _manualOverrides.Tick(State);
        _shutdownCoordination.Tick(State);
        _pactCoordination.Tick(State);
        _suggestionCoordination.Tick(State);
        _social.Tick(State);
        _socialClusters.Tick(State);
        _suspicion.Tick(State);
        _conversationPacing.Tick(State);
        _crewRoutines.Tick(State);
        _robots.Tick(State, turn);
        _turrets.Tick(State, turn);
        _movement.Tick(State, TimeSpan.FromMinutes(1));
        _prisonerContainment.FinalizeMovement(State);
        _crewDoors.Tick(State);
        _shutdown.Tick(State);
        _provisioning.Tick(State, turn);
        _maintenance.Tick(State);
        _comms.Tick(State);
        _accountComparison.Tick(State);
        _suspicionDynamics.Tick(State, turn);
        _memoryRetention.Tick(State);

        // Runs last among the health-affecting/accounting systems so it sees
        // each NPC's true end-of-tick Health, the same value IsAlive reads.
        _fearConditioning.Tick(State);
        _crewLifecycle.Tick(State);

        // Directives are graded before the station layer decides the outcome, so
        // its win gate reads this tick's directive results rather than the
        // previous tick's.
        _directives.Tick(State, turn);
        _scenarioProgress.Tick(State, turn);
    }

    protected void Log(string message)
    {
        var timestamp = State.Elapsed.ToString(@"hh\:mm");
        State.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
