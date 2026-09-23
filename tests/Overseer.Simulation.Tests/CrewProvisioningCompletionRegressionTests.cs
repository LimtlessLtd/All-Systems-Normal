using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewProvisioningCompletionRegressionTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void CompletedCrewTaskFinalizesPlantingEvenWhenProvisioningTimerIsLater()
    {
        var state = FacilitySeeder.CreateDefault(stationSeed: 1337);
        var worker = state.Crew[0];
        var room = state.Facility.Rooms["hydroponics"];
        var bed = state.CropBeds.First();
        var fixture = room.Fixtures.Single(item =>
            item.Type == FixtureType.GrowBed && item.Label == bed.FixtureLabel);

        worker.CurrentRoomId = room.Id;
        worker.PositionX = fixture.InteractionX ?? fixture.X;
        worker.PositionY = fixture.InteractionY ?? fixture.Y;
        worker.ProvisioningJob = ActionKind.TendCrops;
        worker.ProvisioningRoomId = room.Id;
        worker.TendingBedId = bed.Id;
        worker.CurrentAction = new NpcAction(ActionKind.TendCrops, bed.Id, "Plant assigned bay.");

        var system = new CrewProvisioningSystem();
        system.Tick(state, Minute);

        Assert.Equal(CropLifecycleState.Planting, bed.Lifecycle);
        Assert.NotNull(worker.ActiveTask);
        Assert.Equal(CrewTaskStatus.InProgress, worker.ActiveTask.Status);

        var taskCompletesAt = worker.ActiveTask.CompletesAt;
        worker.ProvisioningCompletesAt = taskCompletesAt + TimeSpan.FromMinutes(30);
        state.Elapsed = taskCompletesAt;

        Assert.Equal(100, worker.ActiveTask.ProgressPercent(state.Elapsed), 6);
        system.Tick(state, Minute);

        Assert.Equal(CropLifecycleState.Seedling, bed.Lifecycle);
        Assert.Equal(CrewTaskStatus.Succeeded, worker.ActiveTask.Status);
        Assert.Null(worker.ProvisioningJob);
        Assert.Null(worker.ProvisioningCompletesAt);
    }
}
