using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class SimulationClockTests
{
    [Fact]
    public void Pause_InvalidatesTheActiveGeneration()
    {
        var clock = new SimulationClock();
        var (_, generation) = clock.Start();

        Assert.True(clock.IsActive(generation));

        clock.Pause();

        Assert.False(clock.IsRunning);
        Assert.False(clock.IsActive(generation));
    }

    [Fact]
    public void Restart_DoesNotReactivateAStaleLoop()
    {
        var clock = new SimulationClock();
        var (_, firstGeneration) = clock.Start();

        clock.Pause();
        var (started, secondGeneration) = clock.Start();

        Assert.True(started);
        Assert.True(clock.IsActive(secondGeneration));
        Assert.False(clock.IsActive(firstGeneration));
    }

    [Fact]
    public void Start_WhileAlreadyRunning_DoesNotCreateAnotherGeneration()
    {
        var clock = new SimulationClock();
        var first = clock.Start();
        var second = clock.Start();

        Assert.True(first.Started);
        Assert.False(second.Started);
        Assert.Equal(first.Generation, second.Generation);
    }
}
