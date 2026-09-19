using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Web.Client.Services;

public sealed class GameSession
{
    private readonly SimulationEngine _simulation = new();

    public GameState State { get; private set; } = FacilitySeeder.CreateDefault();

    public void AdvanceOneMinute() =>
        _simulation.Tick(State, TimeSpan.FromMinutes(1));

    public void Reset() =>
        State = FacilitySeeder.CreateDefault();

    public void ToggleDoor(string doorId)
    {
        var door = State.Facility.Doors.First(door => door.Id == doorId);

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

        if (!door.IsLocked && door.IsOpen)
            door.IsOpen = false;

        door.IsLocked = !door.IsLocked;
        Log($"{door.Id} is now {(door.IsLocked ? "LOCKED" : "UNLOCKED")}.");
    }

    private void Log(string message)
    {
        var timestamp = State.Elapsed.ToString(@"hh\:mm");
        State.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
