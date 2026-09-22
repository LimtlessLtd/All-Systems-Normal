using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Lightweight deterministic cognition for the static GitHub Pages build.
/// It exercises the same persistent-intent pipeline as the LLM without
/// shipping model credentials or pretending the browser demo is LLM-backed.
/// </summary>
public sealed class BrowserMindSystem
{
    private readonly NavigationSystem _navigation = new();

    public void Tick(GameState state)
    {
        HandleEmergencyReconsiderations(state);

        if (HandleEventReconsideration(state))
        {
            return;
        }

        var minute = (int)Math.Floor(state.Elapsed.TotalMinutes);

        if (minute <= 0 || minute % 6 != 0)
        {
            return;
        }

        var crew = state.Crew
            .Where(npc =>
                npc.IsAlive
                && npc.IsPresent
                && !npc.IsContainmentBreachInProgress
                && !CrewTaskSystem.IsWorking(npc))
            .OrderBy(npc => npc.Name)
            .ToList();

        if (crew.Count == 0)
        {
            return;
        }

        var npc = crew[(minute / 6) % crew.Count];

        if (npc.Intent is not null)
        {
            return;
        }

        SetIntent(state, npc, Decide(npc, state), NpcBubbleKind.Thought);
    }

    private bool HandleEventReconsideration(GameState state)
    {
        var npc = state.Crew
            .Where(candidate =>
                candidate.IsAlive
                && candidate.IsPresent
                && !candidate.IsContainmentBreachInProgress
                && candidate.NeedsMindReconsideration
                && !CrewTaskSystem.IsWorking(candidate)
                && !CrewEnvironmentSafety.IsDangerous(
                    state.Facility.Rooms[candidate.CurrentRoomId])
                && (candidate.Intent is null
                    || candidate.Intent.Urgency < 85
                    || candidate.Hunger >= 72
                    || candidate.Fatigue >= 86))
            .OrderByDescending(candidate =>
                candidate.MissingPersonConcerns.Values.Any(concern =>
                    concern.Stage == MissingPersonConcernStage.Escalated))
            .ThenBy(candidate => candidate.Name)
            .FirstOrDefault();

        if (npc is null)
        {
            return false;
        }

        npc.Intent = null;
        npc.Movement = null;
        npc.RoutineUntil = TimeSpan.Zero;

        SetIntent(
            state,
            npc,
            Decide(npc, state),
            npc.MissingPersonConcerns.Values.Any(concern =>
                concern.Stage == MissingPersonConcernStage.Escalated)
                || npc.ObservedUnsafeAirlocks.Count > 0
                ? NpcBubbleKind.Alert
                : NpcBubbleKind.Thought);

        npc.NeedsMindReconsideration = false;
        return true;
    }

