namespace Overseer.Simulation;

/// <summary>
/// Authoritative run/pause gate for real-time UI loops. A generation token makes
/// stale or duplicated loops harmless: once paused or restarted, old generations
/// can no longer advance the simulation.
/// </summary>
public sealed class SimulationClock
{
    private long _generation;

    public bool IsRunning { get; private set; }
    public long Generation => _generation;

    public (bool Started, long Generation) Start()
    {
        if (IsRunning)
        {
            return (false, _generation);
        }

        IsRunning = true;
        _generation++;
        return (true, _generation);
    }

    public void Pause()
    {
        IsRunning = false;
        _generation++;
    }

    public bool IsActive(long generation) =>
        IsRunning && generation == _generation;
}
