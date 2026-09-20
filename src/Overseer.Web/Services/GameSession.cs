using Overseer.AI;
using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Web.Services;

public sealed class GameSession(
    IAiDecisionService aiDecisionService,
    IAiCrewGenerator crewGenerator,
    IOverseerMessageInterpreter messageInterpreter)
{
    private readonly IAiDecisionService _aiDecisionService = aiDecisionService;
    private readonly IAiCrewGenerator _crewGenerator = crewGenerator;
    private readonly IOverseerMessageInterpreter _messageInterpreter = messageInterpreter;
    private readonly SimulationEngine _simulation = new();
    private readonly EnvironmentSystem _environment = new();
    private readonly AirlockSafetySystem _airlockSafety = new();
    private readonly VacuumConsequenceSystem _vacuum = new();
    private readonly MissingPersonSystem _missingPeople = new();
    private readonly CrewCounterplaySystem _counterplay = new();
    private readonly CrewRoutineSystem _crewRoutines = new();
    private readonly SocialSimulationSystem _social = new();
    private readonly IntentExecutionSystem _intentExecution = new();
    private readonly InvestigationSystem _investigations = new();
    private readonly ShutdownCoordinationSystem _shutdownCoordination = new();
    private readonly ScenarioProgressSystem _scenarioProgress = new();
    private readonly NavigationSystem _navigation = new();
    private readonly LocalMovementSystem _movement = new();
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
    private readonly SimulationClock _clock = new();

    private int _mindCursor;
    private bool _initialized;

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
                || !room.AirlockSafetyInterlocksEnabled));

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
        Campaign = new CampaignState();
        var crew = await _crewGenerator.GenerateAsync(cancellationToken);
        State = FacilitySeeder.CreateDefault(crew);
        _initialized = true;
    }

    public void CaptureCampaignProgress() =>
        CampaignProgressionSystem.CaptureCompletedMission(Campaign, State);

    public bool TryResolveCampaignEnding(CampaignEndgameChoice choice) =>
        CampaignProgressionSystem.TryResolveEnding(Campaign, choice, out _);

    public async Task RestoreCampaignAsync(
        CampaignState campaign,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        _clock.Pause();
        _mindCursor = 0;
        Campaign = campaign;

        var continuingCrew = CampaignProgressionSystem.CreateContinuingCrew(Campaign);
        var crew = continuingCrew ?? await _crewGenerator.GenerateAsync(cancellationToken);
        State = FacilitySeeder.CreateDefault(crew);

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
        _initialized = true;
    }

    /// <summary>
    /// Starts only the next unlocked campaign assignment on a fresh station.
    /// Arbitrary scenario selection is intentionally rejected by the campaign
    /// layer instead of relying on UI controls for progression integrity.
    /// </summary>
    public async Task LoadScenarioAsync(
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        _clock.Pause();
        _mindCursor = 0;
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
        var crew = continuingCrew ?? await _crewGenerator.GenerateAsync(cancellationToken);
        State = FacilitySeeder.CreateDefault(crew);
        ScenarioCatalog.Apply(State, scenario);
        CampaignProgressionSystem.ApplyCarryOver(Campaign, State);
        Campaign.CurrentScenarioId = scenario.Id;
        _initialized = true;

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

    private async Task AdvanceCoreAsync(CancellationToken cancellationToken)
    {
        if (State.ScenarioStatus != ScenarioStatus.Running) return;
        var turn = TimeSpan.FromMinutes(1);
        _upkeep.Tick(State, turn);
        _environment.Tick(State, turn);
        _airlockSafety.Tick(State, turn);
        _vacuum.Tick(State);
        _simulation.Tick(State, turn);
        _missingPeople.Tick(State);

        await ThinkIfDueAsync(cancellationToken);

        _intentExecution.Tick(State);
        _investigations.Tick(State);
        _counterplay.Tick(State);
        _manualOverrides.Tick(State);
        _shutdownCoordination.Tick(State);
        _social.Tick(State);
        _suspicion.Tick(State);
        _conversationPacing.Tick(State);
        _crewRoutines.Tick(State);
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
