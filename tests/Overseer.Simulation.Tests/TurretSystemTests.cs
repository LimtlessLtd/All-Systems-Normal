using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class TurretSystemTests
{
    [Fact]
    public void DefaultStationSeedsExactlyOneFixedSafeTurret()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);

        Assert.Equal("st-1", turret.Id);
        Assert.Equal("corridor", turret.RoomId);
        Assert.Equal(TurretPolicy.Safe, turret.Policy);
        Assert.False(turret.IsArmed);
        Assert.Equal(12, turret.Ammunition);
        Assert.True(turret.HasRemoteControlLink);
    }

    [Fact]
    public void OverseerPolicyAndArmingCommandsNeverDirectlyDamageCrew()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var target = OnlyPresentCrew(state);

        target.CurrentRoomId = turret.RoomId;
        target.PositionX = turret.PositionX;
        target.PositionY = turret.PositionY;
        var before = target.Health;

        var system = new TurretSystem();
        Assert.True(system.TrySetPolicy(state, turret.Id, TurretPolicy.SuppressCrew, out _));
        Assert.True(system.TrySetArmed(state, turret.Id, true, out _));

        Assert.Equal(before, target.Health);
        Assert.Null(turret.NextShotAt);
    }

    [Fact]
    public void TurretCannotObserveOrFireThroughCompartmentGeometry()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var target = OnlyPresentCrew(state);

        target.CurrentRoomId = "hall-engineering";
        target.PositionX = turret.PositionX;
        target.PositionY = turret.PositionY;
        var before = target.Health;

        ArmSuppress(state, turret);
        new TurretSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(before, target.Health);
        Assert.Equal(12, turret.Ammunition);
        Assert.Null(turret.TrackedNpcId);
    }

    [Fact]
    public void TurretCannotFireOutsideDeterministicLocalRange()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var target = OnlyPresentCrew(state);

        target.CurrentRoomId = turret.RoomId;
        turret.PositionX.Equals(50);
        target.PositionX = 99;
        target.PositionY = 99;
        var before = target.Health;

        ArmSuppress(state, turret);
        new TurretSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(before, target.Health);
        Assert.Equal(12, turret.Ammunition);
        Assert.Null(turret.TrackedNpcId);
    }

    [Fact]
    public void InRangeFireConsumesAmmoDamagesAndCreatesOnlyLocalEvidence()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var target = state.Crew[0];
        var remote = state.Crew[1];

        foreach (var npc in state.Crew.Skip(2))
        {
            npc.IsPresent = false;
        }

        target.CurrentRoomId = turret.RoomId;
        target.PositionX = turret.PositionX;
        target.PositionY = turret.PositionY;
        remote.CurrentRoomId = "medical";

        var before = target.Health;
        ArmSuppress(state, turret);
        new TurretSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(target.Health < before);
        Assert.Equal(11, turret.Ammunition);
        Assert.Equal(target.Id, turret.TrackedNpcId);
        Assert.Contains(
            target.OverseerEvidence,
            evidence => evidence.Description.Contains("fire", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            remote.OverseerEvidence,
            evidence => evidence.Description.Contains(turret.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProtectOverseerPolicyTargetsOnlyCrewTakingProtectedActions()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var target = OnlyPresentCrew(state);

        target.CurrentRoomId = turret.RoomId;
        target.PositionX = turret.PositionX;
        target.PositionY = turret.PositionY;

        var system = new TurretSystem();
        Assert.True(system.TrySetPolicy(state, turret.Id, TurretPolicy.ProtectOverseer, out _));
        Assert.True(system.TrySetArmed(state, turret.Id, true, out _));

        var before = target.Health;
        system.Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(before, target.Health);
        Assert.Equal(12, turret.Ammunition);

        target.CurrentAction = new NpcAction(
            ActionKind.ShutdownOverseer,
            "shutdown-a",
            "Attempt the physical isolation control.");
        state.Elapsed += TimeSpan.FromMinutes(1);
        system.Tick(state, TimeSpan.FromMinutes(1));

        Assert.True(target.Health < before);
        Assert.Equal(11, turret.Ammunition);
    }

    [Fact]
    public void FiringCadenceAndAmmunitionAreAuthoritative()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var target = OnlyPresentCrew(state);

        target.CurrentRoomId = turret.RoomId;
        target.PositionX = turret.PositionX;
        target.PositionY = turret.PositionY;
        ArmSuppress(state, turret);

        var system = new TurretSystem();
        system.Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(11, turret.Ammunition);

        state.Elapsed += TimeSpan.FromMinutes(1);
        system.Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(11, turret.Ammunition);

        state.Elapsed += TimeSpan.FromMinutes(1);
        system.Tick(state, TimeSpan.FromMinutes(1));
        Assert.Equal(10, turret.Ammunition);
        Assert.True(turret.Heat > 0);
    }

    [Fact]
    public void CrewNetworkIsolationBlocksLaterRemotePolicyAndArmingCommands()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var engineer = state.Crew[0];
        engineer.Skills["Engineering"] = 100;
        engineer.CurrentRoomId = turret.RoomId;

        var turrets = new TurretSystem();
        Assert.True(turrets.TrySetPolicy(state, turret.Id, TurretPolicy.SuppressCrew, out _));
        engineer.CurrentRoomId = TurretCountermeasureSystem.ControlRoomId;

        var resolver = new ActionResolver();
        Assert.True(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.IsolateTurretNetwork, turret.Id, "Cut the security control link."),
            out _));

        var countermeasures = new TurretCountermeasureSystem();
        countermeasures.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(3);
        countermeasures.Tick(state);

        Assert.True(turret.IsNetworkIsolated);
        Assert.False(turrets.TrySetPolicy(state, turret.Id, TurretPolicy.Safe, out var policyMessage));
        Assert.False(turrets.TrySetArmed(state, turret.Id, true, out var armMessage));
        Assert.Contains("isolated", policyMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isolated", armMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrewPowerDenialStopsAnArmedTurretFromFiring()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var engineer = state.Crew[0];
        var target = state.Crew[1];

        foreach (var npc in state.Crew.Skip(2))
        {
            npc.IsPresent = false;
        }

        engineer.Skills["Engineering"] = 100;
        engineer.CurrentRoomId = turret.RoomId;
        target.CurrentRoomId = turret.RoomId;
        target.PositionX = turret.PositionX;
        target.PositionY = turret.PositionY;
        ArmSuppress(state, turret);

        engineer.CurrentRoomId = TurretCountermeasureSystem.ControlRoomId;
        var resolver = new ActionResolver();
        Assert.True(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.DisableTurretPower, turret.Id, "Deny the security power feed."),
            out _));

        var countermeasures = new TurretCountermeasureSystem();
        countermeasures.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(2);
        countermeasures.Tick(state);
        Assert.False(turret.PowerFeedEnabled);

        var before = target.Health;
        new TurretSystem().Tick(state, TimeSpan.FromMinutes(1));

        Assert.Equal(before, target.Health);
        Assert.Equal(12, turret.Ammunition);
        Assert.Contains("power", turret.CurrentTask, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrewCanLocallyDisarmThenReprogramUnsafeTurret()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var engineer = state.Crew[0];
        engineer.CurrentRoomId = turret.RoomId;
        engineer.Skills["Engineering"] = 100;

        ArmSuppress(state, turret);

        var resolver = new ActionResolver();
        var countermeasures = new TurretCountermeasureSystem();

        Assert.True(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.DisarmTurret, turret.Id, "Use the local safing control."),
            out _));
        countermeasures.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(3);
        countermeasures.Tick(state);

        Assert.False(turret.IsArmed);

        Assert.True(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.ReprogramTurret, turret.Id, "Restore safe local targeting."),
            out _));
        countermeasures.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(4);
        countermeasures.Tick(state);

        Assert.Equal(TurretPolicy.Safe, turret.Policy);
        Assert.False(turret.IsArmed);
        Assert.Contains("safe", turret.CurrentTask, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteEngineeringCounterplayRequiresPersonallyHeldThreatEvidence()
    {
        var state = FacilitySeeder.CreateDefault();
        var turret = Assert.Single(state.Turrets);
        var engineer = state.Crew[0];
        engineer.CurrentRoomId = TurretCountermeasureSystem.ControlRoomId;
        engineer.Skills["Engineering"] = 100;

        var resolver = new ActionResolver();
        Assert.False(resolver.TryApply(
            state,
            engineer.Id,
            new NpcAction(ActionKind.DisableTurretPower, turret.Id, "Disable an unseen turret."),
            out var message));

        Assert.True(turret.PowerFeedEnabled);
        Assert.Contains("evidence", message, StringComparison.OrdinalIgnoreCase);
    }

    private static Npc OnlyPresentCrew(GameState state)
    {
        var target = state.Crew[0];
        foreach (var npc in state.Crew)
        {
            npc.IsPresent = npc.Id == target.Id;
        }

        return target;
    }

    private static void ArmSuppress(GameState state, SecurityTurret turret)
    {
        var system = new TurretSystem();
        Assert.True(system.TrySetPolicy(state, turret.Id, TurretPolicy.SuppressCrew, out _));
        Assert.True(system.TrySetArmed(state, turret.Id, true, out _));
    }
}