    private void HandleEmergencyReconsiderations(GameState state)
    {
        foreach (var npc in state.Crew
                     .Where(npc =>
                         npc.IsAlive
                         && npc.IsPresent
                         && !npc.IsContainmentBreachInProgress)
                     .OrderByDescending(npc =>
                         CrewEnvironmentSafety.RiskScore(
                             state.Facility.Rooms[npc.CurrentRoomId])))
        {
            var currentRoom = state.Facility.Rooms[npc.CurrentRoomId];

            if (!CrewEnvironmentSafety.IsDangerous(currentRoom)
                || IsAlreadyEscapingToSaferRoom(state, npc, currentRoom))
            {
                continue;
            }

            var fightFire = currentRoom.FireIntensity > 0 && ShouldFightFire(npc, currentRoom);
            var saferRoom = fightFire ? null : FindSaferRoom(state, npc, currentRoom);
            var blockingDoor = !fightFire && saferRoom is null
                ? FindBlockingDoorTowardSaferRoom(state, npc, currentRoom)
                : null;

            if (saferRoom is null
                && blockingDoor is null
                && state.Elapsed - npc.LastThoughtAt < TimeSpan.FromMinutes(3)
                && npc.LastThought.Contains(
                    "cannot identify a safer room",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Emergency cognition pre-empts a low-priority routine before the
            // deterministic action/navigation layers validate the new goal.
            npc.Intent = null;
            npc.Movement = null;
            npc.RoutineUntil = TimeSpan.Zero;

            var intent = fightFire
                ? Create(
                    state,
                    ActionKind.FightFire,
                    currentRoom.Id,
                    $"Suppress the fire in {currentRoom.Name}.",
                    "There is an active fire here and I believe I can help contain it.",
                    100)
                : saferRoom is not null
                ? Create(
                    state,
                    ActionKind.Move,
                    saferRoom.Id,
                    $"Get to {saferRoom.Name} now.",
                    $"The environment in {currentRoom.Name} is dangerous; {saferRoom.Name} is safer.",
                    100)
                : blockingDoor is not null
                    ? Create(
                        state,
                        ActionKind.ForceDoor,
                        blockingDoor.Id,
                        $"Get {blockingDoor.Id} open and escape.",
                        "A sealed hatch is the only thing between me and a safer compartment.",
                        100)
                    : Create(
                        state,
                        ActionKind.Idle,
                        null,
                        "Shelter and call for emergency help.",
                        "The environment is dangerous and I cannot identify a safer room.",
                        100);

            SetIntent(state, npc, intent, NpcBubbleKind.Alert);
        }
    }

    private NpcIntent Decide(Npc npc, GameState state)
    {
        var currentRoom = state.Facility.Rooms[npc.CurrentRoomId];

        if (CrewEnvironmentSafety.IsDangerous(currentRoom))
        {
            if (currentRoom.FireIntensity > 0 && ShouldFightFire(npc, currentRoom))
            {
                return Create(
                    state,
                    ActionKind.FightFire,
                    currentRoom.Id,
                    $"Fight the fire in {currentRoom.Name}.",
                    "The compartment is burning and I think staying to suppress it is worth the risk.",
                    98);
            }

            var saferRoom = FindSaferRoom(state, npc, currentRoom);

            if (saferRoom is not null)
            {
                return Create(
                    state,
                    ActionKind.Move,
                    saferRoom.Id,
                    $"Get to {saferRoom.Name}.",
                    "The atmosphere or temperature here is becoming dangerous.",
                    96);
            }

            var blockingDoor = FindBlockingDoorTowardSaferRoom(
                state,
                npc,
                currentRoom);

            if (blockingDoor is not null)
            {
                return Create(
                    state,
                    ActionKind.ForceDoor,
                    blockingDoor.Id,
                    $"Open {blockingDoor.Id} and get out.",
                    "The hatch is blocking my route out of a dangerous compartment.",
                    99);
            }

            return Create(
                state,
                ActionKind.Idle,
                null,
                "Shelter and call for emergency help.",
                "The environment is dangerous and I cannot identify a safer room.",
                98);
        }

        // Critical bodily needs get an immediate chance to supersede long
        // technical/social plans. This is still cognition choosing the goal,
        // not the world layer issuing a scripted command.
        if (npc.Hunger >= 72)
        {
            return Create(
                state,
                ActionKind.Eat,
                null,
                "Find food now.",
                "I am hungry enough that continuing to ignore it is dangerous.",
                92);
        }

        if (npc.Fatigue >= 86)
        {
            return Create(
                state,
                ActionKind.Sleep,
                null,
                "Get sleep now.",
                "I am dangerously exhausted and need to stop.",
                90);
        }

        var repairSkill = CrewCounterplaySystem.BestRepairSkill(npc);

        if (FindSecurityMalwareResponse(state, npc) is { } malwareResponse)
        {
            return malwareResponse;
        }

        if (FindTurretCountermeasure(state, npc) is { } turretCountermeasure)
        {
            return turretCountermeasure;
        }

        if (FindRobotCountermeasure(state, npc) is { } robotCountermeasure)
        {
            return robotCountermeasure;
        }

        if (FindPerceivedUnsafeAirlock(state, npc) is { } unsafeAirlock)
        {
            return Create(
                state,
                ActionKind.SecureAirlock,
                unsafeAirlock.Id,
                $"Secure {unsafeAirlock.Name}.",
                "I can see the airlock safety state is compromised and I know the emergency controls.",
                94);
        }

        if (!state.LifeSupport.IsOnline && repairSkill >= 55)
        {
            return Create(
                state,
                ActionKind.RestoreSystem,
                CrewCounterplaySystem.LifeSupportTarget,
                "Bring primary life support back online.",
                "People are at risk and I have enough technical ability to attempt the repair.",
                90);
        }

        // Basic survival comes before curiosity. These used to sit below the
        // investigation and suspicion branches, so a crew member who suspected
        // Overseer would investigate indefinitely while starving — and, because
        // the deterministic routine was meanwhile steering them to the kitchen,
        // they oscillated across a doorway and never arrived anywhere at all.
        if (npc.Hunger >= 58)
        {
            return Create(
                state,
                ActionKind.Eat,
                null,
                "Find something to eat.",
                "I am getting hungry and want a proper meal.",
                75);
        }

        if (npc.Fatigue >= 68)
        {
            return Create(
                state,
                ActionKind.Sleep,
                null,
                "Get some sleep.",
                "I am exhausted enough that I should sleep.",
                72);
        }

        if (npc.BladderNeed >= 72)
        {
            return Create(
                state,
                ActionKind.UseToilet,
                null,
                "Use the washroom.",
                "I really need the toilet.",
                82);
        }

        if (MostPressingMissingConcern(npc) is { } missingConcern
            && FindMissingSearchRoom(state, npc, missingConcern) is { } searchRoom)
        {
            return Create(
                state,
                ActionKind.Investigate,
                searchRoom.Id,
                $"Look for {missingConcern.PersonName} in {searchRoom.Name}.",
                MissingConcernReason(state, missingConcern),
                missingConcern.Stage == MissingPersonConcernStage.Escalated ? 80 : 52);
        }

        if (npc.PendingShutdownTeamInvitation is { } invitation
            && ShouldJoinShutdownTeam(npc, invitation))
        {
            return Create(
                state,
                ActionKind.JoinShutdownTeam,
                invitation.TeamId,
                "Join the proposed Overseer isolation team.",
                $"{invitation.FromNpcName} asked for coordinated help and I take the claim seriously enough to join.",
                90);
        }

        if (npc.OverseerSuspicion >= 38
            && FindInvestigationLead(state, npc) is { } investigationLead)
        {
            var leadRoom = state.Facility.Rooms[investigationLead.RoomId];
            return Create(
                state,
                ActionKind.Investigate,
                leadRoom.Id,
                $"Investigate {leadRoom.Name}.",
                investigationLead.Description,
                npc.OverseerSuspicion >= 60 ? 91 : 74);
        }

        if (npc.OverseerSuspicion >= 65
            && FindKnownShutdownMechanism(state, npc) is { } knownMechanism)
        {
            var team = FindShutdownTeam(state, npc, knownMechanism);
            if (team is null || team.MemberIds.Count < knownMechanism.RequiredCrewCount)
            {
                var recruit = FindShutdownRecruit(state, npc, team);
                if (recruit is not null)
                {
                    return Create(
                        state,
                        ActionKind.RecruitShutdownAlly,
                        recruit.Name,
                        $"Recruit {recruit.Name} to help isolate Overseer.",
                        $"I verified {knownMechanism.Label}, but operating it safely requires coordinated crew.",
                        94);
                }
            }
            else
            {
                return Create(
                    state,
                    ActionKind.ShutdownOverseer,
                    knownMechanism.Id,
                    $"Reach {knownMechanism.Label} with the team and isolate Overseer.",
                    "I have verified the hardware and enough crew have committed to the same plan.",
                    98);
            }
        }

        if (CrewCounterplaySystem.HasRestorableProblem(state, currentRoom.Id)
            && repairSkill >= 55)
        {
            return Create(
                state,
                ActionKind.RestoreSystem,
                currentRoom.Id,
                $"Restore {currentRoom.Name}.",
                "A disabled local system is interfering with safety or my work.",
                66);
        }


        if (npc.HygieneNeed >= 60)
        {
            return Create(
                state,
                ActionKind.Shower,
                null,
                "Take a shower.",
                "I feel grimy and want to clean up.",
                64);
        }

        if (npc.RecreationNeed >= 58)
        {
            return Create(
                state,
                ActionKind.Recreate,
                null,
                "Take a proper break.",
                "I need time to unwind instead of working constantly.",
                55);
        }

        var tense = npc.Relationships.Values
            .OrderByDescending(r => r.Resentment)
            .FirstOrDefault();

        if (tense is { Resentment: >= 48 })
        {
            return Create(
                state,
                ActionKind.Argue,
                tense.PersonName,
                $"Confront {tense.PersonName}.",
                $"I am increasingly irritated with {tense.PersonName}.",
                62);
        }

        var trusted = npc.Relationships.Values
            .OrderByDescending(r => r.Trust + r.Affinity)
            .FirstOrDefault();

        if (trusted is not null
            && npc.Personality.Sociability >= 50
            && npc.SocialNeed >= 68)
        {
            return Create(
                state,
                ActionKind.Socialize,
                trusted.PersonName,
                $"Talk to {trusted.PersonName}.",
                $"I feel socially isolated and comfortable around {trusted.PersonName}.",
                45);
        }

        // Browser fallback exercises the same broader affordance contract as
        // Ollama. Choices are deterministic but deliberately varied by traits,
        // relationships and role so the static build is not a scripted demo.
        if (trusted is not null
            && npc.OverseerSuspicion >= 48
            && npc.Relationships.TryGetValue(trusted.PersonName, out var trustedRelation))
        {
            return Create(
                state,
                ActionKind.ReportConcern,
                trusted.PersonName,
                $"Compare concerns with {trusted.PersonName}.",
                "I want another human perspective before deciding what the station AI is doing.",
                54);
        }

        if (tense is { Resentment: >= 30 }
            && npc.Personality.Empathy < 35)
        {
            return Create(
                state,
                ActionKind.MisleadCrew,
                tense.PersonName,
                $"Keep {tense.PersonName} away from what I am doing.",
                "I do not trust them and would rather steer them elsewhere than cooperate.",
                41);
        }

        // Check on whoever is struggling most, not whoever sorts first by name.
        var colleague = state.Crew
            .Where(other =>
                other.IsAlive
                && other.IsPresent
                && other.Id != npc.Id
                && other.Stress >= 55)
            .OrderByDescending(other => other.Stress)
            .ThenBy(other => other.Name)
            .FirstOrDefault();

        if (colleague is not null
            && npc.Personality.Empathy >= 65)
        {
            return Create(
                state,
                ActionKind.CheckOnCrew,
                colleague.Name,
                $"Check on {colleague.Name}.",
                "They look stressed enough that I want to make sure they are all right.",
                43);
        }

        if (npc.Role is CrewRole.Engineer or CrewRole.Technician
            && state.Devices.Values.Any(device =>
                device.RoomId.Equals(currentRoom.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return Create(
                state,
                ActionKind.InspectEquipment,
                currentRoom.Id,
                $"Inspect equipment in {currentRoom.Name}.",
                "A quiet moment is a good chance to check local machinery before it becomes a fault.",
                28);
        }

        if (npc.Role == CrewRole.Security
            && state.Facility.Doors.Any(door =>
                CrewDoorInteractionSystem.IsAdjacent(npc, door)))
        {
            return Create(
                state,
                ActionKind.StandGuard,
                currentRoom.Id,
                $"Watch access through {currentRoom.Name}.",
                "Nothing is immediately wrong, but monitoring movement is part of my job.",
                25);
        }

        // Leaving this as Idle intentionally hands low-pressure time back to
        // CrewRoutineSystem, whose broad role routes make people circulate.
        return Create(
            state,
            ActionKind.Idle,
            null,
            "Stay alert and continue normal duties.",
            "Nothing feels urgent enough to interrupt my routine.",
            15);
    }

    private NpcIntent? FindSecurityMalwareResponse(GameState state, Npc npc)
    {
        if (!state.SecurityMalware.IsActive
            || !SecurityMalwareSystem.HasMalwareEvidence(npc))
        {
            return null;
        }

        var technical = CrewCounterplaySystem.BestTechnicalSkill(npc);
        var controllerRoom = state.Facility.Rooms[SecurityMalwareSystem.ControllerRoomId];

        if (state.SecurityMalware.Stage == SecurityMalwareStage.Active)
        {
            if (SecurityMalwareSystem.CanIsolate(state, npc))
            {
                return Create(
                    state,
                    ActionKind.IsolateSecurityController,
                    SecurityMalwareSystem.ControllerTargetId,
                    "Physically isolate the compromised MR/ST security controller.",
                    "Local diagnostics show malicious signed commands on the security controller. I want to cut its remote links before attempting cleanup.",
                    99);
            }

            if (technical >= 55
                && !npc.CurrentRoomId.Equals(
                    SecurityMalwareSystem.ControllerRoomId,
                    StringComparison.OrdinalIgnoreCase)
                && _navigation.FindPathForCrew(
                    state,
                    npc,
                    npc.CurrentRoomId,
                    SecurityMalwareSystem.ControllerRoomId).Count >= 2)
            {
                return Create(
                    state,
                    ActionKind.Move,
                    controllerRoom.Id,
                    $"Reach {controllerRoom.Name} to isolate the compromised security controller.",
                    "I have grounded diagnostics of a security-controller compromise and need physical access to contain it.",
                    96);
            }
        }

        if (state.SecurityMalware.Stage == SecurityMalwareStage.Isolated)
        {
            if (SecurityMalwareSystem.CanPurge(state, npc))
            {
                return Create(
                    state,
                    ActionKind.PurgeSecurityController,
                    SecurityMalwareSystem.ControllerTargetId,
                    "Purge and reimage the isolated MR/ST security controller.",
                    "The malicious controller is physically isolated. A clean reimage can remove the compromised policies without delegating any combat outcome to software.",
                    98);
            }

            if (technical >= 65
                && !npc.CurrentRoomId.Equals(
                    SecurityMalwareSystem.ControllerRoomId,
                    StringComparison.OrdinalIgnoreCase)
                && _navigation.FindPathForCrew(
                    state,
                    npc,
                    npc.CurrentRoomId,
                    SecurityMalwareSystem.ControllerRoomId).Count >= 2)
            {
                return Create(
                    state,
                    ActionKind.Move,
                    controllerRoom.Id,
                    $"Reach {controllerRoom.Name} to reimage the isolated security controller.",
                    "The controller is contained but still compromised; I have the technical skill to finish recovery locally.",
                    93);
            }
        }

        return null;
    }

    private static NpcIntent? FindTurretCountermeasure(GameState state, Npc npc)
    {
        var visibleTurret = state.Turrets.FirstOrDefault(turret =>
            !turret.IsDestroyed
            && turret.RoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

        if (visibleTurret is not null)
        {
            var technical = CrewCounterplaySystem.BestTechnicalSkill(npc);
            var force = CrewCounterplaySystem.BestForceSkill(npc);

            if (!visibleTurret.IsArmed
                && visibleTurret.Policy != TurretPolicy.Safe
                && technical >= 65)
            {
                return Create(
                    state,
                    ActionKind.ReprogramTurret,
                    visibleTurret.Id,
                    $"Reprogram {visibleTurret.Name} to safe targeting.",
                    "The security turret is disarmed in front of me and its local service controller is accessible.",
                    96);
            }

            if (visibleTurret.IsArmed
                && visibleTurret.Policy != TurretPolicy.Safe
                && TurretSystem.HasPower(state, visibleTurret))
            {
                return technical >= 45
                    ? Create(
                        state,
                        ActionKind.DisarmTurret,
                        visibleTurret.Id,
                        $"Use {visibleTurret.Name}'s local safing controls.",
                        "An armed hostile security turret is physically here; I want to safe it locally.",
                        100)
                    : force >= 40
                        ? Create(
                            state,
                            ActionKind.DamageTurret,
                            visibleTurret.Id,
                            $"Physically sabotage {visibleTurret.Name}.",
                            "The armed turret is an immediate local threat and physical sabotage is the countermeasure I can attempt.",
                            100)
                        : null;
            }
        }

        var knownThreat = state.Turrets.FirstOrDefault(turret =>
            !turret.IsDestroyed
            && TurretCountermeasureSystem.HasHostileTurretEvidence(npc, turret));

        if (knownThreat is null)
        {
            return null;
        }

        var technicalSkill = CrewCounterplaySystem.BestTechnicalSkill(npc);
        if (!knownThreat.IsNetworkIsolated && technicalSkill >= 55)
        {
            return Create(
                state,
                ActionKind.IsolateTurretNetwork,
                knownThreat.Id,
                $"Isolate {knownThreat.Name} from Overseer's security network.",
                "I have direct evidence the turret is dangerous and Engineering has a physical network isolation control.",
                95);
        }

        if (knownThreat.PowerFeedEnabled && technicalSkill >= 45)
        {
            return Create(
                state,
                ActionKind.DisableTurretPower,
                knownThreat.Id,
                $"Cut the dedicated power feed to {knownThreat.Name}.",
                "I have direct evidence the turret is dangerous and can deny its security power feed from Engineering.",
                92);
        }

        return null;
    }

    private static NpcIntent? FindRobotCountermeasure(GameState state, Npc npc)
    {
        var visibleRobot = state.Robots.FirstOrDefault(robot =>
            !robot.IsDestroyed
            && robot.CurrentRoomId.Equals(npc.CurrentRoomId, StringComparison.OrdinalIgnoreCase));

        if (visibleRobot is not null)
        {
            if (!visibleRobot.IsOperational
                && visibleRobot.Policy == RobotPolicy.Hostile
                && CrewCounterplaySystem.BestTechnicalSkill(npc) >= 65)
            {
                return Create(
                    state,
                    ActionKind.ReprogramRobot,
                    visibleRobot.Id,
                    $"Reprogram and reboot {visibleRobot.Name}.",
                    "The hostile robot is shut down in front of me and I can reach its local service port.",
                    96);
            }

            if (visibleRobot.Policy == RobotPolicy.Hostile && visibleRobot.IsOperational)
            {
                var technical = CrewCounterplaySystem.BestTechnicalSkill(npc);
                var force = CrewCounterplaySystem.BestForceSkill(npc);

                return technical >= 45
                    ? Create(
                        state,
                        ActionKind.ShutdownRobot,
                        visibleRobot.Id,
                        $"Use {visibleRobot.Name}'s manual shutdown.",
                        "A hostile robot is physically here; I want to stop it at the local emergency cutoff.",
                        100)
                    : Create(
                        state,
                        ActionKind.DamageRobot,
                        visibleRobot.Id,
                        $"Physically disable {visibleRobot.Name}.",
                        force >= 40
                            ? "A hostile robot is physically here and force is the countermeasure I can attempt."
                            : "The robot is an immediate threat; I have no safer technical option.",
                        100);
            }
        }

        var knownThreat = state.Robots.FirstOrDefault(robot =>
            !robot.IsDestroyed
            && RobotCountermeasureSystem.HasHostileRobotEvidence(npc, robot));

        if (knownThreat is null)
        {
            return null;
        }

        var technicalSkill = CrewCounterplaySystem.BestTechnicalSkill(npc);
        if (!knownThreat.IsNetworkIsolated && technicalSkill >= 55)
        {
            return Create(
                state,
                ActionKind.IsolateRobotNetwork,
                knownThreat.Id,
                $"Isolate {knownThreat.Name} from Overseer's control link.",
                "I have direct evidence the robot is dangerous and Engineering has a physical network isolation control.",
                94);
        }

        if (knownThreat.ChargingEnabled && technicalSkill >= 45)
        {
            return Create(
                state,
                ActionKind.DisableRobotCharging,
                knownThreat.Id,
                $"Cut power to {knownThreat.Name}'s charging circuit.",
                "I have direct evidence the robot is dangerous and can deny its charger from Engineering.",
                88);
        }

        return null;
    }

    private bool ShouldJoinShutdownTeam(
        Npc npc,
        ShutdownTeamInvitation invitation)
    {
        var trust = npc.Relationships.TryGetValue(
            invitation.FromNpcName,
            out var relationship)
            ? relationship.Trust
            : 50;

        return npc.OverseerSuspicion >= 50 && trust >= 35;
    }

    private InvestigationLead? FindInvestigationLead(
        GameState state,
        Npc npc) =>
        npc.InvestigationLeads.Values
            .Where(lead =>
                lead.Stage == InvestigationLeadStage.Open
                && state.Facility.Rooms.ContainsKey(lead.RoomId)
                && (npc.CurrentRoomId.Equals(lead.RoomId, StringComparison.OrdinalIgnoreCase)
                    || _navigation.FindPathForCrew(
                        state,
                        npc,
                        npc.CurrentRoomId,
                        lead.RoomId).Count >= 2))
            .OrderByDescending(lead =>
                lead.Id.Contains("team-claim", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(lead =>
                lead.Id.Contains("shutdown", StringComparison.OrdinalIgnoreCase))
            .ThenBy(lead => lead.CreatedAt)
            .FirstOrDefault();

    private static ShutdownMechanism? FindKnownShutdownMechanism(
        GameState state,
        Npc npc) =>
        state.ShutdownMechanisms
            .Where(mechanism =>
                mechanism.IsOnline
                && npc.KnownShutdownMechanismIds.Contains(mechanism.Id))
            .OrderBy(mechanism => mechanism.Id)
            .FirstOrDefault();

    private static ShutdownTeam? FindShutdownTeam(
        GameState state,
        Npc npc,
        ShutdownMechanism mechanism) =>
        state.ShutdownTeams.FirstOrDefault(team =>
            team.IsActive
            && team.MechanismId.Equals(mechanism.Id, StringComparison.OrdinalIgnoreCase)
            && team.MemberIds.Contains(npc.Id));

    private static Npc? FindShutdownRecruit(
        GameState state,
        Npc npc,
        ShutdownTeam? team)
    {
        var excluded = team?.MemberIds ?? new HashSet<Guid>();
        return state.Crew
            .Where(other =>
                other.IsAlive
                && other.IsPresent
                && other.Id != npc.Id
                && !excluded.Contains(other.Id)
                && (team is null || !team.InvitedNpcIds.Contains(other.Id)))
            .OrderByDescending(other =>
                npc.Relationships.TryGetValue(other.Name, out var relation)
                    ? relation.Trust + relation.Affinity
                    : 100)
            .ThenBy(other => other.Name)
            .FirstOrDefault();
    }

    private static Room? FindPerceivedUnsafeAirlock(
        GameState state,
        Npc npc) =>
        state.Facility.Rooms.Values
            .Where(room =>
                room.Type == RoomType.Airlock
                && room.HasExteriorHatch
                && AirlockSafetySystem.NeedsCrewSecuring(state, room)
                && AirlockSafetySystem.CanCrewSecure(npc)
                && AirlockSafetySystem.CanPerceiveSafetyState(state, npc, room))
            .OrderBy(room => room.Id)
            .FirstOrDefault();

    private static MissingPersonConcern? MostPressingMissingConcern(Npc npc) =>
        npc.MissingPersonConcerns.Values
            .Where(concern => concern.Stage != MissingPersonConcernStage.Concerned)
            .OrderByDescending(concern => concern.Stage)
            .ThenBy(concern => concern.FirstConcernAt)
            .FirstOrDefault();

    private Room? FindMissingSearchRoom(
        GameState state,
        Npc npc,
        MissingPersonConcern concern)
    {
        var candidateIds = new[]
        {
            concern.ExpectedRoomId,
            concern.LastKnownRoomId,
            "quarters",
            "kitchen",
            "lounge",
            "medical",
            "control"
        };

        foreach (var roomId in candidateIds
                     .Where(roomId => !string.IsNullOrWhiteSpace(roomId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (roomId is null
                || concern.CheckedRoomIds.Contains(roomId)
                || !state.Facility.Rooms.TryGetValue(roomId, out var room))
            {
                continue;
            }

            if (npc.CurrentRoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                || _navigation.FindPathForCrew(
                    state,
                    npc,
                    npc.CurrentRoomId,
                    room.Id).Count >= 2)
            {
                return room;
            }
        }

        return null;
    }

    private static string MissingConcernReason(
        GameState state,
        MissingPersonConcern concern)
    {
        var expected = state.Facility.Rooms[concern.ExpectedRoomId].Name;

        return concern.LastSeenAt is { } seenAt
            ? $"I last saw {concern.PersonName} at T+{seenAt:hh\\:mm}; they missed expected duty around {expected}."
            : $"I have not seen {concern.PersonName} this shift and they missed expected duty around {expected}.";
    }

    private static bool ShouldFightFire(Npc npc, Room room)
    {
        var courage = Math.Clamp(
            npc.Personality.Courage + CrewTraitMath.Modifier(npc, TraitEffectKind.Courage),
            0,
            100);
        var practical = Math.Max(
            npc.Skills.GetValueOrDefault("Engineering"),
            npc.Skills.GetValueOrDefault("Security"));
        return room.FireIntensity <= 58
            && npc.Stress < 88
            && npc.Fatigue < 88
            && (practical >= 45 || courage >= 72);
    }

    private bool IsAlreadyEscapingToSaferRoom(
        GameState state,
        Npc npc,
        Room currentRoom)
    {
        if (npc.Intent is { Action: ActionKind.ForceDoor })
        {
            return true;
        }

        if (npc.Intent is not { Action: ActionKind.Move or ActionKind.SeekSafety or ActionKind.EvacuateHazard, TargetId: { } targetId }
            || !state.Facility.Rooms.TryGetValue(targetId, out var targetRoom))
        {
            return false;
        }

        if (CrewEnvironmentSafety.RiskScore(targetRoom)
            >= CrewEnvironmentSafety.RiskScore(currentRoom))
        {
            return false;
        }

        return _navigation.FindPathForCrew(
            state,
            npc,
            currentRoom.Id,
            targetRoom.Id).Count >= 2;
    }

    private Door? FindBlockingDoorTowardSaferRoom(
        GameState state,
        Npc npc,
        Room currentRoom)
    {
        var currentRisk = CrewEnvironmentSafety.RiskScore(currentRoom);

        var candidate = state.Facility.Rooms.Values
            .Where(room =>
                room.Id != currentRoom.Id
                && room.Type != RoomType.Corridor
                && CrewEnvironmentSafety.RiskScore(room) + 0.1 < currentRisk)
            .Select(room => new
            {
                Room = room,
                Risk = CrewEnvironmentSafety.RiskScore(room),
                Path = _navigation.FindPathIgnoringDoorState(
                    state.Facility,
                    currentRoom.Id,
                    room.Id)
            })
            .Where(item => item.Path.Count >= 2)
            .OrderBy(item => CrewEnvironmentSafety.IsHabitable(item.Room) ? 0 : 1)
            .ThenBy(item => item.Risk)
            .ThenBy(item => item.Path.Count)
            .FirstOrDefault();

        if (candidate is null)
        {
            return null;
        }

        var door = state.Facility.FindDoorBetween(
            candidate.Path[0],
            candidate.Path[1]);

        return door is { IsPassable: false, CanBeForced: true }
            && !CrewDoorInteractionSystem.CanOpenForTraversal(state, npc, door)
            ? door
            : null;
    }

    private Room? FindSaferRoom(GameState state, Npc npc, Room currentRoom)
    {
        var currentRisk = CrewEnvironmentSafety.RiskScore(currentRoom);

        return state.Facility.Rooms.Values
            .Where(room =>
                room.Id != currentRoom.Id
                && room.Type != RoomType.Corridor)
            .Select(room => new
            {
                Room = room,
                Risk = CrewEnvironmentSafety.RiskScore(room),
                Path = _navigation.FindPathForCrew(
                    state,
                    npc,
                    currentRoom.Id,
                    room.Id)
            })
            .Where(candidate =>
                candidate.Path.Count >= 2
                && candidate.Risk + 0.1 < currentRisk)
            .OrderBy(candidate =>
                CrewEnvironmentSafety.IsHabitable(candidate.Room) ? 0 : 1)
            .ThenBy(candidate => candidate.Risk)
            .ThenBy(candidate => candidate.Path.Count)
            .ThenBy(candidate =>
                Math.Abs(candidate.Room.MapX - currentRoom.MapX)
                + Math.Abs(candidate.Room.MapY - currentRoom.MapY))
            .ThenBy(candidate => candidate.Room.Id)
            .Select(candidate => candidate.Room)
            .FirstOrDefault();
    }

    private static void SetIntent(
        GameState state,
        Npc npc,
        NpcIntent intent,
        NpcBubbleKind bubbleKind)
    {
        npc.Intent = intent;
        npc.MindMode = "Browser demo";
        npc.LastThought = intent.Reason;
        npc.LastThoughtAt = state.Elapsed;
        npc.Bubble = new NpcBubble(
            intent.Goal,
            bubbleKind,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(
                bubbleKind == NpcBubbleKind.Alert ? 4 : 3));

        AudioCueSystem.Emit(
            state,
            bubbleKind == NpcBubbleKind.Alert
                ? AudioCueKind.Warning
                : AudioCueKind.Thought,
            npc.Id.ToString(),
            npc.CurrentRoomId);

        CognitionTelemetrySystem.Record(
            state,
            npc,
            "Browser demo",
            intent);
    }

    private static NpcIntent Create(
        GameState state,
        ActionKind action,
        string? target,
        string goal,
        string reason,
        int urgency) =>
        new(action, target, goal, reason, urgency, "Browser demo", state.Elapsed);
}
