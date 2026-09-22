using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

/// <summary>
/// Regression coverage for the shared turret/robot countermeasure decision,
/// which <c>RuleBasedAiDecisionService</c> and <c>BrowserMindSystem</c> both
/// used to reimplement byte-for-byte. Both now delegate to
/// <see cref="TurretCountermeasureSystem.FindCountermeasure"/> and
/// <see cref="RobotCountermeasureSystem.FindCountermeasure"/>; this exercises
/// that single shared decision directly.
/// </summary>
public sealed class CrewThreatCountermeasureConvergenceTests
{
    [Fact]
    public void Turret_VisibleArmedHostileWithPower_PrefersLocalDisarmOverDamage()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var turret = Assert.Single(state.Turrets);
        turret.Policy = TurretPolicy.SuppressCrew;
        turret.IsArmed = true;
        turret.PowerFeedEnabled = true;
        state.Facility.Rooms[turret.RoomId].IsPowered = true;
        npc.CurrentRoomId = turret.RoomId;
        npc.Skills["Engineering"] = 90;

        var decision = TurretCountermeasureSystem.FindCountermeasure(state, npc);

        Assert.NotNull(decision);
        Assert.Equal(ActionKind.DisarmTurret, decision!.Action);
        Assert.Equal(turret.Id, decision.TargetId);
        Assert.Equal(100, decision.Urgency);
    }

    [Fact]
    public void Turret_VisibleArmedHostile_LowSkillFallsBackToDamage()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var turret = Assert.Single(state.Turrets);
        turret.Policy = TurretPolicy.SuppressCrew;
        turret.IsArmed = true;
        turret.PowerFeedEnabled = true;
        state.Facility.Rooms[turret.RoomId].IsPowered = true;
        npc.CurrentRoomId = turret.RoomId;
        foreach (var skill in npc.Skills.Keys.ToList())
        {
            npc.Skills[skill] = 0;
        }
        npc.Skills["Athletics"] = 90;

        var decision = TurretCountermeasureSystem.FindCountermeasure(state, npc);

        Assert.NotNull(decision);
        Assert.Equal(ActionKind.DamageTurret, decision!.Action);
    }

    [Fact]
    public void Turret_VisibleUnarmedHostilePolicy_ReprogramsToSafe()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var turret = Assert.Single(state.Turrets);
        turret.Policy = TurretPolicy.SuppressCrew;
        turret.IsArmed = false;
        npc.CurrentRoomId = turret.RoomId;
        npc.Skills["Engineering"] = 90;

        var decision = TurretCountermeasureSystem.FindCountermeasure(state, npc);

        Assert.NotNull(decision);
        Assert.Equal(ActionKind.ReprogramTurret, decision!.Action);
        Assert.Equal(96, decision.Urgency);
    }

    [Fact]
    public void Turret_KnownHostileEvidenceWithoutVisibility_IsolatesNetwork()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var turret = Assert.Single(state.Turrets);
        npc.CurrentRoomId = "quarters";
        turret.IsNetworkIsolated = false;
        npc.Skills["Engineering"] = 90;
        npc.OverseerEvidence.Add(new OverseerEvidence(
            $"{turret.Name} tried to physically arm on a crew member.",
            5,
            state.Elapsed));

        var decision = TurretCountermeasureSystem.FindCountermeasure(state, npc);

        Assert.NotNull(decision);
        Assert.Equal(ActionKind.IsolateTurretNetwork, decision!.Action);
        Assert.Equal(turret.Id, decision.TargetId);
    }

    [Fact]
    public void Robot_VisibleOperationalHostileWithSkill_PrefersLocalShutdownOverDamage()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var robot = new StationRobot
        {
            Id = "robot-1",
            Name = "Maintenance Bot",
            CurrentRoomId = npc.CurrentRoomId,
            Policy = RobotPolicy.Hostile
        };
        state.Robots.Add(robot);
        npc.Skills["Engineering"] = 90;

        var decision = RobotCountermeasureSystem.FindCountermeasure(state, npc);

        Assert.NotNull(decision);
        Assert.Equal(ActionKind.ShutdownRobot, decision!.Action);
        Assert.Equal(robot.Id, decision.TargetId);
        Assert.Equal(100, decision.Urgency);
    }

    [Fact]
    public void Robot_VisibleOperationalHostile_LowTechnicalFallsBackToDamage()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var robot = new StationRobot
        {
            Id = "robot-1",
            Name = "Maintenance Bot",
            CurrentRoomId = npc.CurrentRoomId,
            Policy = RobotPolicy.Hostile
        };
        state.Robots.Add(robot);
        foreach (var skill in npc.Skills.Keys.ToList())
        {
            npc.Skills[skill] = 0;
        }

        var decision = RobotCountermeasureSystem.FindCountermeasure(state, npc);

        Assert.NotNull(decision);
        Assert.Equal(ActionKind.DamageRobot, decision!.Action);
    }

    [Fact]
    public void Robot_KnownHostileEvidenceWithoutVisibility_IsolatesNetwork()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];
        var robot = new StationRobot
        {
            Id = "robot-1",
            Name = "Maintenance Bot",
            CurrentRoomId = "quarters",
        };
        state.Robots.Add(robot);
        npc.CurrentRoomId = "medical";
        npc.Skills["Engineering"] = 90;
        npc.OverseerEvidence.Add(new OverseerEvidence(
            $"{robot.Name} moved with hostile intent toward crew.",
            5,
            state.Elapsed));

        var decision = RobotCountermeasureSystem.FindCountermeasure(state, npc);

        Assert.NotNull(decision);
        Assert.Equal(ActionKind.IsolateRobotNetwork, decision!.Action);
        Assert.Equal(robot.Id, decision.TargetId);
    }

    [Fact]
    public void NoThreatPresent_ReturnsNullForBothCountermeasures()
    {
        var state = FacilitySeeder.CreateDefault();
        var npc = state.Crew[0];

        Assert.Null(TurretCountermeasureSystem.FindCountermeasure(state, npc));
        Assert.Null(RobotCountermeasureSystem.FindCountermeasure(state, npc));
    }
}
