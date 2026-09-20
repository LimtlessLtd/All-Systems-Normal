using Overseer.Domain;
using Overseer.Simulation;

namespace Overseer.Simulation.Tests;

public sealed class CrewCounterplaySystemTests
{
    [Fact]
    public void SkilledNpcCanChooseAndCompleteTechnicalDoorBypass()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var door = state.Facility.FindDoorBetween("engineering", "hall-engineering")!;

        door.IsOpen = false;
        door.IsLocked = true;

        sarah.Intent = new NpcIntent(
            ActionKind.ForceDoor,
            door.Id,
            "Open the hatch.",
            "I need a route out.",
            90,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);
        Assert.Equal(ActionKind.ForceDoor, sarah.CurrentAction.Kind);

        var system = new CrewCounterplaySystem();
        system.Tick(state);
        Assert.True(sarah.RoutineUntil > state.Elapsed);

        state.Elapsed += TimeSpan.FromMinutes(10);
        system.Tick(state);

        Assert.True(door.IsPassable);
        Assert.True(door.IsManuallyOverridden);
        Assert.False(door.IsAiControllable);
        Assert.False(door.IsDamaged);
    }

    [Fact]
    public void StrongNpcCanPhysicallyForceDoorAndDamageIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var marcus = state.Crew.Single(npc => npc.Name == "Marcus Reed");
        var door = state.Facility.Doors.Single(candidate =>
            (candidate.RoomAId == "hall-storage" || candidate.RoomBId == "hall-storage")
            && !candidate.Connects("storage", "hall-storage"));
        var networkRoomId = door.RoomAId == "hall-storage"
            ? door.RoomBId
            : door.RoomAId;

        marcus.CurrentRoomId = networkRoomId;
        marcus.Skills["Security"] = 100;
        marcus.Skills["Athletics"] = 100;
        door.IsOpen = false;
        door.IsLocked = true;
        door.TechnicalDifficulty = 120;
        door.ForceDifficulty = 60;

        var resolver = new ActionResolver();
        Assert.True(resolver.TryApply(
            state,
            marcus.Id,
            new NpcAction(ActionKind.ForceDoor, door.Id, "Force it open."),
            out _));

        var system = new CrewCounterplaySystem();
        system.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(10);
        system.Tick(state);

        Assert.True(door.IsPassable);
        Assert.True(door.IsDamaged);
    }

    [Fact]
    public void SkilledNpcCanRestorePlayerDisabledRoomSystem()
    {
        var state = FacilitySeeder.CreateDefault();
        var felix = state.Crew.Single(npc => npc.Name == "Felix Ward");
        var generator = state.Facility.Rooms["generator"];
        generator.IsPowered = false;

        felix.Intent = new NpcIntent(
            ActionKind.RestoreSystem,
            "generator",
            "Restore generator power.",
            "The station needs this machinery.",
            85,
            "Test",
            state.Elapsed);

        new IntentExecutionSystem().Tick(state);
        Assert.Equal(ActionKind.RestoreSystem, felix.CurrentAction.Kind);

        var system = new CrewCounterplaySystem();
        system.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(10);
        system.Tick(state);

        Assert.True(generator.IsPowered);
        Assert.Equal(ActionKind.Idle, felix.CurrentAction.Kind);
    }

    [Fact]
    public void TraitModifiersChangeCounterplaySkill()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var baseEngineering = sarah.Skills["Engineering"];

        var technical = CrewCounterplaySystem.BestTechnicalSkill(sarah);

        Assert.True(technical > baseEngineering);
        Assert.Contains(
            sarah.Traits,
            trait => trait.Effects.Any(effect =>
                effect.Kind == TraitEffectKind.Technical
                && effect.Modifier > 0));
    }
    [Fact]
    public void ForcedDamagePersistsUntilSkilledCrewRepairIt()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var door = state.Facility.FindDoorBetween("engineering", "hall-engineering")!;
        sarah.CurrentRoomId = "engineering";
        door.IsDamaged = true;
        door.IsTechnicallyBypassed = true;
        door.IsManuallyOverridden = true;
        door.StructuralIntegrityPercent = 35;
        door.IsAiControllable = false;

        Assert.True(new ActionResolver().TryApply(state, sarah.Id,
            new NpcAction(ActionKind.RepairDoor, door.Id, "Repair hatch."), out _));
        var system = new CrewCounterplaySystem();
        system.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(10);
        system.Tick(state);

        Assert.False(door.IsDamaged);
        Assert.False(door.IsTechnicallyBypassed);
        Assert.False(door.IsManuallyOverridden);
        Assert.Equal(100, door.StructuralIntegrityPercent);
        Assert.True(door.IsAiControllable);
    }

    [Fact]
    public void WeldedAndBarricadedDoorsArePhysicallyImpassableAndNotAiControllable()
    {
        var state = FacilitySeeder.CreateDefault();
        var sarah = state.Crew.Single(npc => npc.Name == "Sarah Chen");
        var door = state.Facility.FindDoorBetween("engineering", "hall-engineering")!;
        sarah.CurrentRoomId = "engineering";
        sarah.Skills["Engineering"] = 100;
        door.IsOpen = false;
        door.IsLocked = true;

        Assert.True(new ActionResolver().TryApply(state, sarah.Id,
            new NpcAction(ActionKind.WeldDoor, door.Id, "Seal hatch."), out _));
        var system = new CrewCounterplaySystem();
        system.Tick(state);
        state.Elapsed += TimeSpan.FromMinutes(10);
        system.Tick(state);

        Assert.True(door.IsWelded);
        Assert.False(door.IsPassable);
        Assert.False(door.IsAiControllable);
        Assert.Equal(sarah.Name, door.SecuredByNpcName);
    }

}
