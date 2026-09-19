using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Web.Client.Services;

public sealed class GameSession
{
    private readonly SimulationEngine _simulation = new();
    private readonly CrewRoutineSystem _crewRoutines = new();
    private readonly SocialSimulationSystem _social = new();
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
        + State.Crew.Count(npc => !npc.IsAlive);

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

    public void ToggleDoor(string doorId)
    {
        var door = State.Facility.Doors.First(door => door.Id == doorId);

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
        Log($"{door.Id} is now {(door.IsOpen ? "OPEN" : "CLOSED")}.");
    }

    public void ToggleLock(string doorId)
    {
        var door = State.Facility.Doors.First(door => door.Id == doorId);

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

    private void AdvanceCore()
    {
        _simulation.Tick(State, TimeSpan.FromMinutes(1));
        _social.Tick(State);
        _crewRoutines.Tick(State);
    }

    private void Log(string message)
    {
        var timestamp = State.Elapsed.ToString(@"hh\:mm");
        State.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
