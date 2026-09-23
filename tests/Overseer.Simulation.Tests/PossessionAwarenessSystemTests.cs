using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Owner idea #3, slice 3: ambient discovery of a possession someone is
/// visibly holding, mirroring <see cref="MedicalEvidenceSystem"/>'s
/// NoticeBlood pattern. Never omniscient.
/// </summary>
public sealed class PossessionAwarenessSystemTests
{
    [Fact]
    public void CoLocatedCrewLearnAboutAPossessionTheyCanSeeSomeoneHolding()
    {
        var state = FacilitySeeder.CreateDefault();
        var holder = state.Crew[0];
        var observer = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == holder.Id);
        observer.CurrentRoomId = holder.CurrentRoomId;

        Assert.DoesNotContain(possession.Id, observer.KnownPossessionIds);

        new PossessionAwarenessSystem().Tick(state);

        Assert.Contains(possession.Id, observer.KnownPossessionIds);
    }

    [Fact]
    public void CrewInADifferentRoomDoNotLearnAboutAHeldPossession()
    {
        var state = FacilitySeeder.CreateDefault();
        var holder = state.Crew[0];
        var observer = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == holder.Id);
        holder.CurrentRoomId = "storage";
        observer.CurrentRoomId = "control";

        new PossessionAwarenessSystem().Tick(state);

        Assert.DoesNotContain(possession.Id, observer.KnownPossessionIds);
    }

    [Fact]
    public void AHiddenPossessionIsNotNoticedAmbientlyByAnyoneElseInTheRoom()
    {
        var state = FacilitySeeder.CreateDefault();
        var owner = state.Crew[0];
        var observer = state.Crew[1];
        var possession = state.Possessions.First(p => p.OwnerId == owner.Id);
        possession.CurrentHolderId = null;
        possession.HiddenAtRoomId = "storage";
        observer.CurrentRoomId = "storage";

        new PossessionAwarenessSystem().Tick(state);

        Assert.DoesNotContain(possession.Id, observer.KnownPossessionIds);
    }
}
